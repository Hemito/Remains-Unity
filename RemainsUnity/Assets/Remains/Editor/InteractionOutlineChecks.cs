using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Remains.Editor
{
    public static class InteractionOutlineChecks
    {
        public static void Run()
        {
            var camera=new GameObject("Камера проверки мирового контура").AddComponent<Camera>();
            camera.tag="MainCamera";
            camera.orthographic=true;camera.orthographicSize=4;camera.transform.position=new Vector3(0,0,-10);
            var outer=new GameObject("Фактическая внешняя группа").AddComponent<SortingGroup>();outer.sortingOrder=10;
            var inner=new GameObject("Внутренняя группа рисунка").AddComponent<SortingGroup>();inner.transform.SetParent(outer.transform,false);inner.sortingOrder=100;
            var artwork=new GameObject("Фактическое изображение").AddComponent<SpriteRenderer>();artwork.transform.SetParent(inner.transform,false);
            var square=Sprite.Create(Texture2D.whiteTexture,new Rect(0,0,Texture2D.whiteTexture.width,Texture2D.whiteTexture.height),Vector2.one*.5f,Texture2D.whiteTexture.width);
            artwork.sprite=square;
            var rectangle=new Rect(-2,-1.5f,4,3);var selected=new InteractionTarget(InteractionKind.Map,"outline-map-check",rectangle);
            var source=WorldOutlineSource.ForRenderer(artwork);
            var outline=new WorldInteractionOutline(camera,_=>source);
            var canvas=new GameObject("Overlay только для E",typeof(RectTransform),typeof(Canvas));canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            InteractionHint hint=null;
            try
            {
                Require(source.Group==outer,"Выбран вложенный слой части вместо внешней группы объекта");
                var prepared=outline.Renderers.ToArray();var preparedSprite=prepared[0].sprite;var preparedMaterial=prepared[0].sharedMaterial;
                foreach(int height in new[]{720,1080,2160}) foreach(float zoom in new[]{2f,4f,8f})
                {
                    camera.pixelRect=new Rect(0,0,height*16f/9,height);camera.orthographicSize=zoom;
                    outline.SetTarget(selected,true);
                    Require(outline.Visible && outline.Renderers.All(bar=>RenderSorting.FindOwner(bar)==outer),"Target refresh не привязал/включил bars до camera/group pass");
                    int bound=outline.BindingRevisionCount;outline.Prepare(camera);Require(outline.BindingRevisionCount==bound,"Camera pass меняет регистрацию/порядок/включение Renderer");
                    CheckBars(outline,rectangle,0,camera,outer);
                    for(int i=0;i<4;i++) Require(outline.Renderers[i]==prepared[i] && prepared[i].sprite==preparedSprite && prepared[i].sharedMaterial==preparedMaterial,
                        "Камера/zoom пересоздают SpriteRenderer, Sprite или материал");
                    Require(outer.sortingOrder==10 && inner.sortingOrder==100,"Контур изменил мировой порядок или порядок внутренних частей");
                }
                int revision=outline.GeometryRevisionCount;
                int bindings=outline.BindingRevisionCount;
                outline.Prepare(camera);outline.Prepare(camera);
                Require(outline.GeometryRevisionCount==revision && outline.BindingRevisionCount==bindings,"Повторная подготовка неизменного контура перестраивает геометрию/регистрацию");
                outer.transform.localScale=new Vector3(-1,2,1);outer.transform.rotation=Quaternion.Euler(0,0,37);
                artwork.transform.position=new Vector3(7,8,.25f);outline.Prepare(camera);
                CheckBars(outline,rectangle,.25f,camera,outer);
                outer.transform.position=new Vector3(13,-9,.3f);outline.Prepare(camera);
                CheckBars(outline,rectangle,.55f,camera,outer);
                int warnings=0;
                Application.LogCallback observe=(message,stack,type)=>{if(type==LogType.Warning && message.StartsWith("Контур взаимодействия скрыт: target=Map/outline-map-check",StringComparison.Ordinal)) warnings++;};
                Application.logMessageReceived+=observe;
                try
                {
                    artwork.enabled=false;Require(!source.Visible,"Disabled изображение осталось видимым источником");
                    for(int i=0;i<1000;i++) outline.Prepare(camera);
                    Require(!outline.Visible && warnings==1 && outline.BindingRevisionCount==bindings && outline.Renderers.All(bar=>bar.enabled && bar.forceRenderingOff),
                        "Missing visual не скрывает контур, меняет регистрацию или пишет предупреждение каждый кадр");
                }
                finally {Application.logMessageReceived-=observe;artwork.enabled=true;}
                outline.Prepare(camera);Require(outline.Visible && outline.BindingRevisionCount==bindings,"Повторное включение actual Renderer не восстановило контур без поздней регистрации");
                artwork.forceRenderingOff=true;Require(!source.Visible,"forceRenderingOff изображение осталось видимым источником");artwork.forceRenderingOff=false;
                outline.SetTarget(selected,false);outline.Prepare(camera);Require(!outline.Visible,"Уход курсора/блокировка ввода оставляет контур");
                outline.SetTarget(default,true);outline.Prepare(camera);Require(!outline.Visible,"None цель оставляет контур");
                camera.orthographic=false;Require(!WorldInteractionOutline.TryWorldThickness(camera,out _,out var reason) && reason!=null,"Perspective камера получила скрытый fallback");
                camera.orthographic=true;camera.transform.rotation=Quaternion.Euler(10,0,0);
                Require(!WorldInteractionOutline.TryWorldThickness(camera,out _,out reason) && reason!=null,"Наклонённая камера получила неверную толщину");
                camera.transform.rotation=Quaternion.identity;
                UnityEngine.Object.DestroyImmediate(artwork.gameObject);Require(!source.Visible,"Удалённый Renderer подменён изображением группы");
                var replacement=new GameObject("Новое фактическое изображение").AddComponent<SpriteRenderer>();replacement.sprite=square;replacement.transform.SetParent(inner.transform,false);
                source=WorldOutlineSource.ForRenderer(replacement);outline.SetTarget(selected,true);outline.Prepare(camera);Require(outline.Visible,"Замена фактического изображения не восстановила контур");
                UnityEngine.Object.DestroyImmediate(outer.gameObject);Require(outline.Renderers.All(bar=>bar==null),"Контур пережил удаление своего выбранного объекта");
                outer=new GameObject("Следующая комната").AddComponent<SortingGroup>();replacement=new GameObject("Карта следующей комнаты").AddComponent<SpriteRenderer>();
                replacement.sprite=square;replacement.transform.SetParent(outer.transform,false);source=WorldOutlineSource.ForRenderer(replacement);
                outline.SetTarget(selected,true);outline.Prepare(camera);CheckBars(outline,rectangle,0,camera,outer);
                Require(outline.Renderers.All(bar=>bar.sprite==preparedSprite && bar.sharedMaterial==preparedMaterial),"Смена комнаты повторно создаёт подготовленные Sprite/material");
                hint=new InteractionHint(canvas.transform,new HudElements(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")));
                hint.ConfigureWorldOutline(camera,_=>source);hint.Refresh(new TravelPoint {id=selected.id,title="Карта",x=-2,y=-1.5f,width=4,height=3},true,null);hint.Highlight(selected,true);
                Require(canvas.GetComponentsInChildren<Image>(true).Length==1 && canvas.GetComponentsInChildren<Text>(true).Single().text=="E · Карта",
                    "Контур остался в UI или изменил текст E");
                Require(canvas.GetComponent<Canvas>().renderMode==RenderMode.ScreenSpaceOverlay,"Текст E перестал быть overlay");
                hint.WorldOutline.Prepare(camera);Require(hint.WorldOutline.Visible,"Настроенный hint не передал выбранную цель");
                hint.Refresh((ContainerState)null,false);hint.WorldOutline.Prepare(camera);Require(!hint.WorldOutline.Visible,"Закрытие UI/session сохраняет старый контур без нового Highlight");
                CheckTravelRegistry();
                Debug.Log("Мировой контур: PASS — 3 физических пикселя при 9 размерах/zoom, внешний owner, flip/поворот/поздняя глубина, неизменная геометрия, target None/уход курсора, смена Renderer/комнаты, required artwork, E overlay и saved travel registry. Raster проверяется отдельным RenderMapAndPony.");
            }
            finally {hint?.Dispose();outline.Dispose();UnityEngine.Object.DestroyImmediate(canvas);UnityEngine.Object.DestroyImmediate(outer.gameObject);UnityEngine.Object.DestroyImmediate(camera.gameObject);UnityEngine.Object.DestroyImmediate(square);}
        }
        private static void CheckTravelRegistry()
        {
            var room=new GameObject("Сохранённая комната для реестра").AddComponent<RoomAuthoring>();
            var map=new GameObject("Сохранённый рисунок карты");map.transform.SetParent(room.transform,false);
            var artwork=map.AddComponent<SpriteRenderer>();var authoring=map.AddComponent<RoomTravelPointAuthoring>();
            authoring.Configure("saved-map",TravelPointKind.Map,"Карта","factory:0",null,Vector2.one,Vector2.zero);
            var registry=new TravelPointVisuals();
            try
            {
                registry.Bind(room.gameObject);Require(registry.Find("saved-map")==artwork,"Реестр карты не использует фактический saved Renderer");
                authoring.enabled=false;registry.Bind(room.gameObject);Require(registry.Find("saved-map")==artwork,"Повторный Bind того же instance пересканировал комнату без явной смены регистрации");
                registry.Clear();registry.Bind(room.gameObject);Require(registry.Find("saved-map")==null,"Отключённый authored marker попал в новый реестр");
                authoring.enabled=true;registry.Clear();registry.Bind(room.gameObject);registry.Clear();Require(registry.Find("saved-map")==null,"Clear оставляет карту предыдущей комнаты");
            }
            finally {UnityEngine.Object.DestroyImmediate(room.gameObject);}
        }
        // Presentation fixture only: an unchanged saved hub-map sprite and actual
        // assembled pony. It is not evidence of an ordinary gameplay route.
        public static void RenderMapAndPony()
        {
            ActorTypes.Load();Anatomy.Load();PonyVisual.Prepare();
            var authoring=RoomAuthoring.Load("hub");var sourcePoint=authoring.GetComponentsInChildren<RoomTravelPointAuthoring>(true).Single(p=>p.StableId==TravelPointAuthoring.MapId);
            var point=sourcePoint.Read(authoring,"hub");var map=UnityEngine.Object.Instantiate(sourcePoint.gameObject);map.name="Фактическая сохранённая карта";
            const int layer=31;map.layer=layer;
            var group=new GameObject("Слой карты проверки").AddComponent<SortingGroup>();group.gameObject.layer=layer;group.sortingOrder=10;map.transform.SetParent(group.transform,true);
            var sprite=map.GetComponent<SpriteRenderer>();var outlineSource=WorldOutlineSource.ForRenderer(sprite);
            var actor=new ActorState {id="outline-pony-check",actorType="pony",anatomy="pony",pose="idle",x=point.Bounds.xMin,y=point.Bounds.yMin-.2f,
                appearance=new CharacterAppearance {version=1,fur=0x4890B5,hair=0x782858,eyes=0x0F7539}};
            var pony=(PonyVisual)ActorVisualFactory.Create(actor);pony.Bind(actor);pony.DrawPose(0,true);
            var actorGroup=new GameObject("Пони целиком перед картой").AddComponent<SortingGroup>();actorGroup.gameObject.layer=layer;actorGroup.sortingOrder=100;
            pony.transform.SetParent(actorGroup.transform,true);foreach(var node in pony.GetComponentsInChildren<Transform>(true)) node.gameObject.layer=layer;
            var camera=new GameObject("Камера мирового контура").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=2.5f;
            camera.transform.position=new Vector3(point.Bounds.center.x,point.Bounds.center.y,-10);camera.cullingMask=1<<layer;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;
            var texture=new RenderTexture(1920,1080,24);texture.Create();camera.targetTexture=texture;
            using var outline=new WorldInteractionOutline(camera,_=>outlineSource);
            var snapshots=new List<object>();string phase="map-only";
            void Snapshot(string stage)
            {
                snapshots.Add(new {phase,stage,bindings=outline.BindingRevisionCount,
                    groups=new[]{GroupEvidence(group),GroupEvidence(actorGroup),GroupEvidence(pony.GetComponent<SortingGroup>())},
                    map=RendererEvidence(sprite),bars=outline.Renderers.Select(RendererEvidence).ToArray(),
                    pony=pony.GetComponentsInChildren<Renderer>(true).Where(r=>r.enabled).Select(RendererEvidence).ToArray()});
            }
            void BeforeBuiltin(Camera value) {if(value==camera) Snapshot("camera-hook-after-geometry");}
            void BeforePipeline(ScriptableRenderContext context,Camera value) {if(value==camera) Snapshot("camera-hook-after-geometry");}
            Camera.onPreCull+=BeforeBuiltin;RenderPipelineManager.beginCameraRendering+=BeforePipeline;
            try
            {
                var target=new InteractionTarget(InteractionKind.Map,point.id,point.Bounds);
                actorGroup.gameObject.SetActive(false);using var mapOnly=Read(camera,texture,()=>Snapshot("after-group-pass-before-camera"));
                outline.SetTarget(target,true);Require(outline.Visible && outline.Renderers.All(bar=>RenderSorting.FindOwner(bar)==group),"Рамка не зарегистрирована внутри actual owner до группового прохода");
                phase="map-outline";using var rear=Read(camera,texture,()=>Snapshot("after-group-pass-before-camera"));
                outline.SetTarget(target,false);actorGroup.gameObject.SetActive(true);group.gameObject.SetActive(false);phase="pony-only";
                using var front=Read(camera,texture,()=>Snapshot("after-group-pass-before-camera"));
                group.gameObject.SetActive(true);outline.SetTarget(target,true);phase="combined";
                using var actual=Read(camera,texture,()=>Snapshot("after-group-pass-before-camera"));
                var m=mapOnly.Image.GetPixels32();var r=rear.Image.GetPixels32();var f=front.Image.GetPixels32();var a=actual.Image.GetPixels32();int hiddenOutline=0,opaque=0,errors=0;
                for(int i=0;i<a.Length;i++) if(f[i].a==255)
                {
                    opaque++;if(r[i].r>180 && r[i].g>150 && r[i].b<100 &&
                        (Math.Abs(r[i].r-m[i].r)>10 || Math.Abs(r[i].g-m[i].g)>10 || Math.Abs(r[i].b-m[i].b)>10)) hiddenOutline++;
                    if(Math.Abs(a[i].r-f[i].r)>2 || Math.Abs(a[i].g-f[i].g)>2 || Math.Abs(a[i].b-f[i].b)>2) errors++;
                }
                string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../..", ".reference/tests/world-outline"));Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory,"map-and-pony-1920x1080.png"),actual.Image.EncodeToPNG());
                File.WriteAllBytes(Path.Combine(directory,"map-and-outline-alone-1920x1080.png"),rear.Image.EncodeToPNG());
                File.WriteAllBytes(Path.Combine(directory,"pony-alone-1920x1080.png"),front.Image.EncodeToPNG());
                File.WriteAllText(Path.Combine(directory,"sorting-evidence.json"),JsonConvert.SerializeObject(new {opaque,hiddenOutline,errors,snapshots},Formatting.Indented));
                Require(opaque>1000 && hiddenOutline>10 && errors==0,$"Карта/контур перекрывают actual пони или нет реального перекрытия: opaque={opaque}, contourUnderPony={hiddenOutline}, errors={errors}; PNG {directory}");
                Debug.Log($"Контур за actual пони: PASS — opaque={opaque}, contourUnderPony={hiddenOutline}, errors={errors}; PNG {directory}. E overlay структурно проверен Run; полный gameplay screenshot должен подтвердить runtime wiring.");
            }
            finally
            {
                Camera.onPreCull-=BeforeBuiltin;RenderPipelineManager.beginCameraRendering-=BeforePipeline;
                outline.Dispose();camera.targetTexture=null;texture.Release();UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(camera.gameObject);
                UnityEngine.Object.DestroyImmediate(group.gameObject);UnityEngine.Object.DestroyImmediate(actorGroup.gameObject);
            }
        }
        private static object GroupEvidence(SortingGroup group)=>group==null?null:new
        {name=group.name,enabled=group.enabled,active=group.gameObject.activeInHierarchy,order=group.sortingOrder,layer=group.sortingLayerID,sortAtRoot=group.sortAtRoot};
        private static object RendererEvidence(Renderer renderer)
        {
            if(renderer==null) return null;var owner=RenderSorting.FindOwner(renderer);
            return new {name=renderer.name,type=renderer.GetType().Name,enabled=renderer.enabled,active=renderer.gameObject.activeInHierarchy,
                forceOff=renderer.forceRenderingOff,order=renderer.sortingOrder,layer=renderer.sortingLayerID,owner=GroupEvidence(owner),
                nativeSortingGroupId=NativeGroupingValue(renderer,"sortingGroupID"),nativeSortingGroupOrder=NativeGroupingValue(renderer,"sortingGroupOrder"),
                materials=renderer.sharedMaterials.Select(m=>m==null?null:new {name=m.name,shader=m.shader.name,queue=m.renderQueue}).ToArray()};
        }
        // Optional diagnostic fields depend on Unity's exposed/internal bindings;
        // absence is recorded explicitly and never affects the pixel acceptance.
        private static object NativeGroupingValue(Renderer renderer,string member)
        {
            var property=typeof(Renderer).GetProperty(member,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            return property==null?new {available=false,value=(object)null}:new {available=true,value=property.GetValue(renderer)};
        }
        private sealed class Pixels : IDisposable
        {
            public Texture2D Image {get;}
            public Pixels(Texture2D image)=>Image=image;
            public void Dispose()=>UnityEngine.Object.DestroyImmediate(Image);
        }
        private static void CheckBars(WorldInteractionOutline outline,Rect rectangle,float depth,Camera camera,SortingGroup owner)
        {
            Require(outline.Visible && outline.Renderers.Count==4,"Мировой контур не состоит из четырёх включённых actual SpriteRenderer");
            var centers=new[]{new Vector3(rectangle.center.x,rectangle.yMin,depth),new Vector3(rectangle.center.x,rectangle.yMax,depth),
                new Vector3(rectangle.xMin,rectangle.center.y,depth),new Vector3(rectangle.xMax,rectangle.center.y,depth)};
            float width=3*2*camera.orthographicSize/camera.pixelHeight;
            for(int i=0;i<4;i++)
            {
                var bar=outline.Renderers[i];Require(RenderSorting.FindOwner(bar)==owner && bar.sortingOrder==short.MaxValue,
                    "Сторона контура вышла из actual owner или изменила локальный порядок");
                var axis=i<2?Vector3.up:Vector3.right;
                float thickness=bar.transform.TransformVector(Vector3.Scale(axis,bar.sprite.bounds.size)).magnitude;
                Require(Mathf.Abs(thickness*camera.pixelHeight/(2*camera.orthographicSize)-3)<.00001f,"Actual Sprite-сторона не равна трём физическим пикселям");
                var size=i<2?new Vector3(rectangle.width+width,width,0):new Vector3(width,rectangle.height+width,0);
                var bounds=bar.bounds;
                Require((bounds.center-centers[i]).sqrMagnitude<.00003f*.00003f &&
                    new Vector2(bounds.size.x-size.x,bounds.size.y-size.y).sqrMagnitude<.00003f*.00003f,
                    $"Actual Sprite bounds изменили выбранную мировую рамку при transform/flip: side={i}, actual={bounds}, expectedCenter={centers[i]}, expectedSize={size}");
                // Renderer bounds include Unity's Z culling padding. Geometry
                // comes from actual 2D Sprite vertices at Z=0, not those bounds.
                var corners=new[]{centers[i]+new Vector3(-size.x,-size.y,0)*.5f,centers[i]+new Vector3(-size.x,size.y,0)*.5f,
                    centers[i]+new Vector3(size.x,-size.y,0)*.5f,centers[i]+new Vector3(size.x,size.y,0)*.5f};
                var seen=new bool[4];var vertices=bar.sprite.vertices;
                Require(vertices.Length==4,"Подготовленный Sprite рамки не содержит четырёх фактических углов");
                foreach(var vertex in vertices)
                {
                    var actual=bar.transform.TransformPoint(new Vector3(vertex.x,vertex.y,0));int match=-1;
                    for(int corner=0;corner<corners.Length;corner++) if((actual-corners[corner]).sqrMagnitude<.00003f*.00003f) {match=corner;break;}
                    Require(match>=0 && !seen[match],"Поворот/неравномерный flip исказили настоящие углы Sprite-стороны");seen[match]=true;
                }
            }
        }
        private static Pixels Read(Camera camera,RenderTexture texture,Action afterGroupPass=null)
        {
            SortingGroup.UpdateAllSortingGroups();
            afterGroupPass?.Invoke();
            if(GraphicsSettings.currentRenderPipeline==null) camera.Render();
            else
            {
                var request=new RenderPipeline.StandardRequest {destination=texture};
                Require(RenderPipeline.SupportsRenderRequest(camera,request),"Активный SRP не поддерживает StandardRequest; raster evidence нельзя заменить built-in рендером");
                RenderPipeline.SubmitRenderRequest(camera,request);
            }
            var previous=RenderTexture.active;var image=new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false);
            try {RenderTexture.active=texture;image.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);image.Apply();}
            finally {RenderTexture.active=previous;}
            return new Pixels(image);
        }
        private static void Require(bool condition,string reason) {if(!condition) throw new InvalidOperationException("Мировой контур: "+reason);}
    }
}
