using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.RoomFormat;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Remains.Presentation
{
    // Visuals of a level built from room templates (ITemplateLevel: the generated factory, the hub): walls from the
    // composed grid with backdrops per room, ambient light, and — inside a light window of rooms around a focus point — the background erase
        // mask (windows) and the radiance field lit by the installed room prefabs' pictures (RoomSpriteAuthoring).
    // The whole 4×21 level is too large for one field (≈1500×4000 texels at 10/m, cascades of ~3000×8000)
    // and one erase mask (>16384 px), so both cover only the window; outside it windows are not cut and lamps
    // do not light. Everything is built in explicit calls (Initialize, Focus, RefreshLight), never per frame.
    // Doors: SyncDoors(states, revision) adds closed doors as light occluders (radiance scene); grid walls stay static.
    // Lifetime: Initialize after FactoryLevelRoot.InstantiateRooms, Release before the root is destroyed.
    public sealed class FactoryLevelView : MonoBehaviour,IRoomLighting
    {
        public const int WindowRooms=3;            // rooms across and down in the light window (odd)
        public const int SettleUpdates=8;
        // After a door or lamp event the field gets one update at once and the rest of its settle updates one per
        // frame, then the stale shadow fields one per frame: a local event costs a few milliseconds of video card
        // per frame instead of the whole window's passes in one frame.
        private int pendingSettle;
        private Coroutine settling;
        public const string AmbientResource="Presentation/FactoryAmbientLight";          // field updates after a rebuild: one wall bounce per update

        private ITemplateLevel root;
        private int roomsWide,roomsDeep;
        private RoomLightingTable table;
        private BrokenLampArt brokenLampArt;
        private readonly HashSet<string> warnedLampArt=new(StringComparer.Ordinal);
        private GameObject terrain,ambient,window;
        private RoomRadianceField field;
        private LightWindowContext activeWindow,candidateWindow;
        private Coroutine preparingWindow;
        private System.Collections.IEnumerator preparationWork;
        private readonly HashSet<string> requestedShadowIds=new(StringComparer.Ordinal);
        public bool IsPreparingWindow=>preparingWindow!=null;
        private RectInt requestedWindow;
        private Vector2 requestedFocus;
        private bool requestedCommit;
        private bool windowRequested;
        private const int PreparationStagesPerFrame=2; // Explicit command-count cap, not a GPU-time guarantee.

        private SortingGroup sorting;
        private RectInt windowRooms=new RectInt(-1,-1,0,0);
        private readonly List<(RoomSpriteAuthoring sprite,RoomDefinition definition)> pictures=new();
        private readonly List<(RoomSpriteAuthoring sprite,RoomSpritePlacement placement)> windowPictures=new();
        private readonly List<List<int>> picturesByRoom=new();
        private readonly List<(Rect bounds,double opacity)> doors=new();   // blocking doors, level-local rectangles
        // Lamps of the level by world ID (<level>:<room x>,<room y>:<decor id>): their picture, lit frame and sprite.
        private readonly Dictionary<string,(RoomSpriteAuthoring picture,int frame,Sprite sprite,RoomDefinition definition)> lamps=new(StringComparer.Ordinal);
        private readonly HashSet<string> darkLamps=new(StringComparer.Ordinal);
        private readonly Dictionary<RoomSpriteAuthoring,string> lampByPicture=new();   // lamp ID of a picture (window rebuilds)
        private readonly HashSet<string> brokenLamps=new(StringComparer.Ordinal);
        private readonly List<LampState> lampPlacements=new();
        private readonly ActorShadowSources shadowSources=new();
        private readonly List<ActorShadowSource> windowShadowLamps=new();
        private readonly Dictionary<RoomSpriteAuthoring,string> windowSourceIds=new();
        private readonly HashSet<string> publishedShadowSourceIds=new(StringComparer.Ordinal);
        private float[,] shadowTerrain;
        private Vector2 shadowOrigin;
        private float shadowAmbient;
        private Vector4 ambientRadiance;
        public RoomLightingTable ShadowSettings=>table;
        public long ShadowSourceRevision {get;private set;}
        public float ActorShadowTransmission(Vector2 from,Vector2 to)=>IsReady?shadowSources.Visibility(from,to,doors):0;
        public void SelectActorShadowSources(Vector2 actor,List<ActorShadowSource> output)
        {
            if(output==null) throw new ArgumentNullException(nameof(output));
            if(!IsReady) {output.Clear();return;}
            shadowSources.Select(actor,darkLamps,doors,table.ActorShadowMaxSources,output);
        }
        // The same selection on a worker thread (11–12 ms per moving actor on the factory when run in the frame):
        // the window, dark lamps and doors are captured now; the result belongs to the current ShadowSourceRevision.
        public System.Threading.Tasks.Task<List<ActorShadowSource>> SelectActorShadowSourcesAsync(Vector2 actor)
        {
            var select=CaptureActorShadowSelector();
            return System.Threading.Tasks.Task.Run(()=>select(actor));
        }
        // One capture of the window, dark lamps and doors for any number of worker-thread selections (the inputs of
        // SelectActorShadowSources at the current ShadowSourceRevision); the capture itself is cheap.
        public Func<Vector2,List<ActorShadowSource>> CaptureActorShadowSelector()
        {
            if(!IsReady) return _=>new List<ActorShadowSource>();
            var window=shadowSources.Detached();
            var dark=new HashSet<string>(darkLamps,StringComparer.Ordinal);
            var blocking=doors.ToArray();int max=table.ActorShadowMaxSources;
            return actor=>
            {
                var output=new List<ActorShadowSource>(max);
                window.Select(actor,dark,blocking,max,output);
                return output;
            };
        }
        // Whether every source of a selection can cast its shadow now (its direct field exists; a source without a
        // registered emitter never gets one and does not hold the selection back).
        public bool ActorShadowFieldsReady(IReadOnlyList<ActorShadowSource> sources)
        {
            if(!IsReady) return false;
            for(int i=0;i<sources.Count;i++) if(!field.HasSteadyShadowFieldOrNone(sources[i].Id)) return false;
            return true;
        }
        public IReadOnlyCollection<string> LampIds=>lamps.Keys;
        public void PrepareActorShadowFields(IReadOnlyCollection<string> ids)
        {
            requestedShadowIds.Clear();foreach(string id in ids) requestedShadowIds.Add(id);
            if(!IsReady) return;
            // Nothing to build (only unused fields to release): immediate and cheap.
            if(field.HasRequiredSteadyFields(requestedShadowIds)) {StopShadowFieldWork();field.PrepareSteadyShadowFields(requestedShadowIds);return;}
            // Missing direct fields (a new window's lamps): one build stage per frame, never a whole cascade set in
            // one frame. Until a field lands, its lamp casts no actor shadow (TryGetSteadyShadowField is false).
            if(shadowFieldWork!=null && ReferenceEquals(shadowFieldOwner,field) && shadowFieldIds.SetEquals(requestedShadowIds)) return;
            StopShadowFieldWork();
            shadowFieldOwner=field;shadowFieldIds.Clear();shadowFieldIds.UnionWith(requestedShadowIds);
            shadowFieldSteps=field.PrepareSteadyShadowFieldsSteps(new List<string>(shadowFieldIds));
            shadowFieldWork=StartCoroutine(RunShadowFieldWork(shadowFieldOwner,shadowFieldSteps));
        }
        private Coroutine shadowFieldWork;
        private System.Collections.IEnumerator shadowFieldSteps;
        private RoomRadianceField shadowFieldOwner;
        private readonly HashSet<string> shadowFieldIds=new(StringComparer.Ordinal);
        private System.Collections.IEnumerator RunShadowFieldWork(RoomRadianceField owner,System.Collections.IEnumerator steps)
        {
            while(true)
            {
                if(!ReferenceEquals(owner,field) || !IsReady) break;   // the window changed: its field is no longer ours
                bool more;
                try {using(Remains.Diagnostics.FrameHitchLog.Section("Свет: поле прямого света для теней (по кадрам)")) more=steps.MoveNext();}
                catch(Exception error)
                {
                    Debug.LogError($"Поля прямого света для теней не построены: окно {windowRooms}, источников {shadowFieldIds.Count}. {error}",this);
                    break;
                }
                if(!more) break;
                yield return null;
            }
            (steps as IDisposable)?.Dispose();
            if(ReferenceEquals(shadowFieldSteps,steps)) {shadowFieldSteps=null;shadowFieldWork=null;shadowFieldOwner=null;}
        }
        // StopCoroutine does not dispose the nested iterator: release its unfinished texture explicitly.
        private void StopShadowFieldWork()
        {
            if(shadowFieldWork!=null) StopCoroutine(shadowFieldWork);
            var steps=shadowFieldSteps;shadowFieldWork=null;shadowFieldSteps=null;shadowFieldOwner=null;
            (steps as IDisposable)?.Dispose();
        }
        public IReadOnlyList<LampState> LampPlacements=>lampPlacements;
        public string LampWorldLevel=>root is HubTemplateLevel?"hub":root?.Level;
        // Revision of the last lamp snapshot in the light (-1: none yet).
        public long AppliedLampRevision {get;private set;}=-1;

        // A dark lamp stops emitting. Ordinary switching uses its original off frame;
        // only IDs explicitly present in broken use the authored broken artwork.
        public void SyncLamps(IReadOnlyCollection<string> dark,long revision,IReadOnlyCollection<string> broken=null)
        {
            using var frameSection=Remains.Diagnostics.FrameHitchLog.Section("Свет: лампы (SyncLamps)");
            if(root==null) throw new InvalidOperationException("Вид уровня не подготовлен.");
            if(dark==null) throw new ArgumentNullException(nameof(dark));
            if(revision<0) throw new ArgumentOutOfRangeException(nameof(revision),"Ревизия ламп отрицательная.");
            if(revision==AppliedLampRevision) return;
            broken??=Array.Empty<string>();
            foreach(string id in dark) if(!lamps.ContainsKey(id)) throw new ArgumentException($"Вид уровня {root.Level}: неизвестная лампа {id}.");
            var nextDark=new HashSet<string>(dark,StringComparer.Ordinal);
            foreach(string id in broken) if(!nextDark.Contains(id)) throw new ArgumentException($"Вид уровня {root.Level}: разбитая лампа {id} отсутствует в списке погашенных.");
            if(darkLamps.SetEquals(dark) && brokenLamps.SetEquals(broken))
            {
                AppliedLampRevision=revision;
                PresentationChanged?.Invoke(this);
                return;
            }
            darkLamps.Clear();foreach(string id in dark) darkLamps.Add(id);
            brokenLamps.Clear();foreach(string id in broken) brokenLamps.Add(id);
            foreach(var pair in lamps)
            {
                var (picture,frame,sprite,definition)=pair.Value;
                var renderer=picture.GetComponent<SpriteRenderer>();
                bool off=darkLamps.Contains(pair.Key);
                bool isBroken=brokenLamps.Contains(pair.Key);
                Sprite brokenSprite=null;
                bool hasBrokenArt=isBroken && brokenLampArt.TryGet(definition.Id,out brokenSprite);
                if(isBroken && !hasBrokenArt && warnedLampArt.Add(definition.Id))
                    Debug.LogWarning($"Лампа {pair.Key}, {definition.Id}: {(brokenLampArt.IsKnownOmission(definition.Id)?"разбитая графика не включена в авторский архив":"нет записи разбитой графики")}; используется исходная графика выключения, а при её отсутствии исходный корпус.",this);
                renderer.sprite=sprite;
                if(hasBrokenArt) renderer.sprite=brokenSprite;
                else if(off && definition.DecorFrameOff>0)
                {
                    string frameId=TileManifest.DecorFrameId(definition.Art,definition.DecorFrameOff);
                    if(root.Art.Manifest.Art.ContainsKey(frameId)) renderer.sprite=root.Art.Tile(frameId).sprite;
                    else Debug.LogWarning($"Лампа {pair.Key}, {definition.Id}: отсутствует кадр выключения {frameId}; свет погашен, исходный корпус сохранён до подготовки разбитой графики.",this);
                }
            }
            AppliedLampRevision=revision;
            if(field!=null)
            {
                var sprites=new List<RoomSpritePlacement>(windowPictures.Count);
                bool changed=false;
                foreach(var (picture,placement) in windowPictures)
                {
                    var sprite=picture.GetComponent<SpriteRenderer>().sprite;
                    bool isDark=IsDark(picture);
                    changed|=placement.Sprite!=sprite || placement.Dark!=isDark;
                    placement.Sprite=sprite;
                    placement.Dark=isDark;
                    sprites.Add(placement);
                }
                if(changed)
                {
                    field.UpdateSprites(sprites,WorldDoorOccluders());
                    SettleOverFrames();
                    RefreshShadowEmitters();
                }
            }
            PresentationChanged?.Invoke(this);
        }

        public RoomLayout Room {get;private set;}
        public Exception Failure {get;private set;}
        public bool IsUpdating=>false; // Explicit synchronous rebuilds publish before returning.
        public long AppliedOcclusionRevision=>AppliedDoorRevision;
        public event Action<IRoomLighting> PresentationChanged;
        public event Action<IRoomLighting,Exception> PresentationFailed;
        public bool IsReady=>root!=null && field!=null && Failure==null;
        public void Synchronize(RoomLayout room,WorldState world,long revision)
        {
            if(!ReferenceEquals(room,Room)) throw new InvalidOperationException("Свет завода ссылается на другую игровую геометрию.");
            if(world==null) throw new ArgumentNullException(nameof(world));
            if(revision==AppliedDoorRevision && world.lampRevision==AppliedLampRevision) return;
            try
            {
                SyncDoors(world.doors.Where(door=>door.level==Room.id).ToArray(),revision);
                var broken=world.lamps.Where(lamp=>lamp.level==LampWorldLevel && lamp.broken).Select(lamp=>lamp.id).ToArray();
                SyncLamps(broken,world.lampRevision,broken);
            }
            catch(Exception error) {ReportFailure(error);throw;}
        }
        public void ReportFailure(Exception error)
        {Failure=error;PresentationChanged?.Invoke(this);PresentationFailed?.Invoke(this,error);}
        public RectInt LightWindowRooms=>windowRooms;
        public void RenderMuzzleFlashes(IReadOnlyList<RoomRadianceField.SpriteEmission> emissions,IReadOnlyList<RoomRadianceField.LightOccluder> occluders=null)
        {
            if(!IsReady) return;
            try {field.RenderTransient(emissions,occluders);}
            catch(Exception error) {ReportFailure(error);throw;}
        }
        public void RenderMuzzleFlashes(IReadOnlyList<RoomRadianceField.SpriteEmission> emissions,IReadOnlyList<ProjectedLightShadow> masks,IReadOnlyList<string> sourceIds)
        {
            if(!IsReady) return;
            try {field.RenderTransient(emissions,masks,sourceIds);}
            catch(Exception error) {ReportFailure(error);throw;}
        }
        // Revision of the last door snapshot in the light (-1: none yet); the loader admits actors after it.
        public long AppliedDoorRevision {get;private set;}=-1;
        public int DoorOccluderTexels=>field!=null?field.OccluderTexels:0;
        public int LightFieldInstanceId=>field!=null?field.GetInstanceID():0;
        public int LightSilhouetteReadbacks=>field!=null?field.SilhouetteReadbacks:0;
        public int LightOcclusionUpdates=>field!=null?field.OcclusionUpdates:0;

        // Original Box.door_opac: 1 unless the object type sets opac (door1: 0.8). Definition opacity 0 = not set.
        public const double OriginalDoorOpacity=1;

        // Door states of this level (canonical DoorState: id, definition = original object id without
        // "remains:obj:", bounds in level coordinates). Closed, intact doors stop light by their type's
        // opacity; open or destroyed ones do not. Rebuilds the light window only for a new revision; the
        // set is kept across Focus.
        public void SyncDoors(IReadOnlyList<DoorState> states,long revision)
        {
            using var frameSection=Remains.Diagnostics.FrameHitchLog.Section("Свет: двери (SyncDoors)");
            if(root==null) throw new InvalidOperationException("Вид завода не подготовлен.");
            if(states==null) throw new ArgumentNullException(nameof(states));
            if(revision<0) throw new ArgumentOutOfRangeException(nameof(revision),"Ревизия дверей отрицательная.");
            if(revision==AppliedDoorRevision) return;
            var next=new List<(Rect,double)>();
            foreach(var door in states)
            {
                if(door==null) throw new ArgumentException("Вид завода: пустое состояние двери.");
                door.Validate();
                if(door.level!=root.Level) throw new ArgumentException($"Вид уровня {root.Level}: дверь {door.id} другого уровня {door.level}.");
                var definition=root.Definitions.Require("remains:obj:"+door.definition,$"вид завода, дверь {door.id}");
                if(definition.Kind!=RoomDefinitionKind.Object || definition.ObjectType!="door")
                    throw new ArgumentException($"Вид завода: дверь {door.id}, определение {definition.Id} — не дверь ({definition.Kind}, {definition.ObjectType}).");
                if(door.Blocks) next.Add((door.Bounds,definition.Opacity>0?definition.Opacity:OriginalDoorOpacity));
            }
            doors.Clear();doors.AddRange(next);
            if(field!=null)
            {
                field.UpdateOccluders(WorldDoorOccluders());
                SettleOverFrames();
            }
            AppliedDoorRevision=revision;
            ShadowSourceRevision++;
            using(Remains.Diagnostics.FrameHitchLog.Section("Свет: событие PresentationChanged")) PresentationChanged?.Invoke(this);
        }

        private void SettleOverFrames()
        {
            field.Render();
            pendingSettle=SettleUpdates-1;
            if(!Application.isPlaying || !isActiveAndEnabled)
            {
                // Editor tools and checks have no frames to spread over: the whole settle at once, as before.
                for(;pendingSettle>0;pendingSettle--) field.Render();
                while(field.RebuildOneStaleSteadyShadowField()) {}
                return;
            }
            if(settling==null) settling=StartCoroutine(Settle());
        }
        private System.Collections.IEnumerator Settle()
        {
            while(true)
            {
                yield return null;
                if(field==null) break;
                if(pendingSettle>0)
                {
                    pendingSettle--;
                    // The field textures update in place; listeners already reacted to the event itself.
                    using(Remains.Diagnostics.FrameHitchLog.Section("Свет: досчёт после события")) field.Render();
                }
                else if(!field.RebuildOneStaleSteadyShadowField()) break;
            }
            settling=null;
        }

        public void Initialize(FactoryLevelRoot level,RoomLayout geometry=null)
        {
            if(level==null) throw new ArgumentNullException(nameof(level));
            if(level.gameObject!=gameObject) throw new InvalidOperationException("Вид завода должен лежать на объекте корня завода.");
            Initialize(new FactoryTemplateLevel(level),geometry);
        }

        public void Initialize(ITemplateLevel level,RoomLayout geometry=null)
        {
            if(level==null) throw new ArgumentNullException(nameof(level));
            if(root!=null) throw new InvalidOperationException("Вид уровня уже подготовлен: повторная подготовка запрещена.");
            brokenLampArt=GameAssets.Load<BrokenLampArt>(BrokenLampArt.ResourcePath)??throw new InvalidOperationException("Нет каталога разбитой графики ламп: "+BrokenLampArt.ResourcePath);
            brokenLampArt.Validate();
            Room=geometry??level.Physics;
            if(Room.id!=level.Level) throw new InvalidOperationException("Вид уровня: игровая геометрия другого уровня: "+Room.id);
            if(level.RoomInstances.childCount!=level.Rooms.Count)
                throw new InvalidOperationException($"Вид уровня {level.Level}: установлено комнат {level.RoomInstances.childCount}, в раскладке {level.Rooms.Count} (сначала установка комнат).");
            table=RoomLightingTable.Read(level.Lighting.text,level.Lighting.name);
            var lands=level.Lands;var definitions=level.Definitions;
            roomsWide=roomsDeep=0;
            foreach(var room in level.Rooms) {roomsWide=Math.Max(roomsWide,room.X+1);roomsDeep=Math.Max(roomsDeep,room.Y+1);}

            // Walls and decor of all rooms sort together: one group on the level root, none on the terrain.
            if(!TryGetComponent(out sorting)) sorting=gameObject.AddComponent<SortingGroup>();   // not ?? : Unity null
            sorting.sortingOrder=WorldVisualOrderPolicy.SceneryRoomOrder;
            var backdrops=new List<(RoomFile,Vector2)>();
            foreach(var room in level.Rooms) backdrops.Add((room.Template,room.Offset));
            terrain=RoomTerrainView.Build(level.Grid,definitions,lands,level.Art,Vector2.zero,table.ShadowEdge,transform,backdrops,ownSortingGroup:false);

            // Ambient: one level for the whole level (per-room darkness and tint are not applied yet).
            var scene=RoomLightScene.Build(level.Grid,definitions,lands,table,table.RadiancePower,table.RadianceLampSize);
            foreach(string issue in scene.Issues) Debug.LogWarning("Вид уровня: "+issue,this);
            // From an authored prefab: Light2D fills its target sorting layers only in the Editor, so one added by
            // code in a player build has none (FactoryAmbientLightAuthoring).
            var light=Instantiate(GameAssets.Load<Light2D>(AmbientResource)??throw new InvalidOperationException("Нет префаба общего света завода: Resources/"+AmbientResource),transform,false);
            ambient=light.gameObject;
            if(light.lightType!=Light2D.LightType.Global) throw new InvalidOperationException("Префаб общего света завода: свет не общий (Global).");
            light.intensity=(float)table.Ambient(scene.Darkness);
            light.color=new Color((float)scene.Tint[0],(float)scene.Tint[1],(float)scene.Tint[2]);
            shadowAmbient=light.intensity*(.2126f*light.color.r+.7152f*light.color.g+.0722f*light.color.b);
            // The same ambient for WorldLit artwork (actors, props, doors), which does not read Light2D.
            ambientRadiance=(Vector4)(light.color*light.intensity);

            pictures.Clear();lamps.Clear();lampByPicture.Clear();darkLamps.Clear();brokenLamps.Clear();lampPlacements.Clear();windowSourceIds.Clear();
            foreach(var sprite in level.RoomInstances.GetComponentsInChildren<RoomSpriteAuthoring>(true))
                pictures.Add((sprite,definitions.Require(sprite.Definition,"вид уровня, "+sprite.name)));
            // Pictures by room: a window change looks only at its rooms' pictures (the others are inactive anyway).
            picturesByRoom.Clear();for(int i=0;i<level.Rooms.Count;i++) picturesByRoom.Add(new List<int>());
            for(int p=0;p<pictures.Count;p++)
            {
                var node=pictures[p].sprite.transform;
                while(node!=null && node.parent!=level.RoomInstances) node=node.parent;
                if(node==null) throw new InvalidOperationException($"Вид уровня {level.Level}: картинка {pictures[p].sprite.name} вне комнат.");
                picturesByRoom[node.GetSiblingIndex()].Add(p);
            }
            for(int i=0;i<level.Rooms.Count;i++)
            {
                var room=level.Rooms[i];
                var templateInstance=level.RoomInstances.GetChild(i).GetComponent<RoomTemplateInstance>()??
                    throw new InvalidOperationException($"Источники тени: комната {i}, {room.Template.Template} без RoomTemplateInstance.");
                foreach(var picture in level.RoomInstances.GetChild(i).GetComponentsInChildren<RoomSpriteAuthoring>(true))
                {
                    if(picture.Erase!=null)
                    {
                        string decorId=picture.Kind==RoomSpriteKind.Decor?room.Template.Decor[picture.Index].Id:$"{picture.Kind}:{picture.Index}";
                        if(string.IsNullOrEmpty(decorId)) throw new InvalidOperationException($"Вид уровня {level.Level}: окно без ID, комната {room.X},{room.Y}, картинка {picture.Index}.");
                        windowSourceIds.Add(picture,$"window:{level.Level}:{room.X},{room.Y}:{decorId}");
                    }
                    if(picture.Kind!=RoomSpriteKind.Decor || !table.Lamps.ContainsKey(picture.Definition)) continue;
                    var decor=room.Template.Decor[picture.Index];
                    if(decor.Id==null) throw new InvalidOperationException($"Вид уровня {level.Level}: лампа без ID, шаблон {room.Template.Template}, декор {picture.Index}.");
                    string id=$"{level.Level}:{room.X},{room.Y}:{decor.Id}";
                    var renderer=picture.GetComponent<SpriteRenderer>();
                    lamps.Add(id,(picture,picture.Frame,renderer.sprite,definitions.Require(picture.Definition,"лампа")));
                    lampByPicture.Add(picture,id);
                }
            }
            lampPlacements.AddRange(LampContent.Read(level));
            root=level;
            PresentationChanged?.Invoke(this);
            Debug.Log($"Вид уровня {level.Level}: сетка {level.Grid.Width}×{level.Grid.Height}, комнат {level.Rooms.Count}, картинок {pictures.Count}.",this);
        }

        // Moves the light window to the rooms around a world point; rebuilds only when the window changes.
        public void Focus(Vector2 world)
        {
            var rooms=WindowFor(world);
            if(rooms.Equals(windowRooms) && field!=null) return;
            RefreshLight(rooms);
        }
        // Lightweight camera observation; no allocation, scene lookup or rebuild here.
        public RectInt WindowFor(Vector2 world)
        {
            if(root==null) throw new InvalidOperationException("Вид уровня не подготовлен.");
            var grid=root.Grid;
            float cell=(float)grid.Cell;
            Vector2 local=transform.InverseTransformPoint(world);
            int cx=0,cy=0;float best=float.MaxValue;
            var rooms=root.Rooms;
            for(int i=0;i<rooms.Count;i++)
            {
                var r=rooms[i];
                float w=r.Cells.width*cell,h=r.Cells.height*cell;
                float dx=Mathf.Max(0,Mathf.Max(r.Offset.x-local.x,local.x-(r.Offset.x+w))),dy=Mathf.Max(0,Mathf.Max(r.Offset.y-local.y,local.y-(r.Offset.y+h)));
                float d=dx*dx+dy*dy;
                if(d<best) {best=d;cx=r.X;cy=r.Y;if(d==0) break;}
            }
            return WindowAround(cx,cy);
        }
        private RectInt WindowAround(int cx,int cy)
        {
            int ww=Math.Min(WindowRooms,roomsWide),wh=Math.Min(WindowRooms,roomsDeep);
            return new RectInt(Mathf.Clamp(cx-WindowRooms/2,0,roomsWide-ww),Mathf.Clamp(cy-WindowRooms/2,0,roomsDeep-wh),ww,wh);
        }

        // Rebuilds the erase mask and radiance field after the focus window changes.
        // Door and lamp snapshots update the existing field instead.
        public void RefreshLight(RectInt? requestedRooms=null)
        {
            var rooms=requestedRooms??windowRooms;
            if(root==null || rooms.width<=0) throw new InvalidOperationException("Вид уровня: окно света не выбрано.");
            CancelWindowPreparation();
            // Existing admission/reference callers synchronously drain the same exact stage sequence.
            var context=new LightWindowContext(rooms,AppliedDoorRevision,AppliedLampRevision) {Synchronous=true};
            var steps=BuildWindowSteps(context);
            try {while(steps.MoveNext()) {} CommitWindow(context);}
            catch {if(!ReferenceEquals(activeWindow,context)) context.Dispose();throw;}
            finally {(steps as IDisposable)?.Dispose();}
        }

        // A failed candidate (budget, resource creation) never takes down the active window: the level keeps its
        // light, the failure is reported with its stack and that window is not retried for a while.
        private const float FailedCandidateRetrySeconds=5;
        private RectInt failedCandidate=new(-1,-1,0,0);
        private float failedCandidateUntil;
        private void CandidateFailed(RectInt rooms,Exception failure)
        {
            failedCandidate=rooms;failedCandidateUntil=Time.unscaledTime+FailedCandidateRetrySeconds;
            Debug.LogWarning($"Окно света {rooms}: подготовка не удалась, активное окно {windowRooms} сохранено, повтор через {FailedCandidateRetrySeconds} с. {failure}",this);
        }

        public void RequestLightWindow(Vector2 focus,bool commit,RectInt? predictedRooms=null)
        {
            if(!IsReady) return;
            var rooms=predictedRooms??WindowFor(focus);
            if(rooms.width!=windowRooms.width || rooms.height!=windowRooms.height || rooms.x<0 || rooms.y<0 || rooms.xMax>roomsWide || rooms.yMax>roomsDeep)
                throw new ArgumentException("Окно света: прогноз вне границ уровня.");
            if(rooms==windowRooms) {if(commit) CancelWindowPreparation();return;}
            if(rooms==failedCandidate && Time.unscaledTime<failedCandidateUntil) return;
            requestedWindow=rooms;requestedFocus=focus;requestedCommit=commit;windowRequested=true;
            // A door/lamp revision does not discard the candidate; its commit catches up (CommitWindow).
            if(candidateWindow!=null && candidateWindow.Rooms!=rooms) CancelWindowPreparation();
            // Cancel clears old intent as well; restore the new request.
            requestedWindow=rooms;requestedFocus=focus;requestedCommit=commit;windowRequested=true;
            if(preparingWindow==null)
            {
                preparationWork=PrepareRequestedWindow();
                preparingWindow=StartCoroutine(preparationWork);
            }
        }
        // Returning into the active overlap withdraws publication intent without discarding useful preparation.
        public void DeferLightWindowSwitch()=>requestedCommit=false;
        public void CancelWindowPreparation()
        {
            if(preparingWindow!=null) StopCoroutine(preparingWindow);
            preparingWindow=null;windowRequested=false;
            var work=preparationWork;preparationWork=null;
            // Do not rely on StopCoroutine disposing nested manually-driven iterators/command buffers.
            try {(work as IDisposable)?.Dispose();}
            finally {candidateWindow?.Dispose();candidateWindow=null;}
        }
        private System.Collections.IEnumerator PrepareRequestedWindow()
        {
            // Ensure StartCoroutine returns before completion/error can clear its handle.
            yield return null;
            while(windowRequested && root!=null)
            {
                var context=new LightWindowContext(requestedWindow,AppliedDoorRevision,AppliedLampRevision);
                candidateWindow=context;
                var steps=BuildWindowSteps(context);Exception failure=null;
                try
                {
                    int submitted=0;
                    while(true)
                    {
                        bool more=false;
                        try {more=steps.MoveNext();} catch(Exception error) {failure=error;}
                        if(failure!=null || !more) break;
                        if(++submitted>=PreparationStagesPerFrame) {submitted=0;yield return null;}
                    }
                }
                finally {(steps as IDisposable)?.Dispose();}
                if(failure!=null)
                {
                    context.Dispose();candidateWindow=null;preparingWindow=null;preparationWork=null;windowRequested=false;
                    CandidateFailed(context.Rooms,failure);yield break;
                }
                if(!context.Ready) {context.Dispose();candidateWindow=null;continue;}
                bool committed=false;
                while(windowRequested)
                {
                    // Door and lamp events during preparation do not discard it: the committed window catches up with
                    // them (CommitWindow). Other picture changes (visibility, placement) still start over.
                    if(!CanCatchUp(context)) break;
                    if(!requestedCommit) {yield return null;continue;}
                    // Selection may have advanced while a ready neighbor waited at a boundary.
                    var extra=context.Field.PrepareRegisteredShadowFieldsSteps(requestedShadowIds,retainPrepared:true);
                    Exception extraFailure=null;
                    try
                    {
                        while(true)
                        {
                            bool more=false;
                            try {more=extra.MoveNext();} catch(Exception error) {extraFailure=error;}
                            if(extraFailure!=null || !more) break;
                            yield return null;
                        }
                    }
                    finally {(extra as IDisposable)?.Dispose();}
                    if(extraFailure!=null)
                    {
                        context.Dispose();candidateWindow=null;preparingWindow=null;preparationWork=null;windowRequested=false;
                        CandidateFailed(context.Rooms,extraFailure);yield break;
                    }
                    if(!CanCatchUp(context)) break;
                    if(!requestedCommit || !context.Field.HasRequiredSteadyFields(requestedShadowIds)) continue;
                    Exception commitFailure=null;
                    try {CommitWindow(context);committed=true;} catch(Exception error) {commitFailure=error;}
                    if(commitFailure!=null)
                    {
                        if(!ReferenceEquals(activeWindow,context)) context.Dispose();
                        candidateWindow=null;preparingWindow=null;preparationWork=null;windowRequested=false;
                        ReportFailure(commitFailure);yield break;
                    }
                    break;
                }
                if(!committed)
                {
                    context.Dispose();candidateWindow=null;
                    if(windowRequested) continue;
                }
                candidateWindow=null;windowRequested=false;
            }
            preparingWindow=null;preparationWork=null;
        }

        private System.Collections.IEnumerator BuildWindowSteps(LightWindowContext context)
        {
            var rooms=context.Rooms;var level=root.Grid;float cell=(float)level.Cell;
            // Cancelled CPU tasks retain a bounded slot until complete; do not spawn an unbounded worker backlog.
            while(!preparedWindows.ContainsKey(rooms) && preparedWindows.Count>=PreparedWindowsKept)
            {
                foreach(var key in new List<RectInt>(preparedWindows.Keys))
                    if(key!=windowRooms && key!=rooms && preparedWindows[key].IsCompleted)
                    {
                        if(preparedWindows[key].IsFaulted) Debug.LogWarning($"Предварительная подготовка света {key}: {preparedWindows[key].Exception}.",this);
                        preparedWindows.Remove(key);
                    }
                if(preparedWindows.Count>=PreparedWindowsKept) yield return null;
            }
            if(!preparedWindows.TryGetValue(rooms,out var task)) {task=StartPreparation(rooms);preparedWindows.Add(rooms,task);}
            if(context.Synchronous) task.GetAwaiter().GetResult();
            while(!task.IsCompleted) yield return null;
            var data=task.GetAwaiter().GetResult();
            int column=int.MaxValue,row=int.MaxValue,right=0,bottom=0;
            foreach(var r in root.Rooms) if(Inside(r,rooms))
            {column=Math.Min(column,r.Cells.x);row=Math.Min(row,r.Cells.y);right=Math.Max(right,r.Cells.xMax);bottom=Math.Max(bottom,r.Cells.yMax);}
            int width=right-column,height=bottom-row;
            Vector2 localOrigin=new Vector2(column*cell,(level.Height-row-height)*cell);
            Vector2 origin=transform.TransformPoint(localOrigin);var size=new Vector2(width*cell,height*cell);var bounds=new Rect(origin,size);
            context.Origin=localOrigin;context.Terrain=data.Shadow;
            context.Root=new GameObject($"Подготовка света {rooms.x},{rooms.y}");context.Root.transform.SetParent(transform,false);
            var sprites=new List<RoomSpritePlacement>();var erases=new List<(Sprite,Matrix4x4)>();var indices=new List<int>();
            for(int i=0;i<root.Rooms.Count;i++) if(Inside(root.Rooms[i],rooms)) indices.AddRange(picturesByRoom[i]);
            indices.Sort();
            foreach(int index in indices)
            {
                var (authored,definition)=pictures[index];
                if(authored==null) throw new InvalidOperationException("Окно света: удалён авторский источник картинки "+index);
                bool shown=ShownForPreparation(authored);var renderer=authored.GetComponent<SpriteRenderer>();
                var matrix=authored.transform.localToWorldMatrix;
                context.Signatures.Add((authored,shown,renderer!=null?renderer.sprite:null,authored.Erase,matrix,authored.Frame,IsDark(authored)));
                if(!shown) continue;
                var local=renderer.sprite.bounds;Vector2 a=matrix.MultiplyPoint3x4(local.min),b=matrix.MultiplyPoint3x4(local.max);
                if(!bounds.Overlaps(Rect.MinMaxRect(Mathf.Min(a.x,b.x),Mathf.Min(a.y,b.y),Mathf.Max(a.x,b.x),Mathf.Max(a.y,b.y)))) continue;
                var placement=new RoomSpritePlacement {Definition=definition,Sprite=renderer.sprite,Frame=authored.Frame,LocalToWorld=matrix,Erase=authored.Erase,Dark=IsDark(authored)};
                sprites.Add(placement);context.Pictures.Add((authored,placement));if(authored.Erase!=null) erases.Add((authored.Erase,matrix));
            }
            yield return null;
            if(erases.Count>0)
            {
                context.Erase=context.Root.AddComponent<RoomDecorResources>();context.Erase.BindLightContext(context.Shader);
                context.Erase.BuildErase(erases,origin,size,GameAssets.Load<Shader>(RoomDecorView.EraseShaderResource)??throw new InvalidOperationException("Нет шейдера "+RoomDecorView.EraseShaderResource),publish:false);
            }
            yield return null;
            var initialize=RoomRadianceField.BuildSteps(data.Scene,root.Lands,origin,context.Root.transform,value=>context.Field=value,
                sprites,WorldDoorOccluders(),context.Shader);
            try {while(initialize.MoveNext()) yield return null;} finally {(initialize as IDisposable)?.Dispose();}
            var render=context.Field.PrepareWindowSteps(SettleUpdates);
            try {while(render.MoveNext()) yield return null;} finally {(render as IDisposable)?.Dispose();}
            var sources=BuildShadowEmitterSteps(context.Field,context.Pictures,context.Sources);
            try {while(sources.MoveNext()) yield return null;} finally {(sources as IDisposable)?.Dispose();}
            // Prewarm current requested masks plus the sources predicted at the candidate focus.
            var selector=new ActorShadowSources();selector.Configure(context.Terrain,context.Origin,1f/table.RadianceResolution,transform.worldToLocalMatrix,context.Sources,shadowAmbient);
            var nearby=new List<ActorShadowSource>();selector.Select(requestedFocus,darkLamps,doors,table.ActorShadowMaxSources,nearby);
            var ids=new HashSet<string>(requestedShadowIds,StringComparer.Ordinal);
            foreach(var source in nearby) ids.Add(source.Id);
            var direct=context.Field.PrepareRegisteredShadowFieldsSteps(ids);
            try {while(direct.MoveNext()) yield return null;} finally {(direct as IDisposable)?.Dispose();}
            context.Ready=true;
        }
        // Only door and lamp state differs from the preparation (revisions, lamp art and darkness); visibility,
        // placement, frame and erase of every picture are the same.
        private bool CanCatchUp(LightWindowContext context)
        {
            foreach(var saved in context.Signatures)
            {
                var source=saved.source;
                if(source==null || ShownForPreparation(source)!=saved.shown || source.Erase!=saved.erase || source.Frame!=saved.frame ||
                    source.transform.localToWorldMatrix!=saved.matrix) return false;
            }
            return true;
        }
        // A window committed after door/lamp events it was not prepared with gets them through the active event path
        // (occluders, lamp pictures, settle over frames, stale shadow fields), as if they arrived after the switch.
        private void CatchUpWindow(LightWindowContext context)
        {
            using var frameSection=Remains.Diagnostics.FrameHitchLog.Section("Окно света: события во время подготовки");
            var sprites=new List<RoomSpritePlacement>(windowPictures.Count);bool changed=false;
            foreach(var (picture,placement) in windowPictures)
            {
                var sprite=picture.GetComponent<SpriteRenderer>().sprite;bool isDark=IsDark(picture);
                changed|=placement.Sprite!=sprite || placement.Dark!=isDark;
                placement.Sprite=sprite;placement.Dark=isDark;sprites.Add(placement);
            }
            if(changed) field.UpdateSprites(sprites,WorldDoorOccluders());
            else field.UpdateOccluders(WorldDoorOccluders());
            SettleOverFrames();
            if(changed) RefreshShadowEmitters();
            context.Revise(AppliedDoorRevision,AppliedLampRevision);
        }
        private bool ShownForPreparation(RoomSpriteAuthoring authored)
        {
            if(!authored.enabled) return false;
            var renderer=authored.GetComponent<SpriteRenderer>();if(renderer==null || !renderer.enabled) return false;
            // Room roots are intentionally inactive outside the active window. Respect all lower adapter flags.
            for(var node=authored.transform;node!=null && node!=transform && node.parent!=root.RoomInstances;node=node.parent)
                if(!node.gameObject.activeSelf) return false;
            return true;
        }
        private bool PictureStateMatches(LightWindowContext context)
        {
            // Adapter visibility and transform changes do not necessarily increment the lamp revision.
            foreach(var saved in context.Signatures)
            {
                var source=saved.source;
                if(source==null || ShownForPreparation(source)!=saved.shown || source.Erase!=saved.erase || source.Frame!=saved.frame ||
                    source.transform.localToWorldMatrix!=saved.matrix || IsDark(source)!=saved.dark) return false;
                var renderer=source.GetComponent<SpriteRenderer>();
                if((renderer!=null?renderer.sprite:null)!=saved.image) return false;
            }
            return true;
        }
        private void CommitWindow(LightWindowContext context)
        {
            if(context==null || !context.Ready || context.Root==null || context.Field==null || !CanCatchUp(context))
                throw new InvalidOperationException("Окно света: публикация неподготовленной или устаревшей ревизии.");
            bool outdated=!context.Matches(AppliedDoorRevision,AppliedLampRevision) || !PictureStateMatches(context);
            if(root.RoomInstances.childCount!=root.Rooms.Count)
                throw new InvalidOperationException("Окно света: состав комнат изменился до публикации.");
            var previous=activeWindow;
            try {PublishWindow(context);}
            catch
            {
                // No old resources have been released. Restore all observable references and shader ownership.
                if(previous!=null) PublishWindow(previous);
                else {context.Shader.Release();activeWindow=null;window=null;field=null;windowRooms=new RectInt(-1,-1,0,0);}
                throw;
            }
            pendingSettle=0;ShadowSourceRevision++;
            if(ReferenceEquals(candidateWindow,context)) candidateWindow=null;
            // Publication is now committed. Cleanup/subscriber exceptions must not dispose the new active context.
            previous?.Dispose();
            if(outdated) CatchUpWindow(context);
            PrefetchNeighbours();PresentationChanged?.Invoke(this);
        }
        private void PublishWindow(LightWindowContext context)
        {
            activeWindow=context;window=context.Root;field=context.Field;windowRooms=context.Rooms;
            windowPictures.Clear();windowPictures.AddRange(context.Pictures);
            shadowTerrain=context.Terrain;shadowOrigin=context.Origin;
            windowShadowLamps.Clear();windowShadowLamps.AddRange(context.Sources);
            shadowSources.Configure(shadowTerrain,shadowOrigin,1f/table.RadianceResolution,transform.worldToLocalMatrix,windowShadowLamps,shadowAmbient);
            foreach(var source in context.Sources) publishedShadowSourceIds.Add(source.Id);
            // No yield or event between rooms, selector and all shader parameters.
            context.Shader.Activate();field.PublishRadiance();
            if(context.Erase!=null) context.Erase.PublishErase();
            context.Shader.Publish(()=>
            {
                if(context.Erase==null) Shader.SetGlobalFloat("_RemainsEraseEnabled",0);
                Shader.SetGlobalFloat("_RemainsBackgroundRadianceEnabled",0);
                Shader.SetGlobalVector("_RemainsAmbient",ambientRadiance);
            });
            ShowWindowRooms();
        }

        // CPU data of a light window, prepared on a worker thread: the next window is usually ready when the camera
        // crosses into it (the ~12 ms of scene, wall and shadow absorption no longer run in that frame).
        private sealed class PreparedWindow
        {
            public RoomFile Region;public RoomRadianceField.PreparedScene Scene;public float[,] Shadow;
        }
        private const int PreparedWindowsKept=6;
        private readonly Dictionary<RectInt,System.Threading.Tasks.Task<PreparedWindow>> preparedWindows=new();
        private System.Threading.Tasks.Task<PreparedWindow> StartPreparation(RectInt rooms)
        {
            // Cells of the window's rooms (read here, on the game thread); the rest is pure data.
            int column=int.MaxValue,row=int.MaxValue,right=0,bottom=0;
            foreach(var r in root.Rooms)
                if(Inside(r,rooms)) {column=Math.Min(column,r.Cells.x);row=Math.Min(row,r.Cells.y);right=Math.Max(right,r.Cells.xMax);bottom=Math.Max(bottom,r.Cells.yMax);}
            var level=root.Grid;var definitions=root.Definitions;var lands=root.Lands;var lighting=table;
            return System.Threading.Tasks.Task.Run(()=>
            {
                var region=RoomFileRegion.Crop(level,column,row,right-column,bottom-row);
                var geometry=RoomGeometryBuilder.Build(region,definitions);
                return new PreparedWindow
                {
                    Region=region,Scene=RoomRadianceField.PreparedScene.Create(region,definitions,lands,lighting,geometry),
                    Shadow=RoomLightScene.Absorption(region,geometry,lighting.RadianceResolution,lighting.BeamTransmittance,.985,lighting.BeamHeight)
                };
            });
        }
        private PreparedWindow TakePrepared(RectInt rooms)
        {
            if(!preparedWindows.TryGetValue(rooms,out var task)) {task=StartPreparation(rooms);preparedWindows.Add(rooms,task);}
            return task.GetAwaiter().GetResult();   // a failed preparation throws here, as the synchronous build did
        }
        // The windows centred on the four rooms next to the current centre; others are dropped.
        private void PrefetchNeighbours()
        {
            // GPU preparation has one candidate. Keep CPU work bounded too; no four-neighbour task fan-out.
            foreach(var key in new List<RectInt>(preparedWindows.Keys))
                if(key!=windowRooms && preparedWindows[key].IsCompleted)
                {
                    if(preparedWindows[key].IsFaulted) Debug.LogWarning($"Предварительная подготовка света {key}: {preparedWindows[key].Exception}.",this);
                    preparedWindows.Remove(key);
                }
        }

        private void RefreshShadowEmitters()
        {
            var steps=BuildShadowEmitterSteps(field,windowPictures,windowShadowLamps);
            try {while(steps.MoveNext()) {}} finally {(steps as IDisposable)?.Dispose();}
            foreach(var source in windowShadowLamps) publishedShadowSourceIds.Add(source.Id);
            if(activeWindow!=null && activeWindow.Field==field) {activeWindow.Sources.Clear();activeWindow.Sources.AddRange(windowShadowLamps);}
            shadowSources.Configure(shadowTerrain,shadowOrigin,1f/table.RadianceResolution,transform.worldToLocalMatrix,windowShadowLamps,shadowAmbient);
            ShadowSourceRevision++;
        }
        private System.Collections.IEnumerator BuildShadowEmitterSteps(RoomRadianceField target,
            IReadOnlyList<(RoomSpriteAuthoring sprite,RoomSpritePlacement placement)> placements,List<ActorShadowSource> output)
        {
            output.Clear();var emitters=new List<SteadyLightEmitter>();
            foreach(var (picture,placement) in placements)
            {
                bool hasSource=false;
                if(windowSourceIds.TryGetValue(picture,out string windowId)) {AddEmitter(windowId,placement,true);hasSource=true;}
                if(lampByPicture.TryGetValue(picture,out string lampId)) {AddEmitter(lampId,placement,false);hasSource=true;}
                if(hasSource) yield return null;
            }
            target.RegisterSteadyEmitters(emitters,publishedShadowSourceIds);
            void AddEmitter(string id,RoomSpritePlacement placement,bool isWindow)
            {
                if(!target.TryCreateSteadyEmitter(id,placement,isWindow,out var emitter)) return;
                var samples=target.PrepareLampShadowSamples(emitter.Sprite,emitter.LocalToWorld);
                if(samples.Length==0)
                {Debug.LogWarning($"Источник тени {id}: спрайт {emitter.Sprite.name} не излучает ни одного текселя; источник пропущен.",this);return;}
                emitters.Add(emitter);Vector2 center=Vector2.zero;float weight=0;
                foreach(var sample in samples) {center+=new Vector2(sample.x,sample.y)*sample.z;weight+=sample.z;}
                center/=weight;float radius=0;
                foreach(var sample in samples) radius=Mathf.Max(radius,Vector2.Distance(center,new Vector2(sample.x,sample.y)));
                float luminance=.2126f*emitter.Radiance.r+.7152f*emitter.Radiance.g+.0722f*emitter.Radiance.b;
                output.Add(new ActorShadowSource(id,center,Mathf.Max(.05f,radius),luminance*target.SteadyEmitterWidth(emitter),emissionPoints:samples));
            }
        }

        private bool IsDark(RoomSpriteAuthoring picture)
        {
            if(darkLamps.Count==0) return false;
            return lampByPicture.TryGetValue(picture,out string id) && darkLamps.Contains(id);
        }

        private List<(Rect,double)> WorldDoorOccluders()
        {
            var result=new List<(Rect,double)>(doors.Count);
            foreach(var (local,opacity) in doors)
                result.Add((new Rect((Vector2)transform.TransformPoint(local.position),local.size),opacity));
            return result;
        }

        // Only the rooms of the light window keep their pictures active: all 84 factory rooms are ~5600 sprites, more
        // than a SortingGroup handles (4095) and a large per-frame cost. Room instances are RoomInstances children in
        // Rooms order. Walls (terrain tilemaps) stay level-wide.
        private void ShowWindowRooms()
        {
            var instances=root.RoomInstances;var rooms=root.Rooms;
            if(instances.childCount!=rooms.Count) throw new InvalidOperationException($"Вид уровня: комнат {instances.childCount}, в раскладке {rooms.Count}.");
            for(int i=0;i<rooms.Count;i++)
            {
                bool inside=InWindow(rooms[i]);
                var instance=instances.GetChild(i).gameObject;
                if(instance.activeSelf!=inside) instance.SetActive(inside);
            }
        }

        private bool InWindow(TemplateLevelRoom r)=>Inside(r,windowRooms);
        private static bool Inside(TemplateLevelRoom r,RectInt rooms)=>r.X>=rooms.xMin && r.X<rooms.xMax && r.Y>=rooms.yMin && r.Y<rooms.yMax;

        // Shown in the room, judged up to the level root: a prepared source root may itself be inactive.
        private bool Shown(RoomSpriteAuthoring authored)
        {
            if(!authored.enabled) return false;
            var renderer=authored.GetComponent<SpriteRenderer>();
            if(renderer==null || !renderer.enabled) return false;
            for(var t=authored.transform;t!=null && t!=transform;t=t.parent) if(!t.gameObject.activeSelf) return false;
            return true;
        }

        public void Release()
        {
            StopShadowFieldWork();
            CancelWindowPreparation();
            DestroyWindow();
            if(terrain!=null) Destroy(terrain);
            if(ambient!=null) {ambient.SetActive(false);Destroy(ambient);}
            terrain=ambient=null;root=null;pictures.Clear();windowPictures.Clear();doors.Clear();lamps.Clear();lampByPicture.Clear();darkLamps.Clear();brokenLamps.Clear();lampPlacements.Clear();AppliedDoorRevision=-1;AppliedLampRevision=-1;windowRooms=new RectInt(-1,-1,0,0);preparedWindows.Clear();
            shadowSources.Clear();windowShadowLamps.Clear();windowSourceIds.Clear();publishedShadowSourceIds.Clear();shadowTerrain=null;ShadowSourceRevision++;
            Room=null;PresentationChanged?.Invoke(this);
        }

        private void DestroyWindow()
        {
            activeWindow?.Dispose();activeWindow=null;
            window=null;field=null;
        }

        private void OnDestroy()=>Release();

        private new static void Destroy(UnityEngine.Object o) {if(Application.isPlaying) UnityEngine.Object.Destroy(o);else DestroyImmediate(o);}
    }
}
