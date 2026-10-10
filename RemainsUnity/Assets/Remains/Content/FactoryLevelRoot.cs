using System;
using System.Collections.Generic;
using Remains.Domain;
using Remains.RoomFormat;
using UnityEngine;

namespace Remains.Content
{
    public sealed class FactoryPlacedObject
    {
        public string Id {get;}
        public FactoryRoomPlacement Room {get;}
        public RoomObject Source {get;}
        public int Index {get;}
        public Vector2 Position {get;}
        internal FactoryPlacedObject(string id,FactoryRoomPlacement room,RoomObject source,int index,Vector2 position)
        {Id=id;Room=room;Source=source;Index=index;Position=position;}
    }

    // Instantiated from an authored shell prefab, explicitly initialized while loading.
    // The stored layout/grid is authoritative; prefab metadata validates its authored consumers.
    public sealed class FactoryLevelRoot : MonoBehaviour
    {
        public const string ResourceKey="Content/FactoryLevelRoot";
        [SerializeField,InspectorName("Каталог завода")] private FactoryCatalogAsset catalog;
        [SerializeField,InspectorName("Геометрия уровня")] private RoomAuthoring authoring;
        [SerializeField,InspectorName("Контейнер комнат")] private Transform rooms;
        public PreparedFactory Prepared {get;private set;}
        public FactoryCatalogAsset Catalog=>catalog;
        public RoomAuthoring Authoring=>authoring;
        public Transform Rooms=>rooms;
        public IReadOnlyList<FactoryPlacedObject> Objects {get;private set;}
        public IReadOnlyList<FactoryRolePoint> RolePoints {get;private set;}
        private readonly Dictionary<FactoryRoomPlacement,RoomTemplateInstance> instances=new();
        public void HideObjectArtwork(FactoryPlacedObject placement)
        {
            if(!instances.TryGetValue(placement.Room,out var room))
                throw new InvalidOperationException("Не установлена комната объекта: "+placement.Id);
            foreach(var sprite in room.GetComponentsInChildren<RoomSpriteAuthoring>(true))
                if(sprite.Kind==RoomSpriteKind.Object && sprite.Index==placement.Index)
                    sprite.GetComponent<SpriteRenderer>().enabled=false;
        }
        public void Configure(FactoryCatalogAsset source,RoomAuthoring geometry,Transform roomParent)
        {
            if(source==null || geometry==null || roomParent==null || geometry.transform!=transform || roomParent.parent!=transform)
                throw new ArgumentException("Корень завода: задайте каталог, геометрию на корне и дочерний контейнер комнат.");
            catalog=source;authoring=geometry;rooms=roomParent;
        }
        public Vector2 Offset(FactoryRoomPlacement room)
        {
            if(Prepared==null) throw new InvalidOperationException("Корень завода ещё не подготовлен.");
            return Offset(Prepared,room);
        }
        private static Vector2 Offset(PreparedFactory prepared,FactoryRoomPlacement room)
        {
            var level=prepared.Grid.Level;float cell=(float)level.Cell;
            return new Vector2(room.Column*cell,(level.Height-room.Row-room.Template.Height)*cell);
        }
        public void Initialize(WorldState world,string level)
        {
            if(Prepared!=null) throw new InvalidOperationException("Повторная подготовка корня завода запрещена: "+level);
            if(catalog==null || authoring==null || rooms==null) throw new InvalidOperationException("Корень завода: отсутствуют сериализованные ссылки.");
            var prepared=FactoryPreparation.Prepare(world,catalog,level);
            var placed=new List<FactoryPlacedObject>();var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var room in prepared.Grid.Rooms)
            {
                var prefab=catalog.Prefab(room.Cell.Template,room.Cell.Mirror);
                var points=prefab.GetComponentsInChildren<RoomTemplateObjectPoint>(true);
                var source=room.Template;
                var offset=Offset(prepared,room);
                foreach(var point in points)
                {
                    var obj=source.Objects[point.Index];
                    var definition=catalog.Definitions.Require(obj.Type,$"{level}, {room.Cell.X},{room.Cell.Y}, {obj.Id}");
                    int width=Math.Max(1,definition.ObjectSize);
                    int x=RoomSpriteLayout.ObjectCellX(source,obj,definition,room.Cell.Mirror);
                    var anchor=new Vector2((float)((x+width/2.0)*source.Cell),(float)(obj.Y*source.Cell));
                    var rect=RoomAuthoring.ReadRect(point.transform,prefab.transform);
                    if(point.Cell!=new Vector2Int(x,obj.Y) || point.Width!=width ||
                        Vector2.Distance(new Vector2(rect.x,rect.y),anchor)>.0001f ||
                        Mathf.Abs(rect.width-1)+Mathf.Abs(rect.height-1)>.0001f)
                        throw new InvalidOperationException($"Корень завода {level}: точка {point.Id} в {prefab.name} не соответствует клетке/ширине/якорю шаблона.");
                    string id=$"{level}:{room.Cell.X},{room.Cell.Y}:{point.Id}";
                    if(!ids.Add(id)) throw new InvalidOperationException("Повтор мирового ID объекта завода: "+id);
                    placed.Add(new FactoryPlacedObject(id,room,obj,point.Index,offset+anchor));
                }
            }
            var arrival=FactoryArrival.Require(prepared,placed);
            prepared.Physics.spawnX=arrival.Position.x;prepared.Physics.spawnY=arrival.Position.y;
            authoring.ConfigurePrepared(prepared.Physics);
            Prepared=prepared;Objects=placed.AsReadOnly();
            RolePoints=Array.AsReadOnly(FactoryRolePoints.Read(prepared,Objects));
        }
        public void InstantiateRooms()
        {
            if(Prepared==null || rooms.childCount!=0) throw new InvalidOperationException("Корень завода: комнаты не подготовлены или уже установлены.");
            foreach(var room in Prepared.Grid.Rooms)
            {
                var instance=Instantiate(catalog.Prefab(room.Cell.Template,room.Cell.Mirror),rooms,false);
                instance.name=$"Комната {room.Cell.X},{room.Cell.Y}: {room.Cell.Template}";
                instance.transform.localPosition=Offset(room);
                instances.Add(room,instance);
            }
        }
        public static FactoryLevelRoot Create(WorldState world,string level)
        {
            var asset=Resources.Load<GameObject>(ResourceKey)??throw new InvalidOperationException("Нет авторского корня завода: "+ResourceKey);
            var prefab=asset.GetComponent<FactoryLevelRoot>()??throw new InvalidOperationException("Нет FactoryLevelRoot в "+ResourceKey);
            var root=Instantiate(prefab);
            try {root.Initialize(world,level);return root;}
            catch {if(Application.isPlaying) Destroy(root.gameObject);else DestroyImmediate(root.gameObject);throw;}
        }
    }
}
