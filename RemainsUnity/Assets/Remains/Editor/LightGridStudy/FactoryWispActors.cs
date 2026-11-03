using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using Remains.RoomFormat;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    // Detached Editor fixture. The visible pose is the only source of body/weapon shadow geometry.
    // Nothing is installed in the factory prefab, runtime world or Player.
    internal sealed class FactoryWispActors : IDisposable
    {
        private sealed class Entry
        {
            public ActorState State;
            public ActorVisual Visual;
            public WeaponView Weapon;
            public ActorCastShadowView Shadow;
            public Bounds BodyBounds,RenderedBounds;
            public float OpaqueBottom,OpaqueTop;
            public bool HasAtlasOpaqueBounds;
            public Rect AtlasOpaqueBounds;
            public readonly List<LightOccluder> Parts=new();
        }

        private readonly List<Entry> entries=new();
        private readonly List<string> actorIds=new();
        private readonly RoomLayout layout;
        private readonly Rect roomBounds;
        private readonly int layer;
        private readonly ActorCastShadowView shadowPrefab;
        private readonly ActorShadowSource[] singleSource=new ActorShadowSource[1];
        public GameObject Root {get;}
        public IReadOnlyList<string> ActorIds=>actorIds;
        public Bounds DrawnBounds {get;private set;}

        public FactoryWispActors(Transform parent,RoomLayout wholeLevelLayout,Rect actualRoomBounds,int layer)
        {
            layout=wholeLevelLayout??throw new ArgumentNullException(nameof(wholeLevelLayout));
            roomBounds=actualRoomBounds;this.layer=layer;
            if(layout.platforms==null || roomBounds.width<=0 || roomBounds.height<=0 || layer<0 || layer>31)
                throw new ArgumentException($"Актёры виспов: неверная геометрия комнаты {layout.id}, bounds={roomBounds}, layer={layer}.");
            Root=new GameObject("Настоящие актёры проверки виспов");Root.transform.SetParent(parent,false);
            try
            {
                ActorTypes.Load();PropCatalog.Load();Anatomy.Load();WeaponCatalog.Load();WeaponRigs.Load();PonyVisual.Prepare();
                shadowPrefab=GameAssets.Load<ActorCastShadowView>(ActorCastShadowView.ResourcePath)
                    ??throw new InvalidOperationException("Актёры виспов: отсутствует авторский префаб Presentation/ActorCastShadow.");
                shadowPrefab.Validate();
                Add("pony",.28f,1,"pistol");
                Add("raider",.58f,-1,"pistol");
                Add("rat",.79f,1,null);
                var drawn=entries[0].RenderedBounds;
                for(int i=1;i<entries.Count;i++) drawn.Encapsulate(entries[i].RenderedBounds);
                DrawnBounds=drawn;
                Debug.Log($"Актёры виспов: room={layout.id}, bounds={roomBounds}, актёров={entries.Count}, изображение={DrawnBounds}; настоящие pony/raider/rat, два префаба pistol.");
            }
            catch {Dispose();throw;}
        }

        private void Add(string type,float fraction,int facing,string weaponId)
        {
            var definition=ActorTypes.Get(type);
            if(!EntityAssetCatalog.Load().TryGetActorPrefab(definition,out var authored) || authored==null)
                throw new InvalidOperationException($"Актёры виспов: нет авторского префаба type={type}, room={layout.id}.");
            var state=new ActorState
            {
                id="wisp-study:"+type,actorType=type,anatomy=type,level=layout.id,pose="idle",poseTime=0,
                facing=facing,player=type=="pony",connected=true,weapon=weaponId,weaponReach=.25f,
                weaponAngle=facing>0?8:172,weaponFlipY=facing<0,
                appearance=type=="pony"?new CharacterAppearance
                {version=CharacterAppearance.CurrentVersion,hairStyle=5,eyeStyle=6,fur=0xB8B8B8,hair=0x386BA0,eyes=0x49BB70}:null
            };
            var entry=new Entry{State=state};entries.Add(entry);actorIds.Add(state.id);
            entry.Visual=ActorVisualFactory.Create(state);entry.Visual.transform.SetParent(Root.transform,false);
            if(entry.Visual is PonyVisual pony) pony.ConfigureMagicPreview(0);
            entry.Visual.Bind(state);entry.Visual.enabled=false;
            if(weaponId!=null)
            {
                string presentation=WeaponRigs.PresentationId(weaponId);
                entry.Weapon=Object.Instantiate(WeaponRigs.Prefab(presentation),Root.transform,false);
                entry.Weapon.SetFrame(entry.Weapon.ResolveRestSprite());
                entry.Weapon.Artwork.sortingOrder=WorldVisualOrderPolicy.FirstOrder(WorldVisualCategory.Player)+1;
            }
            if(entry.Visual.TryGetComponent<SortingGroup>(out var sorting))
                sorting.sortingOrder=WorldVisualOrderPolicy.FirstOrder(WorldVisualCategory.Player);
            Sample(entry);
            if(entry.Visual is AtlasActorVisual)
            {
                // Measure the imported texture of the sprite actually selected by Draw, not an optional atlas bake.
                // This is fixture preparation, once per actor; the gameplay assets remain untouched.
                entry.Parts.Clear();entry.Visual.AppendLightOccluders(entry.Parts);
                if(entry.Parts.Count!=1 || entry.Parts[0].Sprite==null)
                    throw new InvalidOperationException($"Актёры виспов: ожидался один нарисованный спрайт атласа, actor={state.id}, parts={entry.Parts.Count}.");
                entry.AtlasOpaqueBounds=ReadImportedOpaqueBounds(entry.Parts[0].Sprite);
                entry.HasAtlasOpaqueBounds=true;Sample(entry);
                Debug.Log($"Актёры виспов: непрозрачная GPU-графика actor={state.id}, sprite={entry.Parts[0].Sprite.name}, local={entry.AtlasOpaqueBounds}, world={entry.BodyBounds}.");
            }
            entry.OpaqueBottom=entry.BodyBounds.min.y-state.y;entry.OpaqueTop=entry.BodyBounds.max.y-state.y;
            Place(entry,fraction);
            entry.Parts.Clear();entry.Visual.AppendLightOccluders(entry.Parts);
            if(entry.Weapon!=null)
            {
                var renderer=entry.Weapon.Artwork;
                if(renderer.sprite==null || !renderer.enabled)
                    throw new InvalidOperationException($"Актёры виспов: оружие не нарисовано, actor={state.id}, weapon={weaponId}.");
                entry.Parts.Add(new LightOccluder(entry.Weapon.ResolveRestSprite(),renderer.transform.localToWorldMatrix,renderer.color.a));
            }
            if(entry.Parts.Count==0) throw new InvalidOperationException($"Актёры виспов: пустой силуэт actor={state.id}, type={type}.");
            entry.Shadow=Object.Instantiate(shadowPrefab,Root.transform,false);
            entry.Shadow.name="Настоящая проекция: "+state.id;
            foreach(var child in entry.Visual.GetComponentsInChildren<Transform>(true)) child.gameObject.layer=layer;
            if(entry.Weapon!=null) foreach(var child in entry.Weapon.GetComponentsInChildren<Transform>(true)) child.gameObject.layer=layer;
            Debug.Log($"Актёры виспов: actor={state.id}, type={type}, pos={state.Position}, body={entry.BodyBounds}, parts={entry.Parts.Count}, weapon={weaponId??"нет"}.");
        }

        private static void Sample(Entry entry)
        {
            if(entry.Visual is PonyVisual pony) pony.DrawPose(0,true);
            else if(entry.Visual is AtlasActorVisual atlas) atlas.Draw();
            else throw new InvalidOperationException($"Актёры виспов: не поддержан настоящий тип представления {entry.Visual.GetType().Name}, actor={entry.State.id}.");
            Bounds bounds;
            if(entry.HasAtlasOpaqueBounds)
            {
                entry.Parts.Clear();entry.Visual.AppendLightOccluders(entry.Parts);
                if(entry.Parts.Count!=1 || entry.Parts[0].Sprite==null)
                    throw new InvalidOperationException($"Актёры виспов: исчез выбранный спрайт actor={entry.State.id}.");
                var matrix=entry.Parts[0].LocalToWorld;var local=entry.AtlasOpaqueBounds;
                bounds=new Bounds(matrix.MultiplyPoint3x4(new Vector3(local.xMin,local.yMin,0)),Vector3.zero);
                bounds.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(local.xMax,local.yMin,0)));
                bounds.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(local.xMax,local.yMax,0)));
                bounds.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(local.xMin,local.yMax,0)));
                entry.BodyBounds=bounds;
            }
            else if(entry.Visual.TryGetDrawnBounds(out bounds)) entry.BodyBounds=bounds;
            else
            {
                bool found=false;bounds=default;
                foreach(var renderer in entry.Visual.GetComponentsInChildren<Renderer>())
                {
                    if(!renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy) continue;
                    if(!found) {bounds=renderer.bounds;found=true;} else bounds.Encapsulate(renderer.bounds);
                }
                if(!found) throw new InvalidOperationException($"Актёры виспов: не нарисовано тело actor={entry.State.id}.");
                entry.BodyBounds=bounds;
            }
            entry.RenderedBounds=entry.BodyBounds;
            if(entry.Weapon==null) return;
            var pose=WeaponPose.SampleRanged(entry.State);
            entry.Weapon.transform.position=pose.Root;entry.Weapon.transform.rotation=Quaternion.Euler(0,0,pose.Angle);
            entry.Weapon.SetMirrored(pose.Mirrored);entry.Weapon.SetVisible(true);
            entry.RenderedBounds.Encapsulate(entry.Weapon.Artwork.bounds);
        }

        private static Rect ReadImportedOpaqueBounds(Sprite sprite)
        {
            var rect=sprite.rect;
            if(sprite.texture==null || rect.width<1 || rect.height<1)
                throw new InvalidOperationException($"Актёры виспов: повреждён выбранный спрайт {sprite.name}, rect={rect}.");
            var target=RenderTexture.GetTemporary(sprite.texture.width,sprite.texture.height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            var previous=RenderTexture.active;Texture2D image=null;
            try
            {
                Graphics.Blit(sprite.texture,target);RenderTexture.active=target;
                image=new Texture2D((int)rect.width,(int)rect.height,TextureFormat.RGBA32,false,true);
                image.ReadPixels(rect,0,0);image.Apply();
                return SpriteOpaqueBoundsBaker.Measure(image.GetPixels32(),image.width,
                    new Rect(0,0,image.width,image.height),sprite.pivot,sprite.pixelsPerUnit);
            }
            catch(Exception error)
            {throw new InvalidOperationException($"Актёры виспов: не измерена альфа выбранного GPU-спрайта {sprite.name}, texture={sprite.texture.name}, rect={rect}.",error);}
            finally
            {
                RenderTexture.active=previous;if(image!=null) Object.DestroyImmediate(image);
                RenderTexture.ReleaseTemporary(target);
            }
        }

        private void Place(Entry entry,float fraction)
        {
            float desired=Mathf.Lerp(roomBounds.xMin,roomBounds.xMax,fraction);
            float bodyLeft=entry.BodyBounds.min.x-entry.State.x,bodyRight=entry.BodyBounds.max.x-entry.State.x;
            var floors=new List<RoomSurface>();
            foreach(var surface in layout.platforms)
            {
                float top=surface.y+surface.height;
                if(surface.slopeDirection==0 && surface.width>.3f && top>=roomBounds.yMin-.06f &&
                    top<roomBounds.yMax-.5f && surface.x<roomBounds.xMax && surface.x+surface.width>roomBounds.xMin)
                    floors.Add(surface);
            }
            floors.Sort((a,b)=>(a.y+a.height).CompareTo(b.y+b.height));
            int attempts=0;
            foreach(var floor in floors)
            {
                float left=Mathf.Max(roomBounds.xMin+.2f,floor.x+.03f-bodyLeft);
                float right=Mathf.Min(roomBounds.xMax-.2f,floor.x+floor.width-.03f-bodyRight);
                if(left>right) continue;
                float anchor=Mathf.Clamp(desired,left,right);
                int steps=Mathf.CeilToInt((right-left)/.25f)+1;
                for(int index=0;index<=steps*2;index++)
                {
                    float x=anchor+(index==0?0:((index+1)/2)*.25f*(index%2==1?1:-1));
                    if(x<left || x>right) continue;
                    attempts++;entry.State.x=x;entry.State.y=floor.y+floor.height+.012f-entry.OpaqueBottom;
                    Sample(entry);
                    var occupied=Occupancy(entry);
                    if(occupied.xMin<roomBounds.xMin+.03f || occupied.xMax>roomBounds.xMax-.03f || occupied.yMax>roomBounds.yMax-.03f) continue;
                    bool blocked=false;
                    foreach(var surface in layout.platforms)
                        // Slopes are conservatively reserved as their whole bounds, rather than ignored.
                        if(!surface.oneWay && occupied.Overlaps(new Rect(surface.x,surface.y,surface.width,surface.height))) {blocked=true;break;}
                    if(blocked) continue;
                    foreach(var previous in entries)
                    {
                        if(previous==entry || previous.Shadow==null) continue;
                        var reserved=Occupancy(previous);reserved.xMin-=.3f;reserved.xMax+=.3f;
                        if(occupied.Overlaps(reserved)) {blocked=true;break;}
                    }
                    if(!blocked) return;
                }
            }
            throw new InvalidOperationException($"Актёры виспов: actor={entry.State.id} не помещается на реальном полу room={layout.id}, bounds={roomBounds}, floorCandidates={floors.Count}, attempts={attempts}, lastBody={entry.BodyBounds}.");
        }

        private static Rect Occupancy(Entry entry)
        {
            var body=entry.BodyBounds;
            var result=Rect.MinMaxRect(body.min.x+.025f,entry.State.y+entry.OpaqueBottom+.003f,body.max.x-.025f,entry.State.y+entry.OpaqueTop-.003f);
            if(entry.Weapon==null) return result;
            var weapon=entry.Weapon.Artwork.bounds;
            return Rect.MinMaxRect(Mathf.Min(result.xMin,weapon.min.x),Mathf.Min(result.yMin,weapon.min.y),
                Mathf.Max(result.xMax,weapon.max.x),Mathf.Max(result.yMax,weapon.max.y));
        }

        // Masks are borrowed from the actual pipeline and live until the next call. Compose them immediately.
        // RGB stays in the caller's same-ID direct source field; it must not tint the mask or remove static fill.
        public void AppendProjectedShadows(string sourceId,Vector2 position,float radius,RoomLightingTable settings,List<ProjectedLightShadow> output)
        {
            if(string.IsNullOrEmpty(sourceId) || !float.IsFinite(position.x) || !float.IsFinite(position.y) || !float.IsFinite(radius) || radius<0)
                throw new ArgumentException($"Актёры виспов: неверный источник id={sourceId}, position={position}, radius={radius}.");
            singleSource[0]=new ActorShadowSource(sourceId,position,radius,1);
            AppendProjectedShadows(singleSource,settings,output);
        }

        public void AppendProjectedShadows(IReadOnlyList<ActorShadowSource> sources,RoomLightingTable settings,List<ProjectedLightShadow> output)
        {
            if(Root==null) throw new ObjectDisposedException(nameof(FactoryWispActors));
            if(sources==null || settings==null || output==null) throw new ArgumentNullException("Актёры виспов: нужны источники, настройки и список масок.");
            foreach(var entry in entries)
            {
                // Prepare only when capacity/assets change: all source positions reuse the same sampled pose mask.
                if(entry.Shadow.NeedsPreparation(entry.Parts,sources.Count)) entry.Shadow.Prepare(entry.Parts,sources.Count);
                Vector2 center=entry.Visual.TryGetDrawnBounds(out var drawn)?drawn.center:entry.RenderedBounds.center;
                entry.Shadow.Draw(center,entry.Parts,sources,settings);
                int before=output.Count;entry.Shadow.AppendProjectedLightShadows(output);
                if(output.Count-before!=sources.Count)
                    throw new InvalidOperationException($"Актёры виспов: actor={entry.State.id}, запрошено масок={sources.Count}, получено={output.Count-before}; проверьте actorShadow settings/coverage.");
            }
        }

        public void Dispose()
        {
            // ActorCastShadowView.OnDestroy releases the pose/slot RTs and owned sprite meshes.
            if(Root!=null) Object.DestroyImmediate(Root);
            entries.Clear();actorIds.Clear();
        }
    }

    // Fast verification of the authored actor fixture without building or rendering the factory.
    public static class FactoryWispActorChecks
    {
        public static void RunBatch()
        {
            try
            {
                var layout=new RoomLayout{id="wisp-actor-check",left=0,right=24,bottom=0,top=7,
                    platforms=new[]{new RoomSurface{x=0,y=0,width=24,height=.2f}},ladders=Array.Empty<RoomLadder>()};
                using var actors=new FactoryWispActors(null,layout,new Rect(0,.2f,24,6.8f),31);
                var settings=new RoomLightingTable{ActorShadowDepth=.2,ActorShadowMaxOffset=.6,ActorShadowMaxOpacity=.65,
                    ActorShadowMaxSources=3,ActorShadowMaxScale=3,ActorShadowMoveThreshold=.05};
                var sources=new[]{new ActorShadowSource("check:left",new Vector2(1,4),.04f,1),
                    new ActorShadowSource("check:right",new Vector2(22,4),.04f,1)};
                var masks=new List<ProjectedLightShadow>();actors.AppendProjectedShadows(sources,settings,masks);
                if(actors.ActorIds.Count!=3 || masks.Count!=6)
                    throw new InvalidOperationException($"Проверка актёров виспов: actorCount={actors.ActorIds.Count}, maskCount={masks.Count}, ожидалось 3/6.");
                var previous=RenderTexture.active;
                try
                {
                    foreach(var mask in masks)
                    {
                        if(mask.Mask is not RenderTexture texture || mask.Opacity<=0)
                            throw new InvalidOperationException($"Проверка актёров виспов: пустая проекция source={mask.SourceId}.");
                        Texture2D image=null;
                        try
                        {
                            image=new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false,true);
                            RenderTexture.active=texture;image.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);image.Apply();
                            int nonempty=0;foreach(var pixel in image.GetPixels32()) if(pixel.a>0) nonempty++;
                            if(nonempty<16) throw new InvalidOperationException($"Проверка актёров виспов: слишком мало альфы source={mask.SourceId}, bounds={mask.WorldBounds}, pixels={nonempty}.");
                            Debug.Log($"Проверка актёров виспов: source={mask.SourceId}, bounds={mask.WorldBounds}, непрозрачных пикселей={nonempty}.");
                        }
                        finally {if(image!=null) Object.DestroyImmediate(image);}
                    }
                }
                finally {RenderTexture.active=previous;}
                Debug.Log("FactoryWispActorChecks: PASS — настоящие pony/raider/rat, два pistol, GPU-границы и шесть непустых масок двух источников.");
                EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
    }
}
