using System;
using System.Collections.Generic;
using Remains.Domain;
using Remains.RoomFormat;
using UnityEngine;

namespace Remains.Content
{
    // One room of a level built from room templates: its place in the window grid (X across, Y down), its cells in
    // the composed grid (top-left column/row, size) and its bottom-left offset in level metres.
    public readonly struct TemplateLevelRoom
    {
        public readonly int X,Y;
        public readonly RoomFile Template;
        public readonly RectInt Cells;
        public readonly Vector2 Offset;
        public TemplateLevelRoom(int x,int y,RoomFile template,RectInt cells,Vector2 offset)
        {X=x;Y=y;Template=template;Cells=cells;Offset=offset;}
    }

    // A level whose walls, decor and light come from room templates (the generated factory, the hub): what the
    // level view (Presentation FactoryLevelView) builds from. Grid is the composed cells of the whole level;
    // RoomInstances holds the installed room prefabs in Rooms order.
    public interface ITemplateLevel
    {
        string Level {get;}
        RoomLayout Physics {get;}
        RoomFile Grid {get;}
        IReadOnlyList<TemplateLevelRoom> Rooms {get;}
        Transform RoomInstances {get;}
        RoomDefinitionCatalog Definitions {get;}
        RoomLandCatalog Lands {get;}
        RoomTileArtAsset Art {get;}
        TextAsset Lighting {get;}
    }

    // The generated factory as a template level (FactoryLevelRoot after InstantiateRooms).
    public sealed class FactoryTemplateLevel : ITemplateLevel
    {
        private readonly FactoryLevelRoot root;
        private readonly List<TemplateLevelRoom> rooms=new();
        public FactoryTemplateLevel(FactoryLevelRoot factory)
        {
            root=factory??throw new ArgumentNullException(nameof(factory));
            var prepared=factory.Prepared??throw new InvalidOperationException("Корень завода ещё не подготовлен (FactoryLevelRoot.Initialize).");
            var grid=prepared.Grid;
            foreach(var room in grid.Rooms)
                rooms.Add(new TemplateLevelRoom(room.Cell.X,room.Cell.Y,room.Template,
                    new RectInt(room.Column,room.Row,room.Template.Width,room.Template.Height),factory.Offset(room)));
        }
        public string Level=>root.Prepared.Level;
        public RoomLayout Physics=>root.Prepared.Physics;
        public RoomFile Grid=>root.Prepared.Grid.Level;
        public IReadOnlyList<TemplateLevelRoom> Rooms=>rooms;
        public Transform RoomInstances=>root.Rooms;
        public RoomDefinitionCatalog Definitions=>root.Catalog.Definitions;
        public RoomLandCatalog Lands=>root.Catalog.Lands;
        public RoomTileArtAsset Art=>root.Catalog.Art;
        public TextAsset Lighting=>root.Catalog.Lighting;
    }
}
