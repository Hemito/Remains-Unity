using System;
using System.Collections.Generic;
using Remains.Domain;
using Remains.RoomFormat;
using UnityEngine;

namespace Remains.Content
{
    // The hub's walls, decor and light from the camp's room templates (rbl room_0_0 and the training room
    // room_1_0), side by side without a shared column, as the hub's authored physics (Levels/hub.prefab) has them.
    // Gameplay stays in the hub prefab (surfaces, ladders, containers, training targets, travel point); this only
    // supplies the template level for the level view. Build is an explicit loading step.
    public sealed class HubTemplateLevel : MonoBehaviour,ITemplateLevel
    {
        [SerializeField,InspectorName("Данные комнат (определения, земли, графика, свет)")] private FactoryCatalogAsset catalog;
        [SerializeField,InspectorName("Земля (граница стен)")] private string landEntry="rbl";
        [SerializeField,InspectorName("Шаблоны слева направо")] private RoomTemplateAsset[] templates=Array.Empty<RoomTemplateAsset>();
        [SerializeField,InspectorName("Префабы комнат (обычные)")] private RoomTemplateInstance[] prefabs=Array.Empty<RoomTemplateInstance>();
        [SerializeField,InspectorName("Контейнер комнат")] private Transform roomInstances;

        private readonly List<TemplateLevelRoom> rooms=new();
        private RoomFile grid;
        private RoomLayout physics;

        public string Level=>physics?.id??throw new InvalidOperationException("Хаб из шаблонов ещё не собран.");
        public RoomLayout Physics=>physics;
        public RoomFile Grid=>grid;
        public IReadOnlyList<TemplateLevelRoom> Rooms=>rooms;
        public Transform RoomInstances=>roomInstances;
        public RoomDefinitionCatalog Definitions=>catalog.Definitions;
        public RoomLandCatalog Lands=>catalog.Lands;
        public RoomTileArtAsset Art=>catalog.Art;
        public TextAsset Lighting=>catalog.Lighting;

        public void Configure(FactoryCatalogAsset data,RoomTemplateAsset[] roomTemplates,RoomTemplateInstance[] roomPrefabs,Transform container)
        {
            catalog=data;templates=roomTemplates;prefabs=roomPrefabs;roomInstances=container;
        }

        public void Build(RoomLayout layout)
        {
            if(grid!=null) throw new InvalidOperationException("Хаб из шаблонов уже собран.");
            if(layout==null) throw new ArgumentNullException(nameof(layout));
            if(catalog==null || roomInstances==null || templates.Length==0 || templates.Length!=prefabs.Length)
                throw new InvalidOperationException("Хаб из шаблонов: нет данных, контейнера или пар шаблон–префаб.");
            if(roomInstances.childCount!=0) throw new InvalidOperationException("Хаб из шаблонов: комнаты уже установлены.");
            var definitions=catalog.Definitions;
            string border=FactoryGridBuilder.BorderSolidOf(catalog.Lands,landEntry,definitions);
            var parts=new List<RoomFile>();
            int column=0;
            for(int i=0;i<templates.Length;i++)
            {
                var template=templates[i];var prefab=prefabs[i];
                if(template==null || prefab==null || prefab.Template!=template || prefab.Mirror)
                    throw new InvalidOperationException($"Хаб из шаблонов: комната {i} — нет шаблона, префаба или префаб не обычный вариант своего шаблона.");
                var room=template.Room;
                // One room composed alone: its edge walls become the land border (Location.mainFrame), as everywhere.
                var single=new FactoryLayout{Land=RoomLandCatalog.LandOf(room.Template),Width=1,Depth=1};
                single.Cells.Add(new FactoryCell{Template=room.Template});
                var composed=FactoryGridBuilder.Build(single,new Dictionary<string,RoomFile>{[room.Template]=room},definitions,border,closeOuter:false);
                foreach(string issue in composed.Issues) Debug.LogWarning($"Хаб, {room.Template}: {issue}",this);
                parts.Add(composed.Level);
                float cell=(float)room.Cell;
                var offset=new Vector2(column*cell,0);
                rooms.Add(new TemplateLevelRoom(i,0,room,new RectInt(column,0,room.Width,room.Height),offset));
                var instance=Instantiate(prefab,roomInstances,false);
                instance.name=$"Комната хаба {i}: {room.Template}";
                instance.transform.localPosition=offset;
                // The hub's objects are its own gameplay content (containers, targets): the template's object pictures
                // would draw them twice. The previous hub import did not draw them either; decor stays.
                int hidden=0;
                foreach(var picture in instance.GetComponentsInChildren<RoomSpriteAuthoring>(true))
                    if(picture.Kind==RoomSpriteKind.Object) {picture.gameObject.SetActive(false);hidden++;}
                if(hidden>0) Debug.Log($"Хаб, {room.Template}: скрыто картинок объектов шаблона {hidden} — объекты хаба задаёт его игровой префаб.",this);
                column+=room.Width;
            }
            grid=RoomFileRegion.JoinHorizontal($"level:{RoomLandCatalog.LandOf(templates[0].Room.Template)}:hub",parts);
            physics=layout;
            float right=grid.Width*(float)grid.Cell;
            if(layout.right>right+1e-3f || layout.top>grid.Height*(float)grid.Cell+1e-3f)
                Debug.LogWarning($"Хаб из шаблонов: физика хаба ({layout.right}×{layout.top} м) шире сетки шаблонов ({right}×{grid.Height*grid.Cell} м).",this);
        }
    }
}
