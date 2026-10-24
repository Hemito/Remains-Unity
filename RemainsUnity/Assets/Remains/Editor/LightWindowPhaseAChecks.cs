using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Remains.Presentation;
using Remains.Content;
using Remains.RoomFormat;
using UnityEditor;
using UnityEngine;
namespace Remains.Editor
{
    // Explicit GPU readbacks are confined to this check, never a runtime preparation stage.
    public static class LightWindowPhaseAChecks
    {
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);} catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Run()
        {
            RoomRadianceField.ClearSpareTextures();
            long initial=LightTextureBudget.Bytes;
            RoomRadianceField reference=null,candidate=null,interference=null;
            IEnumerator steps=null;
            try
            {
                // A legacy room/hub caller omits a token; Build must still give it an explicit owner.
                reference=Make(8,6,null);
                for(int i=0;i<8;i++) reference.RenderReferenceForChecks();
                var globals=Shader.GetGlobalTexture("_RemainsRadiance");
                var expected=Buffers(reference);
                var candidateOwner=new LightShaderContext();
                candidate=Make(8,6,candidateOwner);
                interference=Make(5,3,new LightShaderContext());
                Require(ReferenceEquals(Shader.GetGlobalTexture("_RemainsRadiance"),globals),"Build кандидата изменил опубликованный свет.");
                steps=candidate.PrepareWindowSteps(8);int stages=0;
                while(steps.MoveNext())
                {
                    stages++;
                    // A different grid deliberately overwrites shared compute uniforms/textures between dispatches.
                    interference.RenderReferenceForChecks();
                    Require(ReferenceEquals(Shader.GetGlobalTexture("_RemainsRadiance"),globals),"Этап кандидата опубликовал свет раньше готовности.");
                }
                (steps as IDisposable)?.Dispose();steps=null;
                Require(stages>8 && candidate.StaticRenderUpdates==8,"Подготовка не разделена на этапы или изменила число отражений.");
                EqualBuffers(expected,Buffers(candidate));
                CheckDirectSteps(reference,candidate,interference,globals);
                candidateOwner.Activate();candidate.PublishRadiance();
                var published=Shader.GetGlobalTexture("_RemainsRadiance");
                DestroyField(reference);reference=null;
                Require(ReferenceEquals(Shader.GetGlobalTexture("_RemainsRadiance"),published) && Shader.GetGlobalFloat("_RemainsRadianceEnabled")==1,
                    "Удаление прежнего окна отключило новое.");
                // Cancel a partially dispatched candidate; disposal must release iterators and preserve active globals.
                var cancelled=Make(7,4,new LightShaderContext());
                try
                {
                    var work=cancelled.PrepareWindowSteps(8);
                    try {for(int i=0;i<5;i++) Require(work.MoveNext(),"Мало этапов для проверки отмены.");}
                    finally {(work as IDisposable)?.Dispose();}
                }
                finally {DestroyField(cancelled);}
                Require(ReferenceEquals(Shader.GetGlobalTexture("_RemainsRadiance"),published),"Отмена кандидата повредила активный свет.");
                // Reverse transition: a new legacy field must survive destruction of an old bound factory field.
                reference=Make(6,4,null);reference.RenderReferenceForChecks();
                var legacyGlobal=Shader.GetGlobalTexture("_RemainsRadiance");
                DestroyField(candidate);candidate=null;
                Require(ReferenceEquals(Shader.GetGlobalTexture("_RemainsRadiance"),legacyGlobal) && Shader.GetGlobalFloat("_RemainsRadianceEnabled")==1,
                    "Прежнее factory-окно отключило новое legacy-поле.");
                CheckRevisionContext();CheckBudgetLimit();CheckStableCatalog();CheckPrefetchGate();CheckSharedFieldAndErase();CheckPreparationFailures();CheckResourceStageCancellation();
                Debug.Log($"Свет Phase A: PASS — 8 отражений, {stages} этапов, побайтовые буферы исходного renderer, вмешательство общего compute, owner/отмена/revision/бюджет/стабильные ID.");
            }
            finally
            {
                (steps as IDisposable)?.Dispose();
                foreach(var field in new[]{reference,candidate,interference}) if(field!=null) DestroyField(field);
                RoomRadianceField.ClearSpareTextures();
            }
            Require(LightTextureBudget.Bytes==initial,"Отмена/удаление оставили учтённые GPU-ресурсы.");
        }
        // Full seed104 composition test is separate from the small buffer-equivalence batch.
        public static void RunFactoryBatch()
        {
            GameObject visual=null;IDisposable context=null;IEnumerator steps=null;
            try
            {
                Remains.Domain.Anatomy.Load();Remains.Content.WeaponCatalog.Load();
                var catalog=AssetDatabase.LoadAssetAtPath<Remains.Content.FactoryCatalogAsset>(FactoryCatalogAuthoring.AssetPath);
                var world=new Remains.Domain.WorldState {seed=104};
                Remains.Content.FactoryPreparation.Create(world,catalog,0);
                Remains.Content.FactoryContentSources.Prepare(world,"factory:0");
                var source=RoomAuthoring.Load("factory:0",world);
                Remains.Content.DoorContentInstaller.Apply(world,Remains.Content.RoomDoorContent.Read(source,"factory:0"));
                var room=source.CreateLayout(false);visual=AuthoredRoomVisual.Create("factory:0",room,world);
                var view=visual.GetComponent<FactoryLevelView>();view.Synchronize(room,world,1);view.Focus(new Vector2(room.spawnX,room.spawnY));
                var oldRooms=view.LightWindowRooms;int oldField=view.LightFieldInstanceId;
                var oldGlobal=Shader.GetGlobalTexture("_RemainsRadiance");
                var adapter=Private(view,"root");
                var instances=(Transform)adapter.GetType().GetProperty("RoomInstances").GetValue(adapter);
                var visibility=new bool[instances.childCount];for(int i=0;i<visibility.Length;i++) visibility[i]=instances.GetChild(i).gameObject.activeSelf;
                Vector2 target=default;bool found=false;
                for(float y=room.bottom+1;y<room.top && !found;y+=8)
                    for(float x=room.left+1;x<room.right && !found;x+=8)
                        if(view.WindowFor(new Vector2(x,y))!=oldRooms) {target=new Vector2(x,y);found=true;}
                Require(found,"Завод не имеет соседнего окна для проверки.");
                var type=typeof(FactoryLevelView).Assembly.GetType("Remains.Presentation.LightWindowContext",true);
                Func<IDisposable> make=()=> (IDisposable)Activator.CreateInstance(type,new object[]{view.WindowFor(target),view.AppliedDoorRevision,view.AppliedLampRevision});
                var prepare=typeof(FactoryLevelView).GetMethod("BuildWindowSteps",BindingFlags.Instance|BindingFlags.NonPublic);
                var commit=typeof(FactoryLevelView).GetMethod("CommitWindow",BindingFlags.Instance|BindingFlags.NonPublic);
                context=make();steps=(IEnumerator)prepare.Invoke(view,new object[]{context});
                int count=0;
                while(steps.MoveNext())
                {
                    count++;
                    Require(view.LightFieldInstanceId==oldField && view.LightWindowRooms==oldRooms && ReferenceEquals(oldGlobal,Shader.GetGlobalTexture("_RemainsRadiance")),
                        "Подготовка кандидата преждевременно сменила поле/окно/globals.");
                    for(int i=0;i<visibility.Length;i++) Require(instances.GetChild(i).gameObject.activeSelf==visibility[i],"Подготовка кандидата переключила комнаты.");
                }
                (steps as IDisposable)?.Dispose();steps=null;
                // Source-adapter visibility changes have no lamp revision; the captured picture signature still rejects them.
                var placements=(System.Collections.IList)type.GetField("Pictures").GetValue(context);
                if(placements.Count>0)
                {
                    var first=((RoomSpriteAuthoring sprite,RoomSpritePlacement placement))placements[0];
                    var renderer=first.sprite.GetComponent<SpriteRenderer>();bool enabled=renderer.enabled;
                    renderer.enabled=!enabled;bool stalePictureRejected=false;
                    try {commit.Invoke(view,new object[]{context});}
                    catch(TargetInvocationException error) when(error.InnerException is InvalidOperationException) {stalePictureRejected=true;}
                    finally {renderer.enabled=enabled;}
                    Require(stalePictureRejected && view.LightFieldInstanceId==oldField,"Изменение видимости картинки не инвалидировало кандидата.");
                }
                context.Dispose();context=null;
                RoomRadianceField.ClearSpareTextures();long beforeCancel=LightTextureBudget.Bytes;
                context=make();steps=(IEnumerator)prepare.Invoke(view,new object[]{context});
                for(int i=0;i<5;i++) Require(steps.MoveNext(),"Мало этапов factory candidate для отмены.");
                typeof(FactoryLevelView).GetField("candidateWindow",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(view,context);
                typeof(FactoryLevelView).GetField("preparationWork",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(view,steps);
                view.CancelWindowPreparation();context=null;steps=null;
                RoomRadianceField.ClearSpareTextures();
                Require(LightTextureBudget.Bytes==beforeCancel && view.LightFieldInstanceId==oldField && view.LightWindowRooms==oldRooms,
                    "CancelWindowPreparation потеряла iterator/текстуры или переключила active context.");
                context=make();steps=(IEnumerator)prepare.Invoke(view,new object[]{context});while(steps.MoveNext()) {}
                (steps as IDisposable)?.Dispose();steps=null;
                // A lamp revision during preparation no longer discards the candidate: the commit catches up with it.
                var dark=world.lamps.Where(lamp=>lamp.level==view.LampWorldLevel && lamp.broken).Select(lamp=>lamp.id).ToArray();
                view.SyncLamps(dark,view.AppliedLampRevision+1,dark);
                var committed=context;commit.Invoke(view,new object[]{context});context=null; // view owns the committed context
                Require(view.LightWindowRooms==view.WindowFor(target) && view.LightFieldInstanceId!=oldField,"Готовый кандидат не опубликован атомарно.");
                Require((bool)type.GetMethod("Matches").Invoke(committed,new object[]{view.AppliedDoorRevision,view.AppliedLampRevision}),
                    "Опубликованное окно не догнало ревизию ламп, изменённую во время подготовки.");
                Require(LightTextureBudget.Bytes<=LightTextureBudget.MaximumBytes,"Полный завод превысил суммарный GPU-бюджет.");
                Debug.Log($"Свет Phase A seed104: PASS — {count} этапов без раннего переключения, изменение картинки отклонено, ревизия ламп во время подготовки догнана при публикации, atomic commit; RenderTexture={LightTextureBudget.Bytes} байт.");
                EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
            finally
            {
                (steps as IDisposable)?.Dispose();context?.Dispose();
                if(visual!=null) UnityEngine.Object.DestroyImmediate(visual);
                Remains.Content.FactoryContentSources.Clear();RoomRadianceField.ClearSpareTextures();
            }
        }
        private static void DestroyField(RoomRadianceField field)
        {
            if(field==null) return;
            // EditMode checks may destroy components before Unity invokes their lifecycle callbacks.
            field.ReleaseResources();
            UnityEngine.Object.DestroyImmediate(field.gameObject);
        }
        private static RoomRadianceField Make(int width,int height,LightShaderContext owner)
        {
            var room=new RoomFile {Template="remains:check:window-stages",Width=width,Height=height,Cell=1};
            room.Options.Add("dark","0");room.Options.Add("color","");room.Options.Add("lon","0");
            var table=new RoomLightingTable {RadianceResolution=4,RadiancePower=1,RadianceLampSize=.2,
                RadianceAlbedo=.35,ShadowEdge=.1,ObjectRimWidth=.1,ObjectLightMode="gradient"};
            var field=RoomRadianceField.Build(room,new RoomDefinitionCatalog(),new RoomLandCatalog(),table,Vector2.zero,null,lightContext:owner);
            var scene=(Texture2D)Private(field,"baseScene");
            for(int y=0;y<scene.height;y++) for(int x=0;x<scene.width;x++)
                scene.SetPixel(x,y,x==scene.width/2?new Color(0,0,0,1):x>2 && x<6 && y>2 && y<6?new Color(2,.5f,0,1):Color.clear);
            scene.Apply(false,false);field.UpdateOccluders(Array.Empty<(Rect,double)>());
            return field;
        }
        private static Dictionary<string,byte[]> Buffers(RoomRadianceField field)
        {
            var result=new Dictionary<string,byte[]>();
            foreach(string name in new[]{"distance","fluence","fluenceRaw","staticFluence","bounceA","bounceB","fillSamples","fillWeights"})
                result.Add(name,Read((Texture)Private(field,name)));
            return result;
        }
        private static byte[] Read(Texture source)
        {
            var target=(RenderTexture)source;var previous=RenderTexture.active;
            var pixels=new Texture2D(target.width,target.height,TextureFormat.RGBAFloat,false,true);
            try {RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();return pixels.GetRawTextureData<byte>().ToArray();}
            finally {RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(pixels);}
        }
        private static void EqualBuffers(Dictionary<string,byte[]> expected,Dictionary<string,byte[]> actual)
        {
            foreach(var pair in expected) Require(pair.Value.SequenceEqual(actual[pair.Key]),"Поэтапный renderer изменил точные байты буфера "+pair.Key);
        }
        private static object Private(object value,string name)=>value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(value);
        private static void CheckDirectSteps(RoomRadianceField reference,RoomRadianceField candidate,RoomRadianceField interference,Texture published)
        {
            var pixels=new Texture2D(2,2);pixels.SetPixels(new[]{Color.white,Color.white,Color.white,Color.white});pixels.Apply();
            var sprite=Sprite.Create(pixels,new Rect(0,0,2,2),new Vector2(.5f,.5f),2);
            var emitter=new SteadyLightEmitter("check:direct",sprite,Matrix4x4.Translate(new Vector3(1,1,0)),new Color(2,.5f,0,1));
            try
            {
                reference.RegisterSteadyEmitters(new[]{emitter});candidate.RegisterSteadyEmitters(new[]{emitter});
                reference.PrepareSteadyShadowFields(new[]{emitter.Id});
                var work=candidate.PrepareRegisteredShadowFieldsSteps(new[]{emitter.Id});int count=0;
                try
                {
                    while(work.MoveNext())
                    {
                        count++;interference.RenderReferenceForChecks();
                        Require(ReferenceEquals(Shader.GetGlobalTexture("_RemainsRadiance"),published),"Прямое поле кандидата изменило active globals.");
                    }
                }
                finally {(work as IDisposable)?.Dispose();}
                Require(count>2,"Построение прямого поля не разделено по dispatch.");
                bool a=reference.TryGetSteadyShadowField(emitter.Id,out var expected),b=candidate.TryGetSteadyShadowField(emitter.Id,out var actual);
                Require(a && b && Read(expected).SequenceEqual(Read(actual)),"Поэтапное прямое поле отличается от полного эталона.");
            }
            finally {UnityEngine.Object.DestroyImmediate(sprite);UnityEngine.Object.DestroyImmediate(pixels);}
        }
        private static void CheckResourceStageCancellation()
        {
            RoomRadianceField.ClearSpareTextures();long before=LightTextureBudget.Bytes;
            var room=new RoomFile {Template="remains:check:resource-stages",Width=4,Height=3,Cell=1};
            room.Options.Add("dark","0");room.Options.Add("color","");room.Options.Add("lon","0");
            var table=new RoomLightingTable {RadianceResolution=4,RadiancePower=1,RadianceLampSize=.2,
                RadianceAlbedo=.35,ShadowEdge=.1,ObjectRimWidth=.1,ObjectLightMode="gradient"};
            var data=RoomRadianceField.PreparedScene.Create(room,new RoomDefinitionCatalog(),new RoomLandCatalog(),table);
            // Every early exit in initialization must unwind already assigned targets and the pending field owner.
            for(int cancelAfter=1;cancelAfter<=22;cancelAfter++)
            {
                RoomRadianceField produced=null;var work=RoomRadianceField.BuildSteps(data,new RoomLandCatalog(),Vector2.zero,null,
                    result=>produced=result,lightContext:new LightShaderContext());
                try {for(int i=0;i<cancelAfter && work.MoveNext();i++) {}}
                finally {(work as IDisposable)?.Dispose();if(produced!=null) DestroyField(produced);}
                RoomRadianceField.ClearSpareTextures();
                Require(LightTextureBudget.Bytes==before,"Отмена на этапе выделения ресурсов оставила текстуры: этап="+cancelAfter+
                    $", до={before}, после={LightTextureBudget.Bytes}, ресурсы={TrackedTextures()}");
            }
            var field=Make(4,3,new LightShaderContext());
            try
            {
                var def=new RoomDefinition {Id="check:empty-draw",Kind=RoomDefinitionKind.Decor};
                var spriteTexture=new Texture2D(2,2);var sprite=Sprite.Create(spriteTexture,new Rect(0,0,2,2),Vector2.zero);
                try
                {
                    var placement=new RoomSpritePlacement {Definition=def,Sprite=sprite,LocalToWorld=Matrix4x4.identity};
                    field.UpdateSprites(new[]{placement},Array.Empty<(Rect,double)>());
                    RoomRadianceField.ClearSpareTextures();long stable=LightTextureBudget.Bytes;
                    for(int i=0;i<6;i++) field.UpdateSprites(new[]{placement},Array.Empty<(Rect,double)>());
                    RoomRadianceField.ClearSpareTextures();
                    Require(LightTextureBudget.Bytes==stable,"UpdateSprites накапливает учтённые старые сцены света.");
                }
                finally {UnityEngine.Object.DestroyImmediate(sprite);UnityEngine.Object.DestroyImmediate(spriteTexture);}
            }
            finally {DestroyField(field);RoomRadianceField.ClearSpareTextures();}
            Require(LightTextureBudget.Bytes==before,"Проверка UpdateSprites оставила ресурсы.");
        }
        private static string TrackedTextures()
        {
            var owned=(Dictionary<RenderTexture,long>)typeof(LightTextureBudget).GetField("owned",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
            return string.Join("; ",owned.Select(pair=>$"{(pair.Key==null?"destroyed":pair.Key.name)}:{pair.Value}"));
        }
        private static void CheckPreparationFailures()
        {
            RoomRadianceField.ClearSpareTextures();long before=LightTextureBudget.Bytes;
            var room=new RoomFile {Template="remains:check:failed-draw",Width=4,Height=3,Cell=1};
            room.Options.Add("dark","0");room.Options.Add("color","");room.Options.Add("lon","0");
            var table=new RoomLightingTable {RadianceResolution=4,RadiancePower=1,RadianceLampSize=.2,
                RadianceAlbedo=.35,ShadowEdge=.1,ObjectRimWidth=.1,ObjectLightMode="gradient"};
            bool failed=false;RoomRadianceField unexpected=null;
            try
            {
                unexpected=RoomRadianceField.Build(room,new RoomDefinitionCatalog(),new RoomLandCatalog(),table,Vector2.zero,null,
                    new[]{new RoomSpritePlacement {Definition=new RoomDefinition {Id="check:missing-sprite"},Sprite=null}},lightContext:new LightShaderContext());
            }
            catch(NullReferenceException) {failed=true;}
            finally {if(unexpected!=null) DestroyField(unexpected);}
            RoomRadianceField.ClearSpareTextures();
            Require(failed && LightTextureBudget.Bytes==before,"Отказ DrawSprites оставил локальную текстуру вне владельца.");
            var pixels=new Texture2D(2,2);var sprite=Sprite.Create(pixels,new Rect(0,0,2,2),Vector2.zero);
            try
            {
                foreach(bool cancel in new[]{true,false})
                {
                    var cache=new SteadyLightShadowCache();cache.Register(new[]{new SteadyLightEmitter("fail",sprite,Matrix4x4.identity,Color.white)});
                    IEnumerator Builder(SteadyLightEmitter emitter,Action<RenderTexture> done)
                    {
                        var texture=new RenderTexture(2,2,0);LightTextureBudget.Track(texture,16);done(texture);
                        yield return null;
                        throw new InvalidOperationException("Ожидаемая ошибка проверки передачи ресурса.");
                    }
                    var work=cache.PrepareSteps(new[]{"fail"},Builder);
                    try
                    {
                        Require(work.MoveNext(),"Нет этапа для проверки отмены передачи текстуры.");
                        if(!cancel)
                        {
                            bool rejected=false;
                            try {work.MoveNext();} catch(InvalidOperationException) {rejected=true;}
                            Require(rejected,"Проверка ошибки builder не выполнилась.");
                        }
                    }
                    finally {(work as IDisposable)?.Dispose();cache.Dispose();}
                    Require(LightTextureBudget.Bytes==before,"Отмена/ошибка до fields.Add потеряла текстуру прямого света.");
                }
            }
            finally {UnityEngine.Object.DestroyImmediate(sprite);UnityEngine.Object.DestroyImmediate(pixels);}
        }
        private static void CheckSharedFieldAndErase()
        {
            var owner=new LightShaderContext();owner.Activate();
            var field=Make(4,3,owner);var root=new GameObject("Проверка общего контекста комнаты");
            var pixels=new Texture2D(2,2);pixels.SetPixels(new[]{Color.white,Color.white,Color.white,Color.white});pixels.Apply();
            var sprite=Sprite.Create(pixels,new Rect(0,0,2,2),Vector2.zero,2);
            RoomDecorResources erase=null;
            try
            {
                erase=root.AddComponent<RoomDecorResources>();erase.BindLightContext(owner);
                erase.BuildErase(new List<(Sprite,Matrix4x4)>{(sprite,Matrix4x4.identity)},Vector2.zero,new Vector2(4,3),
                    GameAssets.Load<Shader>(RoomDecorView.EraseShaderResource)??throw new InvalidOperationException("Нет shader erase для проверки"));
                var mask=Shader.GetGlobalTexture("_RemainsErase");
                field.RenderReferenceForChecks();
                Require(Shader.GetGlobalFloat("_RemainsEraseEnabled")==1 && ReferenceEquals(mask,Shader.GetGlobalTexture("_RemainsErase")) &&
                    ReferenceEquals(Shader.GetGlobalTexture("_RemainsRadiance"),Private(field,"fluence")),"Общий контекст комнаты не публикует field и erase одновременно.");
                erase.PublishErase();field.RenderReferenceForChecks();
                Require(Shader.GetGlobalFloat("_RemainsRadianceEnabled")==1 && Shader.GetGlobalFloat("_RemainsEraseEnabled")==1,
                    "Повторная публикация erase отняла владение radiance комнаты.");
            }
            finally
            {
                erase?.ReleaseResources();UnityEngine.Object.DestroyImmediate(root);
                DestroyField(field);UnityEngine.Object.DestroyImmediate(sprite);UnityEngine.Object.DestroyImmediate(pixels);
            }
        }
        private static void CheckPrefetchGate()
        {
            var gate=new LightWindowPrefetchGate();var active=new RectInt(0,0,3,3);var next=new RectInt(0,1,3,3);var other=new RectInt(1,0,3,3);
            Require(!gate.ShouldRequest(active,next,0,.2f) && gate.ShouldRequest(active,next,.3f,.2f),"Стабильный прогноз не готовится заранее.");
            Require(!gate.ShouldRequest(active,active,.4f,.2f),"Остановка запросила активное окно заново.");
            Require(!gate.ShouldRequest(active,next,.5f,.2f) && gate.ShouldRequest(active,next,.8f,.2f),"Продолжение движения нарушило гистерезис.");
            Require(!gate.ShouldRequest(active,other,.9f,.2f) && !gate.ShouldRequest(active,next,1f,.2f),"Шумный прогноз немедленно заменяет кандидата.");
        }
        private static void CheckRevisionContext()
        {
            var type=typeof(FactoryLevelView).Assembly.GetType("Remains.Presentation.LightWindowContext",true);
            var context=Activator.CreateInstance(type,new object[]{new RectInt(1,2,3,3),7L,9L});
            try
            {
                var method=type.GetMethod("Matches");
                Require((bool)method.Invoke(context,new object[]{7L,9L}) && !(bool)method.Invoke(context,new object[]{8L,9L}) &&
                    !(bool)method.Invoke(context,new object[]{7L,10L}),"Revision guard принял устаревший свет.");
            }
            finally {((IDisposable)context).Dispose();}
        }
        private static void CheckBudgetLimit()
        {
            long before=LightTextureBudget.Bytes;int count=LightTextureBudget.Count;
            var texture=new RenderTexture(1,1,0) {name="Проверка отказа бюджета"};
            bool rejected=false;
            try {try {LightTextureBudget.Track(texture,LightTextureBudget.MaximumBytes+1);} catch(InvalidOperationException) {rejected=true;}}
            finally {UnityEngine.Object.DestroyImmediate(texture);}
            Require(rejected && before==LightTextureBudget.Bytes && count==LightTextureBudget.Count,"Отказ бюджета изменил учёт ресурсов.");
            Require(RoomRadianceField.SpareTextureBytes<=LightTextureBudget.MaximumSpareBytes,"Spare превысил общий лимит.");
        }
        private static void CheckStableCatalog()
        {
            var pixels=new Texture2D(2,2);var sprite=Sprite.Create(pixels,new Rect(0,0,2,2),Vector2.zero);
            var cache=new SteadyLightShadowCache();int builds=0;
            var emitter=new SteadyLightEmitter("stable",sprite,Matrix4x4.identity,Color.white);
            try
            {
                cache.Register(new[]{emitter});
                cache.Prepare(new[]{"stable"},_=> {builds++;return new RenderTexture(2,2,0);});
                cache.Register(new[]{emitter});cache.Prepare(new[]{"stable"},_=> {builds++;return new RenderTexture(2,2,0);});
                Require(builds==1 && cache.Count==1,"Повтор регистрации неизменного ID перестроил прямое поле.");
            }
            finally {cache.Dispose();UnityEngine.Object.DestroyImmediate(sprite);UnityEngine.Object.DestroyImmediate(pixels);}
        }
        private static void Require(bool condition,string message) {if(!condition) throw new InvalidOperationException(message);}
    }
}
