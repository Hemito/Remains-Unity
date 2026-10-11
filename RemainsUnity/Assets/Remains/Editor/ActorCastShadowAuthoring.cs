using System;
using Remains.Presentation;
using Remains.RoomFormat;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class ActorCastShadowAuthoring
    {
        public const string PrefabPath="Assets/Remains/Resources/Presentation/ActorCastShadow.prefab";
        public const string MaterialPath="Assets/Remains/Resources/Presentation/ActorCastShadow.mat";
        public static void GenerateAndCheck()
        {
            try {Generate();Check();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Generate()
        {
            var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Remains/Resources/Presentation/ActorCastShadow.shader")
                ??throw new InvalidOperationException("Нет шейдера проекции тени актёра.");
            var material=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if(material==null) {material=new Material(shader){name="Проекция тени персонажа"};AssetDatabase.CreateAsset(material,MaterialPath);}
            material.shader=shader;EditorUtility.SetDirty(material);
            var root=new GameObject("Проекция тени персонажа");
            try {root.AddComponent<ActorCastShadowView>().Configure(material);PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);AssetDatabase.SaveAssets();}
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        public static void Check()
        {
            var actor=new Vector2(2,3);
            var matrix=ActorCastShadowView.Projection(actor,new Vector2(0,3),.2f,.3f);
            var projected=(Vector2)matrix.MultiplyPoint3x4(actor);
            Require(projected.x>actor.x && Mathf.Abs(projected.y-actor.y)<.00001f,"Тень направлена к источнику вместо удаления от него.");
            Require(Vector2.Distance(projected,actor)<=.30001f,"Смещение тени превысило ограничение.");
            Require(ActorCastShadowView.Projection(actor,actor,.2f,.3f).MultiplyPoint3x4(actor)==(Vector3)actor,"Совпадающий источник сдвинул центр.");
            Require(ActorCastShadowView.Penumbra(actor,Vector2.zero,1,.2f)>ActorCastShadowView.Penumbra(actor,Vector2.zero,.1f,.2f),"Радиус источника не изменяет полутень.");
            Require(ActorCastShadowView.ShadowOrder>WorldVisualOrderPolicy.SceneryRoomOrder && ActorCastShadowView.ShadowOrder<WorldVisualOrderPolicy.FirstOrder(WorldVisualCategory.Scenery),"Тень пересекает порядок декорации/актёров.");
            var prefab=AssetDatabase.LoadAssetAtPath<ActorCastShadowView>(PrefabPath);
            Require(prefab!=null,"Не создан авторский префаб тени.");prefab.Validate();
            CheckMask(prefab);
            CapturePonyPreview(prefab);
            Debug.Log("ActorCastShadowAuthoring: PASS — направление, ограничение смещения, полутень, порядок и авторский материал.");
        }
        private static void CapturePonyPreview(ActorCastShadowView prefab)
        {
            // Actual game pony/weapon meshes rendered by the configured pipeline, with a flat diagnostic background.
            // World illumination is disabled temporarily so the proof isolates projection and preserves the source artwork.
            ActorTypes.Load();PropCatalog.Load();Anatomy.Load();WeaponCatalog.Load();PonyVisual.Prepare();
            var artwork=WeaponArtwork.Load();
            var actor=new ActorState{id="cast-shadow-preview",actorType="pony",anatomy="pony",player=true,connected=true,
                level="hub",pose="idle",x=0,y=0,facing=1,weapon="pistol",weaponAngle=0,weaponReach=.4f,
                appearance=new CharacterAppearance{version=CharacterAppearance.CurrentVersion}};
            var visual=(PonyVisual)ActorVisualFactory.Create(actor);
            var shadow=UnityEngine.Object.Instantiate(prefab);
            var weaponRoot=new GameObject("Настоящий пистолет, диагностический кадр");
            var flashRoot=new GameObject("Исходная дульная вспышка");
            var cameraRoot=new GameObject("Резервная камера непрерывной тени");
            var target=new RenderTexture(1024,512,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            Texture2D read=null;var previous=RenderTexture.active;
            float priorWorld=Shader.GetGlobalFloat("_WorldLightEnabled"),priorRadiance=Shader.GetGlobalFloat("_RemainsRadianceEnabled");
            try
            {
                Shader.SetGlobalFloat("_WorldLightEnabled",0);Shader.SetGlobalFloat("_RemainsRadianceEnabled",0);
                visual.ConfigureMagicPreview(0);visual.Bind(actor);visual.DrawPose(0);
                visual.GetComponent<UnityEngine.Rendering.SortingGroup>().sortingOrder=WorldVisualOrderPolicy.FirstOrder(WorldVisualCategory.Player);
                Require(visual.TryGetDrawnBounds(out var actorBounds),"Нет реальных границ пони для доказательного кадра.");
                var weapon=weaponRoot.AddComponent<SpriteRenderer>();SpriteArtworkMaterial.Apply(weapon,artwork["pistol"]);
                var pose=WeaponPose.SampleRanged(actor);weaponRoot.transform.position=pose.Root;weaponRoot.transform.rotation=Quaternion.Euler(0,0,pose.Angle);
                weaponRoot.transform.localScale=new Vector3(1,pose.Mirrored?-1:1,1);weapon.sortingOrder=WorldVisualOrderPolicy.FirstOrder(WorldVisualCategory.Player)+1;
                var flashSprite=AssetDatabase.LoadAssetAtPath<Sprite>(MuzzleFlashAuthoring.Root+"pistol-flash-1.png")
                    ??throw new InvalidOperationException("Нет исходного спрайта вспышки для реального кадра тени.");
                var flash=flashRoot.AddComponent<SpriteRenderer>();SpriteArtworkMaterial.Prepare(flashSprite);SpriteArtworkMaterial.Apply(flash,flashSprite);
                SpriteArtworkMaterial.SetSelfIlluminated(flash);flash.sortingOrder=WorldVisualOrderPolicy.FirstOrder(WorldVisualCategory.Effect);
                flashRoot.transform.position=weaponRoot.transform.TransformPoint(new Vector3(.602f,.122f,0));
                flashRoot.transform.rotation=weaponRoot.transform.rotation;flashRoot.transform.localScale=weaponRoot.transform.localScale;
                var sourcePosition=(Vector2)flashRoot.transform.TransformPoint(flashSprite.bounds.center);
                var sources=new[]{new ActorShadowSource("original-pistol-flash",sourcePosition,flashSprite.bounds.size.magnitude*.25f,8f/9f)};
                var parts=new System.Collections.Generic.List<RoomRadianceField.LightOccluder>();visual.AppendLightOccluders(parts);
                parts.Add(new RoomRadianceField.LightOccluder(weapon.sprite,weaponRoot.transform.localToWorldMatrix));
                var tableAsset=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Remains/Rooms/Original/room-lighting.json")
                    ??throw new InvalidOperationException("Нет текущей таблицы света для доказательного кадра тени.");
                var settings=RoomLightingTable.Read(tableAsset.text,tableAsset.name);
                shadow.Prepare(parts,1);shadow.Draw(actorBounds.center,parts,sources,settings);
                Require(shadow.ActiveSourceCount==1,"Настоящая вспышка не создала непрерывную тень пони.");
                const int layer=31;
                foreach(var child in visual.GetComponentsInChildren<Transform>(true)) child.gameObject.layer=layer;
                foreach(var child in shadow.GetComponentsInChildren<Transform>(true)) child.gameObject.layer=layer;
                weaponRoot.layer=layer;flashRoot.layer=layer;
                Bounds bounds=actorBounds;bounds.Encapsulate(weapon.bounds);bounds.Encapsulate(flash.bounds);
                foreach(var renderer in shadow.GetComponentsInChildren<Renderer>()) if(renderer.enabled) bounds.Encapsulate(renderer.bounds);
                var camera=cameraRoot.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.aspect=2;
                camera.orthographicSize=Mathf.Max(bounds.size.y*.65f,bounds.size.x*.325f);
                camera.transform.position=new Vector3(bounds.center.x,bounds.center.y,-10);camera.cullingMask=1<<layer;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.86f,.84f,.78f,1);camera.allowHDR=false;camera.allowMSAA=false;
                target.Create();camera.targetTexture=target;CombatHudRender.RenderReserveCamera(camera,target);
                read=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);RenderTexture.active=target;
                read.ReadPixels(new Rect(0,0,target.width,target.height),0,0);read.Apply();
                string export=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../.reference/actor-continuous-shadow-pony.png"));
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(export));System.IO.File.WriteAllBytes(export,read.EncodeToPNG());
                string evidence=$"Рендер настоящего PonyVisual, пистолета и исходной дульной вспышки через резервную камеру активного render pipeline. Не AI-изображение.\nПозa: {actor.pose}, кадр: {visual.DrawnFrame}; силуэтов тела и оружия: {parts.Count}.\nПроекция: depth={settings.ActorShadowDepth}, maxOffset={settings.ActorShadowMaxOffset}, maxOpacity={settings.ActorShadowMaxOpacity}.\nОднородный фон без пола/стены; глобальный свет временно выключен только для сохранения видимости исходной графики в диагностическом кадре.\n";
                System.IO.File.WriteAllText(System.IO.Path.ChangeExtension(export,".txt"),evidence);
                Debug.Log($"ActorCastShadowAuthoring: actual pony+pistol+original flash camera preview, {parts.Count} silhouettes: {export}.");
            }
            finally
            {
                Shader.SetGlobalFloat("_WorldLightEnabled",priorWorld);Shader.SetGlobalFloat("_RemainsRadianceEnabled",priorRadiance);RenderTexture.active=previous;
                if(read!=null) UnityEngine.Object.DestroyImmediate(read);target.Release();UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(cameraRoot);UnityEngine.Object.DestroyImmediate(flashRoot);UnityEngine.Object.DestroyImmediate(weaponRoot);
                UnityEngine.Object.DestroyImmediate(shadow.gameObject);UnityEngine.Object.DestroyImmediate(visual.gameObject);
            }
        }
        private static void CheckMask(ActorCastShadowView prefab)
        {
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
            var pixels=new[]{new Color(1,1,1,.5f),new Color(1,1,1,.5f),new Color(1,1,1,.5f),new Color(1,1,1,.5f)};
            texture.SetPixels(pixels);texture.Apply();
            var mesh=new Mesh();
            mesh.vertices=new[]{new Vector3(-.5f,-.5f),new Vector3(.5f,-.5f),new Vector3(.5f,.5f),new Vector3(-.5f,.5f)};
            mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};mesh.colors=new[]{Color.white,Color.white,Color.white,Color.white};mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateBounds();
            var view=UnityEngine.Object.Instantiate(prefab);Texture2D read=null;
            var oldTarget=RenderTexture.active;
            try
            {
                var original=Matrix4x4.Scale(new Vector3(-1,1,1));
                var part=new RoomRadianceField.LightOccluder(mesh,texture,original);
                var parts=new[]{part,part};
                var sources=new[]{new ActorShadowSource("check",new Vector2(-2,0),.1f,1)};
                var settings=new RoomLightingTable{ActorShadowDepth=.1,ActorShadowMaxOffset=.5,ActorShadowMaxOpacity=.4,ActorShadowMaxSources=3};
                Require(view.NeedsPreparation(parts,sources.Length),"Новый вид не запросил явную подготовку.");
                view.Prepare(parts,sources.Length);
                Require(!view.NeedsPreparation(parts,sources.Length),"Явная подготовка не создала рабочий кеш.");
                view.Draw(Vector2.zero,parts,sources,settings);
                Require(view.ActiveSourceCount==1,"Проекция не создала маску источника.");
                int rebuilds=view.MaskRebuilds;
                view.Draw(Vector2.zero,parts,sources,settings);
                Require(view.MaskRebuilds==rebuilds,"Неизменный силуэт повторно перерисовал маску.");
                var slots=(System.Collections.IList)typeof(ActorCastShadowView).GetField("slots",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(view);
                var slot=slots[0];
                var mask=(RenderTexture)slot.GetType().GetField("mask").GetValue(slot);
                read=new Texture2D(mask.width,mask.height,TextureFormat.RGBA32,false,true);
                RenderTexture.active=mask;read.ReadPixels(new Rect(0,0,mask.width,mask.height),0,0);read.Apply();
                float unionAlpha=read.GetPixel(mask.width/2,mask.height/2).a;
                Require(Mathf.Abs(unionAlpha-.5f)<.02f,$"Две совпадающие части должны объединяться без двойного затемнения: alpha {unionAlpha}.");
                Require(part.LocalToWorld==original && mesh.bounds.center==Vector3.zero,"Проекция изменила исходную матрицу/mesh актёра.");
                CheckComposite(view);
                view.Prepare(parts,4);
                view.Draw(Vector2.zero,parts,new[]{sources[0],sources[0],sources[0],new ActorShadowSource("flash",new Vector2(2,0),.05f,1)},settings);
                Require(view.ActiveSourceCount==4,"Вспышка после трёх ламп потерялась из-за лимита постоянных источников.");
                CheckConnectedSweep(view,texture);
                view.Clear();Require(view.ActiveSourceCount==0,"Clear оставил активную тень.");
                Debug.Log($"ActorCastShadowAuthoring: GPU union alpha={unionAlpha:F4}; mirror, исходные mesh/матрица и Clear PASS.");
            }
            finally {RenderTexture.active=oldTarget;if(read!=null) UnityEngine.Object.DestroyImmediate(read);UnityEngine.Object.DestroyImmediate(view.gameObject);UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(texture);}
        }
        private static void CheckConnectedSweep(ActorCastShadowView view,Texture texture)
        {
            var narrow=new Mesh{name="Узкий силуэт для проверки непрерывной тени"};
            narrow.vertices=new[]{new Vector3(-.02f,-.5f),new Vector3(.02f,-.5f),new Vector3(.02f,.5f),new Vector3(-.02f,.5f)};
            narrow.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};narrow.colors=new[]{Color.white,Color.white,Color.white,Color.white};narrow.triangles=new[]{0,1,2,0,2,3};narrow.RecalculateBounds();
            var parts=new[]{new RoomRadianceField.LightOccluder(narrow,texture,Matrix4x4.identity)};
            var source=new[]{new ActorShadowSource("left",new Vector2(-3,0),.03f,1)};
            var settings=new RoomLightingTable{ActorShadowDepth=.25,ActorShadowMaxOffset=.6,ActorShadowMaxOpacity=.4,ActorShadowMaxSources=3};
            Texture2D read=null;var previous=RenderTexture.active;
            try
            {
                view.Prepare(parts,1);view.Draw(Vector2.zero,parts,source,settings);
                var slots=(System.Collections.IList)typeof(ActorCastShadowView).GetField("slots",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(view);
                var slot=slots[0];var type=slot.GetType();
                var target=(RenderTexture)type.GetField("mask").GetValue(slot);
                var bounds=(Bounds)type.GetField("lastBounds").GetValue(slot);
                read=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false,true);
                RenderTexture.active=target;read.ReadPixels(new Rect(0,0,target.width,target.height),0,0);read.Apply();
                float minimum=1;
                for(int step=0;step<=120;step++)
                {
                    float x=.6f*step/120;
                    float alpha=WorldAlpha(read,bounds,new Vector2(x,0));minimum=Mathf.Min(minimum,alpha);
                    Require(alpha>.45f,$"Непрерывная тень разорвана между исходным телом и концом проекции: x={x:F4}, alpha={alpha:F4}.");
                }
                Require(WorldAlpha(read,bounds,new Vector2(-.1f,0))<.01f,"Непрерывная тень растёт в сторону источника света.");
                int poseRebuilds=view.PoseMaskRebuilds;
                view.Draw(Vector2.zero,parts,new[]{new ActorShadowSource("right",new Vector2(3,0),.03f,1)},settings);
                Require(view.PoseMaskRebuilds==poseRebuilds,"Смена источника повторно отрисовала исходную маску тела.");
                string export=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../.reference/actor-continuous-shadow-mask.png"));
                // Flatten alpha onto white for a legible diagnostic PNG; runtime mask bytes remain unchanged.
                var preview=read.GetPixels();for(int i=0;i<preview.Length;i++) preview[i]=Color.Lerp(Color.white,Color.black,preview[i].a);
                read.SetPixels(preview);read.Apply();
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(export));System.IO.File.WriteAllBytes(export,read.EncodeToPNG());
                Debug.Log($"ActorCastShadowAuthoring: continuous sweep 121 samples, min alpha={minimum:F4}, away-light direction and one pose mask PASS; {export}.");
            }
            finally {RenderTexture.active=previous;if(read!=null) UnityEngine.Object.DestroyImmediate(read);UnityEngine.Object.DestroyImmediate(narrow);}
        }
        private static float WorldAlpha(Texture2D texture,Bounds bounds,Vector2 world)
        {
            var uv=new Vector2((world.x-bounds.min.x)/bounds.size.x,(world.y-bounds.min.y)/bounds.size.y);
            if(uv.x<0 || uv.x>1 || uv.y<0 || uv.y>1) return 0;
            return texture.GetPixelBilinear(uv.x,uv.y).a;
        }
        private static void CheckComposite(ActorCastShadowView view)
        {
            const int layer=31;
            var cameraRoot=new GameObject("Проверка композиции тени");
            var actor=new GameObject("Проверочный непрозрачный актёр");
            var target=new RenderTexture(256,256,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            var white=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
            white.SetPixels(new[]{Color.white,Color.white,Color.white,Color.white});white.Apply();
            var sprite=Sprite.Create(white,new Rect(0,0,2,2),Vector2.one*.5f,4);
            var actorMaterial=new Material(Shader.Find("Sprites/Default")??throw new InvalidOperationException("Нет шейдера проверки непрозрачного актёра."));
            Texture2D read=null;
            var previous=RenderTexture.active;
            try
            {
                foreach(var renderer in view.GetComponentsInChildren<Renderer>()) renderer.gameObject.layer=layer;
                actor.layer=layer;var body=actor.AddComponent<SpriteRenderer>();body.sprite=sprite;body.color=Color.red;
                body.sharedMaterial=actorMaterial;body.sortingOrder=WorldVisualOrderPolicy.FirstOrder(WorldVisualCategory.Scenery);
                var camera=cameraRoot.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=1;
                camera.transform.position=new Vector3(0,0,-10);camera.cullingMask=1<<layer;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.white;camera.allowHDR=false;camera.allowMSAA=false;
                target.Create();camera.targetTexture=target;
                CombatHudRender.RenderReserveCamera(camera,target);
                read=new Texture2D(256,256,TextureFormat.RGBA32,false,true);
                RenderTexture.active=target;read.ReadPixels(new Rect(0,0,256,256),0,0);read.Apply();
                Color self=read.GetPixel(128,128),shadow=read.GetPixel(205,128),clear=read.GetPixel(20,128);
                Require(self.r>.95f && self.g<.03f && self.b<.03f,$"Проекция затемнила исходную картинку актёра: {self}.");
                Require(shadow.r<clear.r-.08f && clear.r>.95f,$"Проекция не видна на фоне или затемняет фон без силуэта: shadow={shadow}, clear={clear}.");
                Debug.Log($"ActorCastShadowAuthoring: camera composite actor={self}, shadow={shadow}, clear={clear} PASS.");
            }
            finally
            {
                RenderTexture.active=previous;if(read!=null) UnityEngine.Object.DestroyImmediate(read);
                target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(cameraRoot);UnityEngine.Object.DestroyImmediate(actor);
                UnityEngine.Object.DestroyImmediate(actorMaterial);UnityEngine.Object.DestroyImmediate(sprite);UnityEngine.Object.DestroyImmediate(white);
            }
        }
        private static void Require(bool condition,string message) {if(!condition) throw new InvalidOperationException(message);}
    }
}
