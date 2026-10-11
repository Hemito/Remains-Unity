using System;
using System.Reflection;
using System.Collections.Generic;
using Remains.Presentation;
using Remains.RoomFormat;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class MuzzleFlashLightingChecks
    {
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }

        public static void Run()
        {
            Remains.Content.WeaponCatalog.Load();
            Remains.Domain.Anatomy.Load();
            WeaponShotChecks.Run();
            RoomRadianceField field=null;
            Texture2D mask=null;
            Sprite sprite=null;
            try
            {
                var room=new RoomFile {Template="remains:check:muzzle-light",Width=8,Height=6,Cell=1};
                room.Options.Add("dark","0");room.Options.Add("color","");room.Options.Add("lon","0");
                var table=new RoomLightingTable {RadianceResolution=8,RadiancePower=1,RadianceLampSize=.2,
                    RadianceAlbedo=.35,ShadowEdge=.1,ObjectRimWidth=.1,ObjectLightMode="gradient"};
                field=RoomRadianceField.Build(room,new RoomDefinitionCatalog(),new RoomLandCatalog(),table,Vector2.zero,null,
                    occluders:new[]{(new Rect(4,0,1,6),1d)});
                // A static emitter beside the wall ensures the lamp bounce history is nonzero.
                var staticScene=(Texture2D)Scene(field,"scene");
                staticScene.SetPixel(24,24,new Color(2,1,.5f,1));staticScene.Apply();
                // Fully closed barrier separates the two sides, with the emitter far from it.
                for(int i=0;i<8;i++) field.Render();
                var baseline=Read(Scene(field,"scene"));
                var baselineLight=Read(Shader.GetGlobalTexture("_RemainsRadiance"));
                var bounceBefore=Read(Scene(field,"bounceA"));
                var bounceOtherBefore=Read(Scene(field,"bounceB"));
                float reflected=0;
                foreach(var pixel in bounceBefore) reflected+=pixel.r;
                Require(reflected>.01f,"Лампа не создала отражений для проверки изоляции вспышки.");
                float initial=Mean(Read(Shader.GetGlobalTexture("_RemainsRadiance")),64,16,24,24,32);
                mask=new Texture2D(4,4,TextureFormat.RGBA32,false,true) {filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
                var pixels=new Color[16];
                for(int i=0;i<pixels.Length;i++) pixels[i]=new Color(.8f,.25f,.05f,1);
                mask.SetPixels(pixels);mask.Apply();
                sprite=Sprite.Create(mask,new Rect(0,0,4,4),new Vector2(.5f,.5f),4);
                var emissions=new[]{new RoomRadianceField.SpriteEmission(sprite,
                    Matrix4x4.TRS(new Vector3(1.5f,3,0),Quaternion.identity,Vector3.one),10)};
                Require(field.RenderTransient(emissions),"Вспышка не вызвала расчёт света.");
                var composed=Read(Scene(field,"transientScene"));
                Require(composed.Length==baseline.Length,"Изменился размер сцены света.");
                float sourceRed=0,sourceGreen=0,sourceBlue=0;
                for(int i=0;i<baseline.Length;i++)
                {
                    Require(Mathf.Abs(composed[i].a-baseline[i].a)<.00001f,$"Вспышка изменила поглощение препятствия в текселе {i}.");
                    sourceRed+=composed[i].r;sourceGreen+=composed[i].g;sourceBlue+=composed[i].b;
                }
                Require(sourceRed>sourceGreen*2 && sourceGreen>sourceBlue*2,"Цвет исходной маски вспышки потерян.");
                var lit=Read(Shader.GetGlobalTexture("_RemainsRadiance"));
                float nearby=Mean(lit,64,16,24,24,32);
                float shadow=Mean(lit,64,44,52,24,32);
                Require(nearby>initial+.02f,$"Вспышка в пустом пространстве не освещает соседние тексели: исходное={initial}, вспышка={nearby}.");
                Require(shadow<nearby*.02f,$"Свет вспышки прошёл через сплошную стену: рядом={nearby}, за стеной={shadow}.");
                for(int i=0;i<3;i++)
                {
                    field.RenderTransient(emissions);
                    Equal(Read(Scene(field,"bounceA")),bounceBefore,"Вспышка изменила основную историю отражений лампы.");
                    Equal(Read(Scene(field,"bounceB")),bounceOtherBefore,"Вспышка изменила вторую историю отражений лампы.");
                    Equal(Read(Shader.GetGlobalTexture("_RemainsRadiance")),lit,"Повторная вспышка накопила свет.");
                }
                Require(field.RenderTransient(Array.Empty<RoomRadianceField.SpriteEmission>()),"Удаление вспышки не восстановило свет ламп.");
                Equal(Read(Shader.GetGlobalTexture("_RemainsRadiance")),baselineLight,"После вспышки изменился свет ламп или остался световой хвост.");
                float cleared=Mean(Read(Shader.GetGlobalTexture("_RemainsRadiance")),64,16,24,24,32);
                Require(cleared<initial+.002f+nearby*.02f,$"Свет остался после удаления вспышки: вспышка={nearby}, остаток={cleared}.");
                Require(!field.RenderTransient(Array.Empty<RoomRadianceField.SpriteEmission>()),"Стабильное поле продолжает пересчитываться.");
                var blocker=new[]{new RoomRadianceField.LightOccluder(sprite,
                    Matrix4x4.TRS(new Vector3(2.25f,3,0),Quaternion.identity,new Vector3(.25f,4,1)))};
                field.RenderTransient(emissions,blocker);
                float blocked=Mean(Read(Scene(field,"directFluence")),64,24,28,24,32);
                field.RenderTransient(emissions);
                float unblocked=Mean(Read(Scene(field,"directFluence")),64,24,28,24,32);
                Require(unblocked>.02f && blocked<unblocked*.25f,$"Динамический силуэт не отбрасывает тень: открыто={unblocked}, тень={blocked}.");
                var movedBlocker=new[]{new RoomRadianceField.LightOccluder(sprite,
                    Matrix4x4.TRS(new Vector3(2.25f,5.5f,0),Quaternion.identity,new Vector3(.25f,.5f,1)))};
                field.RenderTransient(emissions,movedBlocker);
                float moved=Mean(Read(Scene(field,"directFluence")),64,24,28,24,32);
                Require(moved>unblocked*.8f,$"Тень осталась после перемещения силуэта: открыто={unblocked}, перемещено={moved}.");
                Equal(Read(Scene(field,"bounceA")),bounceBefore,"Динамический силуэт изменил отражения лампы.");
                field.RenderTransient(Array.Empty<RoomRadianceField.SpriteEmission>());
                Equal(Read(Shader.GetGlobalTexture("_RemainsRadiance")),baselineLight,"После тени не восстановился свет лампы.");
                Debug.Log($"Тени динамических силуэтов: PASS — открыто={unblocked}, тень={blocked}, перемещено={moved}.");
                CheckActorSilhouette(field,emissions,baseline);
                Debug.Log($"Дульная вспышка: PASS — прямой GPU свет, цвет, неизменное поглощение, стена, история отражений лампы сохранена, повтор без накопления, мгновенное восстановление; рядом={nearby}, за стеной={shadow}, восстановлено={cleared}, отражение лампы={reflected}.");
            }
            finally
            {
                if(field!=null) UnityEngine.Object.DestroyImmediate(field.gameObject);
                if(sprite!=null) UnityEngine.Object.DestroyImmediate(sprite);
                if(mask!=null) UnityEngine.Object.DestroyImmediate(mask);
            }
        }

        private static Texture Scene(RoomRadianceField field,string name)
            => (Texture)typeof(RoomRadianceField).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(field);

        private static void CheckActorSilhouette(RoomRadianceField field,RoomRadianceField.SpriteEmission[] emissions,Color[] baseline)
        {
            Remains.Content.ActorTypes.Load();Remains.Content.PropCatalog.Load();PonyVisual.Prepare();
            var actor=new Remains.Domain.ActorState {id="shadow-pony",actorType="pony",anatomy="pony",player=true,connected=true,
                level="hub",pose="idle",x=2.6f,y=2.2f,appearance=new Remains.Domain.CharacterAppearance {version=Remains.Domain.CharacterAppearance.CurrentVersion}};
            var visual=(PonyVisual)ActorVisualFactory.Create(actor);
            try
            {
                visual.ConfigureMagicPreview(1);visual.Bind(actor);visual.DrawPose(0);
                var silhouettes=new List<RoomRadianceField.LightOccluder>();
                visual.AppendLightOccluders(silhouettes);
                Require(silhouettes.Count>0,"Пони не передал нарисованные части для тени.");
                foreach(var part in silhouettes) Require(part.Mesh!=null && part.Texture!=null,"Часть силуэта пони потеряла mesh/texture.");
                field.RenderTransient(emissions,silhouettes);
                var scene=Read(Scene(field,"transientScene"));
                int changed=0;
                for(int i=0;i<scene.Length;i++) if(scene[i].a>baseline[i].a+.5f) changed++;
                Require(changed>4,$"Нарисованный пони не перекрывает свет на GPU: текселей={changed}.");
                actor.facing=-1;visual.DrawPose(0);silhouettes.Clear();visual.AppendLightOccluders(silhouettes);
                Require(silhouettes[0].LocalToWorld.determinant<0,"Зеркальность тела не передана в силуэт тени.");
                visual.SetVisible(false);silhouettes.Clear();visual.AppendLightOccluders(silhouettes);
                Require(silhouettes.Count==0,"Скрытый пони продолжает отбрасывать тень.");
                field.RenderTransient(Array.Empty<RoomRadianceField.SpriteEmission>());
                Debug.Log($"Силуэт настоящего пони: PASS — GPU поглощение, зеркальность, скрытие; текселей={changed}.");
            }
            finally {UnityEngine.Object.DestroyImmediate(visual.gameObject);}
        }

        private static Color[] Read(Texture source)
        {
            if(source==null) throw new InvalidOperationException("Нет GPU-текстуры для проверки вспышки.");
            var target=RenderTexture.GetTemporary(source.width,source.height,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var previous=RenderTexture.active;
            Texture2D copy=null;
            try
            {
                Graphics.Blit(source,target);RenderTexture.active=target;
                copy=new Texture2D(source.width,source.height,TextureFormat.RGBAFloat,false,true);
                copy.ReadPixels(new Rect(0,0,source.width,source.height),0,0);copy.Apply();return copy.GetPixels();
            }
            finally {RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);if(copy!=null) UnityEngine.Object.DestroyImmediate(copy);}
        }

        private static float Mean(Color[] pixels,int width,int x0,int x1,int y0,int y1)
        {
            float total=0;
            for(int y=y0;y<y1;y++) for(int x=x0;x<x1;x++) total+=pixels[y*width+x].r;
            return total/((x1-x0)*(y1-y0));
        }
        private static void Equal(Color[] actual,Color[] expected,string message)
        {
            Require(actual.Length==expected.Length,message);
            for(int i=0;i<actual.Length;i++)
                Require(actual[i]==expected[i],$"{message} Тексель={i}, было={expected[i]}, стало={actual[i]}.");
        }
        private static void Require(bool condition,string message)
        {if(!condition) throw new InvalidOperationException(message);}
    }
}
