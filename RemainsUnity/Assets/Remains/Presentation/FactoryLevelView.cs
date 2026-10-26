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
    // composed grid with backdrops per room, ambient light, the light of the whole level (LevelLight: direct light
    // per source on the full grid, bounce on a half grid) and, inside a window of rooms around the camera, the room
    // pictures and the background erase mask (windows). Moving the camera never recomputes light: doors and lamps
    // change it through events, spread over frames.
    // Lifetime: Initialize after FactoryLevelRoot.InstantiateRooms, Release before the root is destroyed.
    public sealed class FactoryLevelView : MonoBehaviour,IRoomLighting
    {
        public const int WindowRooms=3;            // rooms across and down in the picture window (odd)
        public const string AmbientResource="Presentation/FactoryAmbientLight";
        // Event work of the level light per frame: field builds (~1 ms of video card each) and bounce stages.
        private const int FieldBuildsPerFrame=2,BounceStagesPerFrame=1;
        // The camera view assumed before the first camera report (around the focus point).
        private static readonly Vector2 DefaultView=new(40,24);

        private ITemplateLevel root;
        private int roomsWide,roomsDeep;
        private RoomLightingTable table;
        private BrokenLampArt brokenLampArt;
        private readonly HashSet<string> warnedLampArt=new(StringComparer.Ordinal);
        private GameObject terrain,ambient;
        private SortingGroup sorting;
        private RectInt windowRooms=new RectInt(-1,-1,0,0);
        private GameObject eraseRoot;
        private RoomDecorResources erase;
        private readonly LightShaderContext shaderContext=new();
        private LevelLight levelLight;
        private LevelLightView lightView;
        private Coroutine lightWork;
        private Rect viewArea;
        private readonly List<(RoomSpriteAuthoring sprite,RoomDefinition definition)> pictures=new();
        private readonly List<List<int>> picturesByRoom=new();
        private readonly List<(Rect bounds,double opacity)> doors=new();   // blocking doors, level-local rectangles
        private readonly List<LevelLightScene.Door> lightDoors=new();      // every door of the level, world rectangles
        // Lamps of the level by world ID (<level>:<room x>,<room y>:<decor id>): their picture, lit frame and sprite.
        private readonly Dictionary<string,(RoomSpriteAuthoring picture,int frame,Sprite sprite,RoomDefinition definition)> lamps=new(StringComparer.Ordinal);
        private readonly HashSet<string> darkLamps=new(StringComparer.Ordinal);
        private readonly Dictionary<RoomSpriteAuthoring,string> lampByPicture=new();
        private readonly HashSet<string> brokenLamps=new(StringComparer.Ordinal);
        private readonly List<LampState> lampPlacements=new();
        private readonly Dictionary<RoomSpriteAuthoring,string> windowSourceIds=new();
        private readonly ActorShadowSources shadowSources=new();
        private float[,] shadowTerrain;
        private float shadowAmbient;
        private Vector4 ambientRadiance;
        public RoomLightingTable ShadowSettings=>table;
        public long ShadowSourceRevision {get;private set;}
        public LevelLight Light=>levelLight;
        public float ActorShadowTransmission(Vector2 from,Vector2 to)=>IsReady?shadowSources.Visibility(from,to,doors):0;
        public void SelectActorShadowSources(Vector2 actor,List<ActorShadowSource> output)
        {
            if(output==null) throw new ArgumentNullException(nameof(output));
            if(!IsReady) {output.Clear();return;}
            shadowSources.Select(actor,darkLamps,doors,table.ActorShadowMaxSources,output);
        }
        // One capture of the sources, dark lamps and doors for any number of worker-thread selections (the inputs of
        // SelectActorShadowSources at the current ShadowSourceRevision); the capture itself is cheap.
        public Func<Vector2,List<ActorShadowSource>> CaptureActorShadowSelector()
        {
            if(!IsReady) return _=>new List<ActorShadowSource>();
            var selector=shadowSources.Detached();
            var dark=new HashSet<string>(darkLamps,StringComparer.Ordinal);
            var blocking=doors.ToArray();int max=table.ActorShadowMaxSources;
            return actor=>
            {
                var output=new List<ActorShadowSource>(max);
                selector.Select(actor,dark,blocking,max,output);
                return output;
            };
        }
        // Whether every source of a selection can cast its shadow now (its direct field exists; a source that emits
        // nothing never gets one and does not hold the selection back).
        public bool ActorShadowFieldsReady(IReadOnlyList<ActorShadowSource> sources)
        {
            if(!IsReady) return false;
            for(int i=0;i<sources.Count;i++) if(!levelLight.Direct.ReadyOrNone(sources[i].Id)) return false;
            return true;
        }
        public IReadOnlyCollection<string> LampIds=>lamps.Keys;
        // The direct fields actor shadows need now: kept in the cache, missing ones built a few per frame.
        public void PrepareActorShadowFields(IReadOnlyCollection<string> ids)
        {
            if(!IsReady) return;
            levelLight.Direct.Request(ids);
            StartLightWork();
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
            if(levelLight!=null) {levelLight.SetDark(darkLamps);ShadowSourceRevision++;StartLightWork();}
            PresentationChanged?.Invoke(this);
        }

        public RoomLayout Room {get;private set;}
        public Exception Failure {get;private set;}
        public bool IsUpdating=>false; // Explicit synchronous rebuilds publish before returning.
        public long AppliedOcclusionRevision=>AppliedDoorRevision;
        public event Action<IRoomLighting> PresentationChanged;
        public event Action<IRoomLighting,Exception> PresentationFailed;
        public bool IsReady=>root!=null && levelLight!=null && Failure==null;
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
        public RectInt RoomWindow=>windowRooms;

        // The camera's visible world rectangle (FactoryLightFocus, once per frame): the light is composed around it.
        public void SetViewArea(Rect view)
        {
            if(!(view.width>0 && view.height>0) || !float.IsFinite(view.x) || !float.IsFinite(view.y)) throw new ArgumentException("Вид уровня: неверная область камеры "+view);
            viewArea=view;
        }
        // Every frame from the presentation: actor shadow masks and the frame's muzzle flashes over the camera area.
        public void RenderMuzzleFlashes(IReadOnlyList<SpriteEmission> emissions,IReadOnlyList<ProjectedLightShadow> masks,IReadOnlyList<string> sourceIds)
        {
            if(!IsReady) return;
            try {lightView.Present(viewArea,emissions,masks,sourceIds);}
            catch(Exception error) {ReportFailure(error);throw;}
        }
        // Revision of the last door snapshot in the light (-1: none yet); the loader admits actors after it.
        public long AppliedDoorRevision {get;private set;}=-1;

        // Original Box.door_opac: 1 unless the object type sets opac (door1: 0.8). Definition opacity 0 = not set.
        public const double OriginalDoorOpacity=1;

        // Door states of this level (canonical DoorState: id, definition = original object id without
        // "remains:obj:", bounds in level coordinates). Closed, intact doors stop light by their type's
        // opacity; open or destroyed ones do not. A new revision changes only the doors that changed.
        public void SyncDoors(IReadOnlyList<DoorState> states,long revision)
        {
            using var frameSection=Remains.Diagnostics.FrameHitchLog.Section("Свет: двери (SyncDoors)");
            if(root==null) throw new InvalidOperationException("Вид завода не подготовлен.");
            if(states==null) throw new ArgumentNullException(nameof(states));
            if(revision<0) throw new ArgumentOutOfRangeException(nameof(revision),"Ревизия дверей отрицательная.");
            if(revision==AppliedDoorRevision) return;
            var next=new List<(Rect,double)>();var nextLight=new List<LevelLightScene.Door>(states.Count);
            foreach(var door in states)
            {
                if(door==null) throw new ArgumentException("Вид завода: пустое состояние двери.");
                door.Validate();
                if(door.level!=root.Level) throw new ArgumentException($"Вид уровня {root.Level}: дверь {door.id} другого уровня {door.level}.");
                var definition=root.Definitions.Require("remains:obj:"+door.definition,$"вид завода, дверь {door.id}");
                if(definition.Kind!=RoomDefinitionKind.Object || definition.ObjectType!="door")
                    throw new ArgumentException($"Вид завода: дверь {door.id}, определение {definition.Id} — не дверь ({definition.Kind}, {definition.ObjectType}).");
                double opacity=definition.Opacity>0?definition.Opacity:OriginalDoorOpacity;
                if(door.Blocks) next.Add((door.Bounds,opacity));
                nextLight.Add(new LevelLightScene.Door(door.id,new Rect((Vector2)transform.TransformPoint(door.Bounds.position),door.Bounds.size),opacity,door.Blocks));
            }
            doors.Clear();doors.AddRange(next);lightDoors.Clear();lightDoors.AddRange(nextLight);
            if(levelLight!=null) {levelLight.SetDoors(lightDoors);StartLightWork();}
            AppliedDoorRevision=revision;
            ShadowSourceRevision++;
            using(Remains.Diagnostics.FrameHitchLog.Section("Свет: событие PresentationChanged")) PresentationChanged?.Invoke(this);
        }

        // Event work over frames; tools without frames (checks, previews) finish it at once.
        private void StartLightWork()
        {
            if(levelLight==null || !levelLight.HasWork) return;
            if(!Application.isPlaying || !isActiveAndEnabled)
            {
                int steps=0;
                while(levelLight.Step(64,64)) if(++steps>100000) throw new InvalidOperationException("Свет уровня: события не завершились.");
                return;
            }
            lightWork??=StartCoroutine(RunLightWork());
        }
        private System.Collections.IEnumerator RunLightWork()
        {
            while(levelLight!=null && levelLight.HasWork)
            {
                using(Remains.Diagnostics.FrameHitchLog.Section("Свет: события по кадрам")) levelLight.Step(FieldBuildsPerFrame,BounceStagesPerFrame);
                yield return null;
            }
            lightWork=null;
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
            var ambientLight=Instantiate(GameAssets.Load<Light2D>(AmbientResource)??throw new InvalidOperationException("Нет префаба общего света завода: Resources/"+AmbientResource),transform,false);
            ambient=ambientLight.gameObject;
            if(ambientLight.lightType!=Light2D.LightType.Global) throw new InvalidOperationException("Префаб общего света завода: свет не общий (Global).");
            ambientLight.intensity=(float)table.Ambient(scene.Darkness);
            ambientLight.color=new Color((float)scene.Tint[0],(float)scene.Tint[1],(float)scene.Tint[2]);
            shadowAmbient=ambientLight.intensity*(.2126f*ambientLight.color.r+.7152f*ambientLight.color.g+.0722f*ambientLight.color.b);
            // The same ambient for WorldLit artwork (actors, props, doors), which does not read Light2D.
            ambientRadiance=(Vector4)(ambientLight.color*ambientLight.intensity);

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
                if(level.RoomInstances.GetChild(i).GetComponent<RoomTemplateInstance>()==null)
                    throw new InvalidOperationException($"Источники света: комната {i}, {room.Template.Template} без RoomTemplateInstance.");
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

        // The window of rooms around a world point (pictures, background erase); the level light is built on the first
        // call. Nothing is rebuilt while the point stays in the same window.
        public void Focus(Vector2 world)
        {
            if(root==null) throw new InvalidOperationException("Вид уровня не подготовлен.");
            bool first=levelLight==null;
            if(first)
            {
                BuildLight();
                SetViewArea(new Rect(world-DefaultView*.5f,DefaultView));
            }
            var rooms=WindowFor(world);
            if(rooms!=windowRooms) ShowWindow(rooms);
            // The first light is shown at once; afterwards the frame's presentation composes it (with shadows).
            if(first) lightView.Present(viewArea,null,null,null);
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

        // The level light: whole level, built at once (entry; the user accepted entry freezes for now).
        private void BuildLight()
        {
            // Doors not synchronized yet (a level without door states) start open; the first SyncDoors applies them.
            using var section=Remains.Diagnostics.FrameHitchLog.Section("Переход: свет уровня");
            var shadows=new List<ActorShadowSource>();
            var built=Create(DirectLight.DefaultCacheBytes,out var walls,shadows);
            try
            {
                built.BuildAll();
                shaderContext.Activate();
                var view=new LevelLightView(built,shaderContext);
                levelLight=built;lightView=view;
            }
            catch {built.Dispose();throw;}
            shadowTerrain=walls;
            shadowSources.Configure(shadowTerrain,Vector2.zero,1f/table.RadianceResolution,transform.worldToLocalMatrix,shadows,shadowAmbient);
            shaderContext.Publish(()=>Shader.SetGlobalVector("_RemainsAmbient",ambientRadiance));
            ShadowSourceRevision++;
            PresentationChanged?.Invoke(this);
        }

        private void ShowWindow(RectInt rooms)
        {
            using var section=Remains.Diagnostics.FrameHitchLog.Section("Окно комнат: картинки и маска окон");
            windowRooms=rooms;ShowWindowRooms();
            var level=root.Grid;float cell=(float)level.Cell;
            int column=int.MaxValue,row=int.MaxValue,right=0,bottom=0;
            foreach(var r in root.Rooms) if(Inside(r,rooms))
            {column=Math.Min(column,r.Cells.x);row=Math.Min(row,r.Cells.y);right=Math.Max(right,r.Cells.xMax);bottom=Math.Max(bottom,r.Cells.yMax);}
            int width=right-column,height=bottom-row;
            Vector2 origin=transform.TransformPoint(new Vector2(column*cell,(level.Height-row-height)*cell));var size=new Vector2(width*cell,height*cell);
            var bounds=new Rect(origin,size);
            var erases=new List<(Sprite,Matrix4x4)>();
            for(int i=0;i<root.Rooms.Count;i++) if(Inside(root.Rooms[i],rooms)) foreach(int index in picturesByRoom[i])
            {
                var authored=pictures[index].sprite;
                if(authored.Erase==null || !ShownForPreparation(authored)) continue;
                var renderer=authored.GetComponent<SpriteRenderer>();var matrix=authored.transform.localToWorldMatrix;
                var local=renderer.sprite.bounds;Vector2 a=matrix.MultiplyPoint3x4(local.min),b=matrix.MultiplyPoint3x4(local.max);
                if(bounds.Overlaps(Rect.MinMaxRect(Mathf.Min(a.x,b.x),Mathf.Min(a.y,b.y),Mathf.Max(a.x,b.x),Mathf.Max(a.y,b.y)))) erases.Add((authored.Erase,matrix));
            }
            // The previous mask goes first: its release clears the erase globals of the shared light context.
            if(eraseRoot!=null) {erase?.ReleaseResources();Destroy(eraseRoot);}
            eraseRoot=null;erase=null;
            if(erases.Count>0)
            {
                eraseRoot=new GameObject($"Маска окон фона {rooms.x},{rooms.y}");eraseRoot.transform.SetParent(transform,false);
                erase=eraseRoot.AddComponent<RoomDecorResources>();erase.BindLightContext(shaderContext);
                erase.BuildErase(erases,origin,size,GameAssets.Load<Shader>(RoomDecorView.EraseShaderResource)??throw new InvalidOperationException("Нет шейдера "+RoomDecorView.EraseShaderResource),publish:false);
                erase.PublishErase();
            }
            else shaderContext.Publish(()=>Shader.SetGlobalFloat("_RemainsEraseEnabled",0));
            PresentationChanged?.Invoke(this);
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

        // The light of the whole level from the installed pictures, doors and lamps. Heavy: wall raster of the whole
        // level, one readback per new sprite. The view builds its own on the first Focus; checks may build more.
        public LevelLight CreateLevelLight(long directCacheBytes=DirectLight.DefaultCacheBytes)=>Create(directCacheBytes,out _,null);
        private LevelLight Create(long directCacheBytes,out float[,] walls,List<ActorShadowSource> shadows)
        {
            if(root==null) throw new InvalidOperationException("Вид уровня не подготовлен.");
            var grid=root.Grid;var definitions=root.Definitions;int resolution=table.RadianceResolution;float texel=1f/resolution;
            using(Remains.Diagnostics.FrameHitchLog.Section("Свет уровня: стены"))
                walls=RoomLightScene.Absorption(grid,RoomGeometryBuilder.Build(grid,definitions),resolution,table.BeamTransmittance,LightRules.MaxObjectTexelAbsorption,table.BeamHeight);
            var scene=RoomLightScene.Build(grid,definitions,root.Lands,table,table.RadiancePower,table.RadianceLampSize);
            bool hasWindowLight=LightRules.TryWindowEmission(table,root.Lands,grid,scene.Tint,out var windowEmission,out string fon);
            if(!hasWindowLight) Debug.LogWarning($"Свет уровня {root.Level}: нет света окон для дальнего фона «{fon}» в таблице света — окна не светят.",this);
            Vector2 origin=transform.TransformPoint(Vector3.zero);
            int levelWidth=walls.GetLength(0),levelHeight=walls.GetLength(1);
            var roomOf=new int[pictures.Count];
            for(int r=0;r<picturesByRoom.Count;r++) foreach(int p in picturesByRoom[r]) roomOf[p]=r;
            var objects=new List<LevelLightScene.ObjectSprite>();var list=new List<LevelLightSource>();
            for(int p=0;p<pictures.Count;p++)
            {
                var (picture,definition)=pictures[p];
                if(!ShownForPreparation(picture)) continue;
                var renderer=picture.GetComponent<SpriteRenderer>();var matrix=picture.transform.localToWorldMatrix;
                var scale=matrix.lossyScale;float areaScale=Mathf.Abs(scale.x*scale.y);
                if(definition.Kind==RoomDefinitionKind.Object)
                {
                    double absorption=1-table.ObjectTransmittance(definition,LightSpriteShapes.Measure(renderer.sprite,texel).Area*areaScale);
                    if(absorption>0) objects.Add(new LevelLightScene.ObjectSprite(renderer.sprite,matrix,absorption));
                }
                if(lampByPicture.TryGetValue(picture,out string lampId))
                {
                    var (_,frame,sprite,_)=lamps[lampId];
                    double share=table.LampFrameShare(definition,frame);
                    if(share>0)
                    {
                        var shape=LightSpriteShapes.Measure(sprite,texel);double area=shape.Area*areaScale;
                        var lampSource=Source(lampId,LevelLightSourceKind.Lamp,sprite,matrix,shape.Strata,
                            LightRules.LampRadiance(table,table.Lamps[definition.Id],scene.Tint,share,area,texel),(float)LightRules.EmitterWidth(area,texel),
                            (float)(LevelLightSources.LampReachScale*table.Lamps[definition.Id].Radius),!darkLamps.Contains(lampId));
                        list.Add(lampSource);Shadow(lampSource,shape,matrix);
                    }
                }
                if(hasWindowLight && windowSourceIds.TryGetValue(picture,out string windowId))
                {
                    var shape=LightSpriteShapes.Measure(picture.Erase,texel);
                    var windowSource=Source(windowId,LevelLightSourceKind.Window,picture.Erase,matrix,shape.Strata,windowEmission,
                        (float)LightRules.EmitterWidth(shape.Area*areaScale,texel),LevelLightSources.WindowReach,true);
                    list.Add(windowSource);Shadow(windowSource,shape,matrix);
                }
                // The light scene glows a lamp decor through its lamp emission only (LightScene: lamp branch first).
                if(!(definition.Kind==RoomDefinitionKind.Decor && table.Lamps.ContainsKey(definition.Id)) && table.Glow.TryGetValue(definition.Id,out var glow) && glow.Gain>0)
                {
                    var bright=LightSpriteShapes.MeasureGlow(renderer.sprite,(float)glow.Threshold);
                    if(bright.Points.Length==0) continue;
                    var bounds=renderer.sprite.bounds;var points=new Vector3[bright.Points.Length];
                    for(int i=0;i<points.Length;i++)
                        points[i]=new Vector3(bounds.min.x+bright.Points[i].x*bounds.size.x,bounds.min.y+bright.Points[i].y*bounds.size.y,bright.Points[i].z);
                    double area=bright.AreaFraction*bounds.size.x*bounds.size.y*areaScale;
                    var room=root.Rooms[roomOf[p]];
                    list.Add(Source($"screen:{root.Level}:{room.X},{room.Y}:{picture.Kind}:{picture.Index}",LevelLightSourceKind.Screen,renderer.sprite,matrix,points,
                        bright.Color*(float)glow.Gain,(float)LightRules.EmitterWidth(area,texel),LevelLightSources.ScreenReach,true));
                }
            }
            LevelLightScene lightScene=null;
            try
            {
                lightScene=new LevelLightScene(walls,origin,resolution,objects);
                lightScene.SetDoors(lightDoors,null);
                var result=new LevelLight(lightScene,new LevelLightSources(list,lightScene.Width,lightScene.Height),(float)table.RadianceAlbedo,directCacheBytes);
                var names=new List<string>(objects.Count);
                foreach(var item in objects) names.Add($"{item.Sprite.name}@{item.LocalToWorld.GetColumn(3)}:{item.Absorption:R}");
                names.Sort(StringComparer.Ordinal);
                Debug.Log($"Свет уровня {root.Level}: сетка {lightScene.Width}×{lightScene.Height}, источников {list.Count}, поглощающих предметов {objects.Count} "+
                    $"(отпечаток {Fingerprint(string.Join("|",names))}), дверей {lightDoors.Count}.",this);
                return result;
            }
            catch {lightScene?.Dispose();throw;}

            LevelLightSource Source(string id,LevelLightSourceKind kind,Sprite sprite,Matrix4x4 matrix,Vector3[] local,Color radiance,float width,float reach,bool enabled)
            {
                var points=new Vector4[local.Length];
                for(int i=0;i<local.Length;i++)
                {
                    var world=matrix.MultiplyPoint3x4(new Vector3(local[i].x,local[i].y,0));
                    points[i]=new Vector4(world.x,world.y,local[i].z,0);
                }
                var spriteBounds=sprite.bounds;Vector2 a=matrix.MultiplyPoint3x4(spriteBounds.min),b=matrix.MultiplyPoint3x4(spriteBounds.max);
                var form=Rect.MinMaxRect(Mathf.Min(a.x,b.x),Mathf.Min(a.y,b.y),Mathf.Max(a.x,b.x),Mathf.Max(a.y,b.y));
                int x0=Mathf.Clamp(Mathf.FloorToInt((form.xMin-reach-origin.x)*resolution),0,levelWidth),x1=Mathf.Clamp(Mathf.CeilToInt((form.xMax+reach-origin.x)*resolution),0,levelWidth);
                int y0=Mathf.Clamp(Mathf.FloorToInt((form.yMin-reach-origin.y)*resolution),0,levelHeight),y1=Mathf.Clamp(Mathf.CeilToInt((form.yMax+reach-origin.y)*resolution),0,levelHeight);
                return new LevelLightSource(id,kind,LevelLightSources.Normalise(points,id),radiance,width,reach,form,new RectInt(x0,y0,x1-x0,y1-y0),enabled);
            }
            // A lamp or window as an actor shadow source: eight weighted columns of its form, strength by luminance × width.
            void Shadow(LevelLightSource source,LightSpriteShapes.Shape shape,Matrix4x4 matrix)
            {
                if(shadows==null) return;
                var samples=new Vector3[shape.Columns.Length];
                for(int i=0;i<samples.Length;i++)
                {
                    var world=matrix.MultiplyPoint3x4(new Vector3(shape.Columns[i].x,shape.Columns[i].y,0));
                    samples[i]=new Vector3(world.x,world.y,shape.Columns[i].z);
                }
                if(samples.Length==0) {Debug.LogWarning($"Источник тени {source.Id}: форма не излучает ни одного текселя; источник пропущен.",this);return;}
                Vector2 center=Vector2.zero;float weight=0;
                foreach(var sample in samples) {center+=new Vector2(sample.x,sample.y)*sample.z;weight+=sample.z;}
                center/=weight;float radius=0;
                foreach(var sample in samples) radius=Mathf.Max(radius,Vector2.Distance(center,new Vector2(sample.x,sample.y)));
                float luminance=.2126f*source.Radiance.r+.7152f*source.Radiance.g+.0722f*source.Radiance.b;
                shadows.Add(new ActorShadowSource(source.Id,center,Mathf.Max(.05f,radius),luminance*source.Width,emissionPoints:samples,reach:source.Reach));
            }
        }

        private static string Fingerprint(string text)
        {
            using var sha=System.Security.Cryptography.SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text)),0,8).Replace("-","").ToLowerInvariant();
        }

        // Only the rooms of the window keep their pictures active: all 84 factory rooms are ~5600 sprites, more
        // than a SortingGroup handles (4095) and a large per-frame cost. Room instances are RoomInstances children in
        // Rooms order. Walls (terrain tilemaps) stay level-wide.
        private void ShowWindowRooms()
        {
            var instances=root.RoomInstances;var rooms=root.Rooms;
            if(instances.childCount!=rooms.Count) throw new InvalidOperationException($"Вид уровня: комнат {instances.childCount}, в раскладке {rooms.Count}.");
            for(int i=0;i<rooms.Count;i++)
            {
                bool inside=Inside(rooms[i],windowRooms);
                var instance=instances.GetChild(i).gameObject;
                if(instance.activeSelf!=inside) instance.SetActive(inside);
            }
        }
        private static bool Inside(TemplateLevelRoom r,RectInt rooms)=>r.X>=rooms.xMin && r.X<rooms.xMax && r.Y>=rooms.yMin && r.Y<rooms.yMax;

        public void Release()
        {
            if(lightWork!=null) {StopCoroutine(lightWork);lightWork=null;}
            lightView?.Dispose();lightView=null;
            levelLight?.Dispose();levelLight=null;
            if(eraseRoot!=null) {erase?.ReleaseResources();Destroy(eraseRoot);}
            eraseRoot=null;erase=null;
            shaderContext.Release();
            if(terrain!=null) Destroy(terrain);
            if(ambient!=null) {ambient.SetActive(false);Destroy(ambient);}
            terrain=ambient=null;root=null;pictures.Clear();doors.Clear();lightDoors.Clear();lamps.Clear();lampByPicture.Clear();darkLamps.Clear();brokenLamps.Clear();lampPlacements.Clear();AppliedDoorRevision=-1;AppliedLampRevision=-1;windowRooms=new RectInt(-1,-1,0,0);
            shadowSources.Clear();windowSourceIds.Clear();shadowTerrain=null;ShadowSourceRevision++;
            Room=null;PresentationChanged?.Invoke(this);
        }

        private void OnDestroy()=>Release();

        private new static void Destroy(UnityEngine.Object o) {if(Application.isPlaying) UnityEngine.Object.Destroy(o);else DestroyImmediate(o);}
    }
}
