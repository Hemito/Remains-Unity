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
        public const string AmbientResource="Presentation/FactoryAmbientLight";          // field updates after a rebuild: one wall bounce per update

        private ITemplateLevel root;
        private int roomsWide,roomsDeep;
        private RoomLightingTable table;
        private BrokenLampArt brokenLampArt;
        private readonly HashSet<string> warnedLampArt=new(StringComparer.Ordinal);
        private GameObject terrain,ambient,window;
        private RoomRadianceField field;
        private SortingGroup sorting;
        private RectInt windowRooms=new RectInt(-1,-1,0,0);
        private readonly List<(RoomSpriteAuthoring sprite,RoomDefinition definition)> pictures=new();
        private readonly List<(RoomSpriteAuthoring sprite,RoomSpritePlacement placement)> windowPictures=new();
        private readonly List<(Rect bounds,double opacity)> doors=new();   // blocking doors, level-local rectangles
        // Lamps of the level by world ID (<level>:<room x>,<room y>:<decor id>): their picture, lit frame and sprite.
        private readonly Dictionary<string,(RoomSpriteAuthoring picture,int frame,Sprite sprite,RoomDefinition definition)> lamps=new(StringComparer.Ordinal);
        private readonly HashSet<string> darkLamps=new(StringComparer.Ordinal);
        private readonly HashSet<string> brokenLamps=new(StringComparer.Ordinal);
        private readonly List<LampState> lampPlacements=new();
        public IReadOnlyCollection<string> LampIds=>lamps.Keys;
        public IReadOnlyList<LampState> LampPlacements=>lampPlacements;
        public string LampWorldLevel=>root is HubTemplateLevel?"hub":root?.Level;
        // Revision of the last lamp snapshot in the light (-1: none yet).
        public long AppliedLampRevision {get;private set;}=-1;

        // A dark lamp stops emitting. Ordinary switching uses its original off frame;
        // only IDs explicitly present in broken use the authored broken artwork.
        public void SyncLamps(IReadOnlyCollection<string> dark,long revision,IReadOnlyCollection<string> broken=null)
        {
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
                    for(int i=0;i<SettleUpdates;i++) field.Render();
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
                for(int i=0;i<SettleUpdates;i++) field.Render();
            }
            AppliedDoorRevision=revision;
            PresentationChanged?.Invoke(this);
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
            brokenLampArt=Resources.Load<BrokenLampArt>(BrokenLampArt.ResourcePath)??throw new InvalidOperationException("Нет каталога разбитой графики ламп: "+BrokenLampArt.ResourcePath);
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
            var light=Instantiate(Resources.Load<Light2D>(AmbientResource)??throw new InvalidOperationException("Нет префаба общего света завода: Resources/"+AmbientResource),transform,false);
            ambient=light.gameObject;
            if(light.lightType!=Light2D.LightType.Global) throw new InvalidOperationException("Префаб общего света завода: свет не общий (Global).");
            light.intensity=(float)table.Ambient(scene.Darkness);
            light.color=new Color((float)scene.Tint[0],(float)scene.Tint[1],(float)scene.Tint[2]);
            // The same ambient for WorldLit artwork (actors, props, doors), which does not read Light2D.
            Shader.SetGlobalVector("_RemainsAmbient",(Vector4)(light.color*light.intensity));

            pictures.Clear();lamps.Clear();darkLamps.Clear();brokenLamps.Clear();lampPlacements.Clear();
            foreach(var sprite in level.RoomInstances.GetComponentsInChildren<RoomSpriteAuthoring>(true))
                pictures.Add((sprite,definitions.Require(sprite.Definition,"вид уровня, "+sprite.name)));
            for(int i=0;i<level.Rooms.Count;i++)
            {
                var room=level.Rooms[i];
                foreach(var picture in level.RoomInstances.GetChild(i).GetComponentsInChildren<RoomSpriteAuthoring>(true))
                {
                    if(picture.Kind!=RoomSpriteKind.Decor || !table.Lamps.ContainsKey(picture.Definition)) continue;
                    var decor=room.Template.Decor[picture.Index];
                    if(decor.Id==null) throw new InvalidOperationException($"Вид уровня {level.Level}: лампа без ID, шаблон {room.Template.Template}, декор {picture.Index}.");
                    string id=$"{level.Level}:{room.X},{room.Y}:{decor.Id}";
                    var renderer=picture.GetComponent<SpriteRenderer>();
                    lamps.Add(id,(picture,picture.Frame,renderer.sprite,definitions.Require(picture.Definition,"лампа")));
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
            windowRooms=rooms;
            RefreshLight();
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
            int ww=Math.Min(WindowRooms,roomsWide),wh=Math.Min(WindowRooms,roomsDeep);
            return new RectInt(Mathf.Clamp(cx-WindowRooms/2,0,roomsWide-ww),Mathf.Clamp(cy-WindowRooms/2,0,roomsDeep-wh),ww,wh);
        }

        // Rebuilds the erase mask and radiance field after the focus window changes.
        // Door and lamp snapshots update the existing field instead.
        public void RefreshLight()
        {
            if(root==null) throw new InvalidOperationException("Вид уровня не подготовлен.");
            if(windowRooms.width<=0) throw new InvalidOperationException("Вид уровня: окно света не выбрано (Focus).");
            DestroyWindow();
            var level=root.Grid;
            ShowWindowRooms();
            float cell=(float)level.Cell;
            // The window's cells: the union of its rooms' cells in the composed grid.
            int column=int.MaxValue,row=int.MaxValue,right=0,bottom=0;
            foreach(var r in root.Rooms)
                if(InWindow(r)) {column=Math.Min(column,r.Cells.x);row=Math.Min(row,r.Cells.y);right=Math.Max(right,r.Cells.xMax);bottom=Math.Max(bottom,r.Cells.yMax);}
            int width=right-column,height=bottom-row;
            var region=RoomFileRegion.Crop(level,column,row,width,height);
            Vector2 localOrigin=new Vector2(column*cell,(level.Height-row-height)*cell);
            Vector2 origin=transform.TransformPoint(localOrigin);
            var size=new Vector2(width*cell,height*cell);
            var bounds=new Rect(origin,size);

            window=new GameObject($"Окно света {windowRooms.x},{windowRooms.y}");
            window.transform.SetParent(transform,false);
            var sprites=new List<RoomSpritePlacement>();
            windowPictures.Clear();
            var erases=new List<(Sprite,Matrix4x4)>();
            foreach(var (authored,definition) in pictures)
            {
                if(authored==null || !Shown(authored)) continue;   // adapters disable pictures their objects replace
                var renderer=authored.GetComponent<SpriteRenderer>();
                var matrix=authored.transform.localToWorldMatrix;
                // From the sprite, not Renderer.bounds: an inactive source root reports empty renderer bounds.
                var local=renderer.sprite.bounds;
                Vector2 a=matrix.MultiplyPoint3x4(local.min),b=matrix.MultiplyPoint3x4(local.max);
                if(!bounds.Overlaps(Rect.MinMaxRect(Mathf.Min(a.x,b.x),Mathf.Min(a.y,b.y),Mathf.Max(a.x,b.x),Mathf.Max(a.y,b.y)))) continue;
                var placement=new RoomSpritePlacement{Definition=definition,Sprite=renderer.sprite,Frame=authored.Frame,LocalToWorld=matrix,Erase=authored.Erase,Dark=IsDark(authored)};
                sprites.Add(placement);windowPictures.Add((authored,placement));
                if(authored.Erase!=null) erases.Add((authored.Erase,matrix));
            }
            Shader.SetGlobalFloat("_RemainsEraseEnabled",0);
            if(erases.Count>0)
                window.AddComponent<RoomDecorResources>().BuildErase(erases,origin,size,Resources.Load<Shader>(RoomDecorView.EraseShaderResource)??throw new InvalidOperationException("Нет шейдера "+RoomDecorView.EraseShaderResource));
            field=RoomRadianceField.Build(region,root.Definitions,root.Lands,table,origin,window.transform,sprites,WorldDoorOccluders());
            for(int i=0;i<SettleUpdates;i++) field.Render();
            PresentationChanged?.Invoke(this);
        }

        private bool IsDark(RoomSpriteAuthoring picture)
        {
            if(darkLamps.Count==0) return false;
            foreach(var pair in lamps) if(pair.Value.picture==picture) return darkLamps.Contains(pair.Key);
            return false;
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

        private bool InWindow(TemplateLevelRoom r)=>r.X>=windowRooms.xMin && r.X<windowRooms.xMax && r.Y>=windowRooms.yMin && r.Y<windowRooms.yMax;

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
            DestroyWindow();
            if(terrain!=null) Destroy(terrain);
            if(ambient!=null) {ambient.SetActive(false);Destroy(ambient);}
            terrain=ambient=null;root=null;pictures.Clear();windowPictures.Clear();doors.Clear();lamps.Clear();darkLamps.Clear();brokenLamps.Clear();lampPlacements.Clear();AppliedDoorRevision=-1;AppliedLampRevision=-1;windowRooms=new RectInt(-1,-1,0,0);
            Room=null;PresentationChanged?.Invoke(this);
        }

        private void DestroyWindow()
        {
            // Immediately: the old field's and erase mask's OnDestroy clear shared shader globals, which a deferred
            // destroy would do after the new window has set them.
            if(window!=null) DestroyImmediate(window);
            window=null;field=null;
        }

        private void OnDestroy()=>Release();

        private new static void Destroy(UnityEngine.Object o) {if(Application.isPlaying) UnityEngine.Object.Destroy(o);else DestroyImmediate(o);}
    }
}
