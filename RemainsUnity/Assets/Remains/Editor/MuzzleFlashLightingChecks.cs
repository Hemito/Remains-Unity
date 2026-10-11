using System;
using System.Reflection;
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
                // Fully closed barrier separates the two sides, with the emitter far from it.
                for(int i=0;i<8;i++) field.Render();
                var baseline=Read(Scene(field,"scene"));
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
                for(int i=0;i<8;i++) Require(field.RenderTransient(Array.Empty<RoomRadianceField.SpriteEmission>()),$"Затухание закончено раньше восьмого расчёта: {i}.");
                float cleared=Mean(Read(Shader.GetGlobalTexture("_RemainsRadiance")),64,16,24,24,32);
                Require(cleared<initial+.002f+nearby*.02f,$"Свет остался после удаления вспышки: вспышка={nearby}, остаток={cleared}.");
                Require(!field.RenderTransient(Array.Empty<RoomRadianceField.SpriteEmission>()),"Стабильное поле продолжает пересчитываться.");
                Debug.Log($"Дульная вспышка: PASS — GPU излучение в пустом пространстве, цвет маски, неизменное поглощение, стена, затухание; рядом={nearby}, за стеной={shadow}, остаток={cleared}.");
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
        private static void Require(bool condition,string message)
        {if(!condition) throw new InvalidOperationException(message);}
    }
}
