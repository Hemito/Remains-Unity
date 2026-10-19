using System;
using System.Collections.Generic;
using System.Reflection;
using Remains.Presentation;
using Remains.Domain;
using Remains.RoomFormat;
using Remains.Content;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Remains.Editor
{
    public static class StaticLightShadowChecks
    {
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }

        public static void Run()
        {
            CheckWindowReplacementHistory();
            CheckCoincidentEmitterBlend();
            RoomRadianceField field=null;
            Texture2D emitterMask=null,shadowMask=null;
            Sprite sprite=null;
            try
            {
                var room=new RoomFile {Template="remains:check:steady-shadows",Width=8,Height=6,Cell=1};
                room.Options.Add("dark","0");room.Options.Add("color","");room.Options.Add("lon","0");
                var table=new RoomLightingTable {RadianceResolution=8,RadiancePower=1,RadianceLampSize=.2,
                    RadianceAlbedo=.35,ShadowEdge=.1,ObjectRimWidth=.1,ObjectLightMode="gradient"};
                var barrier=new[]{(new Rect(4,0,1,6),1d)};
                field=RoomRadianceField.Build(room,new RoomDefinitionCatalog(),new RoomLandCatalog(),table,Vector2.zero,null,occluders:barrier);
                var scene=(Texture2D)PrivateTexture(field,"baseScene");
                // Exact grid-aligned source footprints, red selected lamp and green independent lamp.
                Paint(scene,8,16,20,28,new Color(2,0,0,1));
                Paint(scene,20,28,20,28,new Color(0,2,0,1));scene.Apply();
                emitterMask=Mask();sprite=Sprite.Create(emitterMask,new Rect(0,0,2,2),new Vector2(.5f,.5f),2);
                var sources=new[]{
                    new SteadyLightEmitter("red",sprite,Matrix4x4.Translate(new Vector3(1.5f,3,0)),new Color(2,0,0,1)),
                    new SteadyLightEmitter("green",sprite,Matrix4x4.Translate(new Vector3(3,3,0)),new Color(0,2,0,1))};
                field.RegisterSteadyEmitters(sources);field.UpdateOccluders(barrier);
                for(int i=0;i<8;i++) field.Render();
                var baseline=Read(Shader.GetGlobalTexture("_RemainsRadiance"));
                var bounceA=Read(PrivateTexture(field,"bounceA"));var bounceB=Read(PrivateTexture(field,"bounceB"));
                Require(Sum(bounceA,0)>.01f,"Для проверки постоянной тени не получены красные отражения от стены.");
                field.PrepareSteadyShadowFields(new[]{"red","green"});
                Require(field.SteadyShadowFieldCount==2,"Не подготовлены отдельные поля двух постоянных источников.");
                Require(field.TryGetSteadyShadowField("red",out var directTexture),"Не найден прямой вклад красной лампы.");
                var direct=Read(directTexture);
                Require(Sum(direct,0)>.01f && Sum(direct,1)<.001f,"Поле выбранной лампы содержит чужой цвет или не содержит прямого света.");
                Equal(Read(Shader.GetGlobalTexture("_RemainsRadiance")),baseline,"Подготовка вкладов изменила освещение актёров.");
                shadowMask=Mask();var bounds=new Rect(0,0,8,6);
                Compose(field,new[]{new ProjectedLightShadow("red",shadowMask,bounds,1)});
                var masked=Read(Shader.GetGlobalTexture("_RemainsBackgroundRadiance"));
                ExpectedSubtraction(masked,baseline,direct,1);
                Require(Sum(masked,0)>.01f,"Полная тень лампы погасила её отражённый свет вместе с прямым.");
                for(int i=0;i<baseline.Length;i++) Close(masked[i].g,baseline[i].g,"Тень красной лампы убрала зелёный свет",i);
                Equal(Read(Shader.GetGlobalTexture("_RemainsRadiance")),baseline,"Постоянная тень затронула освещение актёров.");
                Equal(Read(PrivateTexture(field,"bounceA")),bounceA,"Постоянная тень изменила первую историю отражений.");
                Equal(Read(PrivateTexture(field,"bounceB")),bounceB,"Постоянная тень изменила вторую историю отражений.");
                for(int i=0;i<3;i++)
                {
                    Compose(field,new[]{new ProjectedLightShadow("red",shadowMask,bounds,1)});
                    Equal(Read(Shader.GetGlobalTexture("_RemainsBackgroundRadiance")),masked,"Повторная постоянная тень накапливает затемнение.");
                }
                var half=new ProjectedLightShadow("red",shadowMask,bounds,.5f);
                Compose(field,new[]{half});var oneHalf=Read(Shader.GetGlobalTexture("_RemainsBackgroundRadiance"));
                ExpectedSubtraction(oneHalf,baseline,direct,.5f);
                Compose(field,new[]{half,half});
                Equal(Read(Shader.GetGlobalTexture("_RemainsBackgroundRadiance")),oneHalf,"Маски двух актёров одного источника суммируются вместо объединения Max.");
                Compose(field,Array.Empty<ProjectedLightShadow>());
                Equal(Read(Shader.GetGlobalTexture("_RemainsRadiance")),baseline,"Снятие тени изменило освещение актёров.");
                if(Shader.GetGlobalFloat("_RemainsBackgroundRadianceEnabled")>0)
                    Equal(Read(Shader.GetGlobalTexture("_RemainsBackgroundRadiance")),baseline,"Снятие тени не восстановило фон.");
                CheckOverlappingSources(field,scene,sprite,shadowMask,barrier);
                int generation=field.SteadyShadowGeneration,count=field.SteadyShadowFieldCount;
                Require(field.TryGetSteadyShadowField("red",out var beforeField),"Нет поля источника до ревизии заслонки.");
                var beforePixels=Read(beforeField);
                field.UpdateOccluders(new[]{(new Rect(2.5f,0,.25f,6),1d)});
                // A door revision keeps the fields as stale until they are rebuilt one per frame (FactoryLevelView).
                Require(field.SteadyShadowGeneration>generation && field.HasStaleSteadyShadowFields && field.SteadyShadowFieldCount==count,
                    "Ревизия заслонки не пометила поля постоянных источников устаревшими.");
                while(field.RebuildOneStaleSteadyShadowField()) {}
                bool rebuilt=field.TryGetSteadyShadowField("red",out var afterField);
                Require(!field.HasStaleSteadyShadowFields && rebuilt && !ReferenceEquals(afterField,beforeField),
                    "Устаревшее поле источника не пересобрано после ревизии.");
                bool differs=false;var afterPixels=Read(afterField);
                for(int i=0;i<afterPixels.Length && !differs;i++) differs=(afterPixels[i]-beforePixels[i]).maxColorComponent>1e-4f || (beforePixels[i]-afterPixels[i]).maxColorComponent>1e-4f;
                Require(differs,"Пересобранное поле источника не учло новую заслонку.");
                Debug.Log("Постоянные тени: PASS — только прямой вклад выбранной лампы, сохранены отражения/другой источник/актёры, Max масок, без накопления, ревизия: поля устаревают и пересобираются с новой заслонкой.");
            }
            finally
            {
                if(field!=null) UnityEngine.Object.DestroyImmediate(field.gameObject);
                if(sprite!=null) UnityEngine.Object.DestroyImmediate(sprite);
                if(emitterMask!=null) UnityEngine.Object.DestroyImmediate(emitterMask);
                if(shadowMask!=null) UnityEngine.Object.DestroyImmediate(shadowMask);
            }
        }

        private static void CheckWindowReplacementHistory()
        {
            // Reproduce a new GPU cache after a window move, with an old actor crossfade.
            using var cache=new SteadyLightShadowCache();
            cache.Register(Array.Empty<SteadyLightEmitter>(),new[]{"window:previous"});
            int builds=0,warnings=0;
            void Log(string message,string trace,LogType type) {if(type==LogType.Warning) warnings++;}
            Application.logMessageReceived+=Log;
            try
            {
                cache.Prepare(new[]{"window:previous"},_=>{builds++;return null;});
                Require(builds==0 && warnings==0 && cache.Count==0,"Предыдущий источник окна не распознан как выбывший после замены GPU-кеша.");
                cache.Prepare(new[]{"window:unknown"},_=>{builds++;return null;});
                Require(builds==0 && warnings==1,"Неизвестный ID источника перестал диагностироваться.");
            }
            finally {Application.logMessageReceived-=Log;}
        }

        public static void RunAuthoredWindow()
        {
            GameObject room=null;Texture2D mask=null;
            try
            {
                room=AuthoredRoomVisual.Create("hub",RoomLayout.Load("hub"));
                var view=room.GetComponent<FactoryLevelView>();
                var windows=(Dictionary<RoomSpriteAuthoring,string>)typeof(FactoryLevelView)
                    .GetField("windowSourceIds",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view);
                RoomSpriteAuthoring picture=null;string sourceId=null;
                foreach(var candidate in windows) {picture=candidate.Key;sourceId=candidate.Value;break;}
                Require(picture!=null && picture.Erase!=null && sourceId.StartsWith("window:",StringComparison.Ordinal),
                    "В настоящем хабе не найден авторский источник окна со стирающим спрайтом.");
                Vector2 center=picture.transform.TransformPoint(picture.Erase.bounds.center);
                view.Focus(center);
                Require(view.IsReady,"Поле настоящего хаба не готово после фокуса на окне.");
                var field=(RoomRadianceField)typeof(FactoryLevelView).GetField("field",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view);
                var cache=(SteadyLightShadowCache)typeof(RoomRadianceField).GetField("steadyShadows",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(field);
                var catalog=(Dictionary<string,SteadyLightEmitter>)typeof(SteadyLightShadowCache)
                    .GetField("catalog",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(cache);
                Require(catalog.TryGetValue(sourceId,out var emitter),$"Поле хаба не зарегистрировало окно {sourceId}.");
                Require(emitter.Sprite==picture.Erase,$"Источник окна {sourceId} использует иной спрайт вместо настоящего Erase.");
                var baseline=Read(Shader.GetGlobalTexture("_RemainsRadiance"));
                view.PrepareActorShadowFields(new[]{sourceId});
                Require(field.TryGetSteadyShadowField(sourceId,out var directTexture),$"Не подготовлен прямой вклад настоящего окна {sourceId}.");
                var direct=Read(directTexture);Require(Sum(direct,0)+Sum(direct,1)+Sum(direct,2)>.01f,$"Настоящее окно {sourceId} не излучает прямой свет.");
                Equal(Read(Shader.GetGlobalTexture("_RemainsRadiance")),baseline,"Подготовка источника окна изменила свет актёров.");
                var world=Shader.GetGlobalVector("_RemainsRadianceRect");mask=Mask();
                Compose(field,new[]{new ProjectedLightShadow(sourceId,mask,new Rect(world.x,world.y,world.z,world.w),1)});
                ExpectedSubtraction(Read(Shader.GetGlobalTexture("_RemainsBackgroundRadiance")),baseline,direct,1);
                Equal(Read(Shader.GetGlobalTexture("_RemainsRadiance")),baseline,"Тень настоящего окна изменила свет актёров.");
                Debug.Log($"Окно настоящего хаба: PASS — ID={sourceId}, Erase={picture.Erase.name}, центр={center}, прямой вклад={Sum(direct,0)+Sum(direct,1)+Sum(direct,2)}; фон без собственного прямого света, актёры неизменны.");
            }
            finally
            {
                if(room!=null) UnityEngine.Object.DestroyImmediate(room);
                if(mask!=null) UnityEngine.Object.DestroyImmediate(mask);
            }
        }

        private static void CheckCoincidentEmitterBlend()
        {
            RoomRadianceField field=null;Texture2D mask=null;Sprite sprite=null;
            try
            {
                mask=Mask();sprite=Sprite.Create(mask,new Rect(0,0,2,2),new Vector2(.5f,.5f),2);
                var definition=new RoomDefinition {Id="check:coincident",Kind=RoomDefinitionKind.Decor};
                var table=new RoomLightingTable {RadianceResolution=8,RadiancePower=1,RadianceLampSize=.2,
                    RadianceAlbedo=.35,ShadowEdge=.1,ObjectRimWidth=.1,ObjectLightMode="gradient"};
                table.Lamps.Add(definition.Id,new RoomLamp {Intensity=1,Color=new[]{1d,.5d,.25d}});
                var placement=new RoomSpritePlacement {Definition=definition,Sprite=sprite,
                    LocalToWorld=Matrix4x4.Translate(new Vector3(1.5f,3,0))};
                var room=new RoomFile {Template="remains:check:coincident-emitters",Width=8,Height=6,Cell=1};
                room.Options.Add("dark","0");room.Options.Add("color","");room.Options.Add("lon","0");
                field=RoomRadianceField.Build(room,new RoomDefinitionCatalog(),new RoomLandCatalog(),table,Vector2.zero,null,
                    sprites:new[]{placement,placement});
                var pixels=Read(PrivateTexture(field,"staticScene"));
                field.UpdateSprites(new[]{placement},Array.Empty<(Rect,double)>());
                var single=Read(PrivateTexture(field,"staticScene"));
                for(int y=20;y<28;y++) for(int x=8;x<16;x++)
                {
                    int i=y*64+x;
                    Close(pixels[i].r,2*single[i].r,"Совпадающие источники не складывают красное излучение",i);
                    Close(pixels[i].g,2*single[i].g,"Совпадающие источники не складывают зелёное излучение",i);
                    Close(pixels[i].b,2*single[i].b,"Совпадающие источники не складывают синее излучение",i);
                    Close(pixels[i].a,1,"Совпадающие источники сложили поглощение вместо Max",i);
                }
                Debug.Log("Совпадающие источники: PASS — RGB двух излучателей складывается, альфа поглощения объединяется Max.");
            }
            finally
            {
                if(field!=null) UnityEngine.Object.DestroyImmediate(field.gameObject);
                if(sprite!=null) UnityEngine.Object.DestroyImmediate(sprite);
                if(mask!=null) UnityEngine.Object.DestroyImmediate(mask);
            }
        }

        private static void Compose(RoomRadianceField field,IReadOnlyList<ProjectedLightShadow> masks)
            =>field.RenderTransient(Array.Empty<RoomRadianceField.SpriteEmission>(),masks,null);
        private static void CheckOverlappingSources(RoomRadianceField field,Texture2D scene,Sprite sprite,Texture2D mask,
            IReadOnlyList<(Rect bounds,double opacity)> barrier)
        {
            // Two red emitters illuminate the same opaque wall. Independent per-source Fill maxima
            // can choose different probes, overestimate their sum and incorrectly subtract red GI.
            Paint(scene,20,28,20,28,new Color(2,0,0,1));
            Paint(scene,8,16,8,16,new Color(0,0,2,1));scene.Apply();
            field.RegisterSteadyEmitters(new[]{
                new SteadyLightEmitter("red",sprite,Matrix4x4.Translate(new Vector3(1.5f,3,0)),new Color(2,0,0,1)),
                new SteadyLightEmitter("red-other",sprite,Matrix4x4.Translate(new Vector3(3,3,0)),new Color(2,0,0,1)),
                new SteadyLightEmitter("blue",sprite,Matrix4x4.Translate(new Vector3(1.5f,1.5f,0)),new Color(0,0,2,1))});
            field.UpdateOccluders(barrier);
            for(int i=0;i<8;i++) field.Render();
            var baseline=Read(Shader.GetGlobalTexture("_RemainsRadiance"));
            var bounceA=Read(PrivateTexture(field,"bounceA"));var bounceB=Read(PrivateTexture(field,"bounceB"));
            field.PrepareSteadyShadowFields(new[]{"red","red-other","blue"});
            bool firstFound=field.TryGetSteadyShadowField("red",out var firstTexture);
            bool secondFound=field.TryGetSteadyShadowField("red-other",out var secondTexture);
            Require(firstFound && secondFound,"Не подготовлены два перекрывающихся красных источника.");
            var first=Read(firstTexture);var second=Read(secondTexture);
            Require(Sum(first,0)>.01f && Sum(second,0)>.01f,"Один из перекрывающихся источников не имеет прямого вклада.");
            var expected=new Color[baseline.Length];float remainingRed=0;int illuminatedWallTexels=0;
            for(int i=0;i<expected.Length;i++)
            {
                float directRed=first[i].r+second[i].r;
                Require(directRed<=baseline[i].r+Mathf.Max(.004f,baseline[i].r*.005f),
                    $"Изолированные Fill завысили сумму прямого красного света: тексель={i}, прямой={directRed}, полный={baseline[i].r}.");
                expected[i]=baseline[i];
                for(int channel=0;channel<3;channel++)
                    expected[i][channel]=Mathf.Max(0,baseline[i][channel]-first[i][channel]-second[i][channel]);
                remainingRed+=expected[i].r;
                int x=i%scene.width;
                if(x>=32 && x<40 && first[i].r>.001f && second[i].r>.001f) illuminatedWallTexels++;
            }
            Require(illuminatedWallTexels>0,"Два источника не перекрываются на принимающей непрозрачной стене.");
            Require(remainingRed>.01f,"После вычитания двух прямых вкладов нет красного GI для проверки.");
            var bounds=new Rect(0,0,8,6);
            Compose(field,new[]{new ProjectedLightShadow("red",mask,bounds,1),new ProjectedLightShadow("red-other",mask,bounds,1)});
            var actual=Read(Shader.GetGlobalTexture("_RemainsBackgroundRadiance"));
            for(int i=0;i<actual.Length;i++)
            {
                for(int channel=0;channel<4;channel++) Close(actual[i][channel],expected[i][channel],"Перекрытие постоянных теней изменило остаточный GI",i);
                Close(actual[i].b,baseline[i].b,"Две красные тени затронули независимый синий источник",i);
            }
            Require(Sum(actual,0)>.01f,"Перекрывающиеся тени уничтожили красный отражённый свет.");
            Equal(Read(Shader.GetGlobalTexture("_RemainsRadiance")),baseline,"Перекрывающиеся тени изменили свет актёров.");
            Equal(Read(PrivateTexture(field,"bounceA")),bounceA,"Перекрывающиеся тени изменили первую историю GI.");
            Equal(Read(PrivateTexture(field,"bounceB")),bounceB,"Перекрывающиеся тени изменили вторую историю GI.");
            Debug.Log($"Перекрывающиеся постоянные тени: PASS — два красных прямых вклада, красный GI={remainingRed}, независимый синий свет, общих текселей стены={illuminatedWallTexels}.");
        }
        private static Texture2D Mask()
        {
            var result=new Texture2D(2,2,TextureFormat.RGBA32,false,true) {filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            result.SetPixels(new[]{Color.white,Color.white,Color.white,Color.white});result.Apply();return result;
        }
        private static void Paint(Texture2D texture,int x0,int x1,int y0,int y1,Color color)
        {
            // SetColor uniforms are converted by Unity in a linear project. This fixture writes
            // the scene texture directly, so use the same emitted linear RGB as the real shader.
            var emitted=QualitySettings.activeColorSpace==ColorSpace.Linear?color.linear:color;
            for(int y=y0;y<y1;y++) for(int x=x0;x<x1;x++) texture.SetPixel(x,y,emitted);
        }
        private static Texture PrivateTexture(RoomRadianceField field,string name)
            =>(Texture)typeof(RoomRadianceField).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(field);
        private static float Sum(Color[] pixels,int channel)
        {float total=0;foreach(var pixel in pixels) total+=pixel[channel];return total;}
        private static void ExpectedSubtraction(Color[] actual,Color[] baseline,Color[] direct,float opacity)
        {
            Require(actual.Length==baseline.Length && direct.Length==baseline.Length,"Несовпадение размеров полей постоянного света.");
            for(int i=0;i<actual.Length;i++)
            {
                for(int channel=0;channel<3;channel++)
                    Close(actual[i][channel],Mathf.Max(0,baseline[i][channel]-direct[i][channel]*opacity),"Неверно вычтен вклад постоянной лампы",i);
                Close(actual[i].a,baseline[i].a,"Тень изменила альфу светового поля",i);
            }
        }
        private static void Close(float actual,float expected,string message,int texel)
            =>Require(Mathf.Abs(actual-expected)<=Mathf.Max(.002f,Mathf.Abs(expected)*.003f),$"{message}: тексель={texel}, ожидалось={expected}, получено={actual}.");
        private static void Equal(Color[] actual,Color[] expected,string message)
        {
            Require(actual.Length==expected.Length,message);
            for(int i=0;i<actual.Length;i++) Require(actual[i]==expected[i],$"{message} Тексель={i}, было={expected[i]}, стало={actual[i]}.");
        }
        private static Color[] Read(Texture source)
        {
            Require(source!=null,"Нет GPU-текстуры постоянного света.");
            var target=RenderTexture.GetTemporary(source.width,source.height,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var previous=RenderTexture.active;Texture2D copy=null;
            try
            {
                Graphics.Blit(source,target);RenderTexture.active=target;
                copy=new Texture2D(source.width,source.height,TextureFormat.RGBAFloat,false,true);
                copy.ReadPixels(new Rect(0,0,source.width,source.height),0,0);copy.Apply();return copy.GetPixels();
            }
            finally {RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);if(copy!=null) UnityEngine.Object.DestroyImmediate(copy);}
        }
        private static void Require(bool condition,string message)
        {if(!condition) throw new InvalidOperationException(message);}
    }
}
