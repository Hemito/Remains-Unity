using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using Remains.RoomFormat;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Remains.Presentation
{
    // Visuals of a prepared factory level (FactoryLevelRoot): walls from the composed grid with backdrops per
    // room, ambient light, and — inside a light window of rooms around a focus point — the background erase
    // mask (windows) and the radiance field lit by the installed room prefabs' pictures (RoomSpriteAuthoring).
    // The whole 4×21 level is too large for one field (≈1500×4000 texels at 10/m, cascades of ~3000×8000)
    // and one erase mask (>16384 px), so both cover only the window; outside it windows are not cut and lamps
    // do not light. Everything is built in explicit calls (Initialize, Focus, RefreshLight), never per frame.
    // Doors: SyncDoors(states, revision) adds closed doors as light occluders (radiance scene); grid walls stay static.
    // Lifetime: Initialize after FactoryLevelRoot.InstantiateRooms, Release before the root is destroyed.
    public sealed class FactoryLevelView : MonoBehaviour
    {
        public const int WindowRooms=3;            // rooms across and down in the light window (odd)
        public const int SettleUpdates=8;          // field updates after a rebuild: one wall bounce per update

        private FactoryLevelRoot root;
        private RoomLightingTable table;
        private GameObject terrain,ambient,window;
        private RoomRadianceField field;
        private SortingGroup sorting;
        private RectInt windowRooms=new RectInt(-1,-1,0,0);
        private readonly List<(RoomSpriteAuthoring sprite,RoomDefinition definition)> pictures=new();
        private readonly List<(Rect bounds,double opacity)> doors=new();   // blocking doors, level-local rectangles

        public bool IsReady=>root!=null && field!=null;
        public RectInt LightWindowRooms=>windowRooms;
        // Revision of the last door snapshot in the light (-1: none yet); the loader admits actors after it.
        public long AppliedDoorRevision {get;private set;}=-1;
        public int DoorOccluderTexels=>field!=null?field.OccluderTexels:0;

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
                if(door.level!=root.Prepared.Level) throw new ArgumentException($"Вид завода {root.Prepared.Level}: дверь {door.id} другого уровня {door.level}.");
                var definition=root.Catalog.Definitions.Require("remains:obj:"+door.definition,$"вид завода, дверь {door.id}");
                if(definition.Kind!=RoomDefinitionKind.Object || definition.ObjectType!="door")
                    throw new ArgumentException($"Вид завода: дверь {door.id}, определение {definition.Id} — не дверь ({definition.Kind}, {definition.ObjectType}).");
                if(door.Blocks) next.Add((door.Bounds,definition.Opacity>0?definition.Opacity:OriginalDoorOpacity));
            }
            doors.Clear();doors.AddRange(next);
            AppliedDoorRevision=revision;
            if(field!=null) RefreshLight();
        }

        public void Initialize(FactoryLevelRoot level)
        {
            if(level==null) throw new ArgumentNullException(nameof(level));
            if(root!=null) throw new InvalidOperationException("Вид завода уже подготовлен: повторная подготовка запрещена.");
            if(level.gameObject!=gameObject) throw new InvalidOperationException("Вид завода должен лежать на объекте корня завода.");
            var prepared=level.Prepared??throw new InvalidOperationException("Вид завода: корень ещё не подготовлен (FactoryLevelRoot.Initialize).");
            if(level.Rooms.childCount!=prepared.Grid.Rooms.Count)
                throw new InvalidOperationException($"Вид завода: установлено комнат {level.Rooms.childCount}, в раскладке {prepared.Grid.Rooms.Count} (сначала InstantiateRooms).");
            var catalog=level.Catalog;
            table=RoomLightingTable.Read(catalog.Lighting.text,catalog.Lighting.name);
            var grid=prepared.Grid;
            var lands=catalog.Lands;var definitions=catalog.Definitions;

            // Walls and decor of all rooms sort together: one group on the level root, none on the terrain.
            if(!TryGetComponent(out sorting)) sorting=gameObject.AddComponent<SortingGroup>();   // not ?? : Unity null
            sorting.sortingOrder=WorldVisualOrderPolicy.SceneryRoomOrder;
            var backdrops=new List<(RoomFile,Vector2)>();
            foreach(var room in grid.Rooms) backdrops.Add((room.Template,level.Offset(room)));
            terrain=RoomTerrainView.Build(grid.Level,definitions,lands,catalog.Art,Vector2.zero,table.ShadowEdge,transform,backdrops,ownSortingGroup:false);

            // Ambient: one level for the factory (per-room darkness and tint are not applied yet).
            var scene=RoomLightScene.Build(grid.Level,definitions,lands,table,table.RadiancePower,table.RadianceLampSize);
            foreach(string issue in scene.Issues) Debug.LogWarning("Вид завода: "+issue,this);
            ambient=new GameObject("Общий свет завода");
            ambient.transform.SetParent(transform,false);
            var light=ambient.AddComponent<Light2D>();
            light.lightType=Light2D.LightType.Global;
            light.intensity=(float)table.Ambient(scene.Darkness);
            light.color=new Color((float)scene.Tint[0],(float)scene.Tint[1],(float)scene.Tint[2]);

            pictures.Clear();
            foreach(var sprite in level.Rooms.GetComponentsInChildren<RoomSpriteAuthoring>(true))
                pictures.Add((sprite,definitions.Require(sprite.Definition,"вид завода, "+sprite.name)));
            root=level;
            Debug.Log($"Вид завода {prepared.Level}: сетка {grid.Level.Width}×{grid.Level.Height}, комнат {grid.Rooms.Count}, картинок {pictures.Count}.",this);
        }

        // Moves the light window to the rooms around a world point; rebuilds only when the window changes.
        public void Focus(Vector2 world)
        {
            if(root==null) throw new InvalidOperationException("Вид завода не подготовлен.");
            var layout=root.Prepared.Grid.Layout;
            var level=root.Prepared.Grid.Level;
            float cell=(float)level.Cell;
            Vector2 local=transform.InverseTransformPoint(world);
            int stepX=root.Prepared.Grid.StepX,stepY=root.Prepared.Grid.StepY;
            int cx=Mathf.Clamp(Mathf.FloorToInt(local.x/cell/stepX),0,layout.Width-1);
            int cy=Mathf.Clamp(Mathf.FloorToInt((level.Height-local.y/cell)/stepY),0,layout.Depth-1);
            int w=Math.Min(WindowRooms,layout.Width),h=Math.Min(WindowRooms,layout.Depth);
            var rooms=new RectInt(Mathf.Clamp(cx-WindowRooms/2,0,layout.Width-w),Mathf.Clamp(cy-WindowRooms/2,0,layout.Depth-h),w,h);
            if(rooms.Equals(windowRooms) && field!=null) return;
            windowRooms=rooms;
            RefreshLight();
        }

        // Rebuilds the window's erase mask and radiance field (after Focus, or when lights/occluders change:
        // door state, a lamp switched or broken). Doors are not light occluders yet: a future door change calls
        // this after updating the light scene input.
        public void RefreshLight()
        {
            if(root==null) throw new InvalidOperationException("Вид завода не подготовлен.");
            if(windowRooms.width<=0) throw new InvalidOperationException("Вид завода: окно света не выбрано (Focus).");
            DestroyWindow();
            var grid=root.Prepared.Grid;var level=grid.Level;var catalog=root.Catalog;
            float cell=(float)level.Cell;
            int column=windowRooms.x*grid.StepX,row=windowRooms.y*grid.StepY;
            int width=windowRooms.width*grid.StepX+1,height=windowRooms.height*grid.StepY+1;
            var region=RoomFileRegion.Crop(level,column,row,width,height);
            Vector2 localOrigin=new Vector2(column*cell,(level.Height-row-height)*cell);
            Vector2 origin=transform.TransformPoint(localOrigin);
            var size=new Vector2(width*cell,height*cell);
            var bounds=new Rect(origin,size);

            window=new GameObject($"Окно света {windowRooms.x},{windowRooms.y}");
            window.transform.SetParent(transform,false);
            var sprites=new List<RoomSpritePlacement>();
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
                sprites.Add(new RoomSpritePlacement{Definition=definition,Sprite=renderer.sprite,Frame=authored.Frame,LocalToWorld=matrix});
                if(authored.Erase!=null) erases.Add((authored.Erase,matrix));
            }
            Shader.SetGlobalFloat("_RemainsEraseEnabled",0);
            if(erases.Count>0)
                window.AddComponent<RoomDecorResources>().BuildErase(erases,origin,size,Resources.Load<Shader>(RoomDecorView.EraseShaderResource)??throw new InvalidOperationException("Нет шейдера "+RoomDecorView.EraseShaderResource));
            var occluders=new List<(Rect,double)>();
            foreach(var (local,opacity) in doors)
            {
                var world=new Rect((Vector2)transform.TransformPoint(local.position),local.size);   // the root is not scaled
                if(bounds.Overlaps(world)) occluders.Add((world,opacity));
            }
            field=RoomRadianceField.Build(region,catalog.Definitions,catalog.Lands,table,origin,window.transform,sprites,occluders);
            for(int i=0;i<SettleUpdates;i++) field.Render();
        }

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
            if(ambient!=null) Destroy(ambient);
            terrain=ambient=null;root=null;pictures.Clear();doors.Clear();AppliedDoorRevision=-1;windowRooms=new RectInt(-1,-1,0,0);
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
