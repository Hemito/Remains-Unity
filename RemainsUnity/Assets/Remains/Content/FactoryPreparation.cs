using System;
using System.Collections.Generic;
using Remains.Domain;
using Remains.RoomFormat;
using UnityEngine;

namespace Remains.Content
{
    // Explicit loading operation. The complete grid remains available to physics, view and objects.
    public sealed class PreparedFactory
    {
        public string Level {get;}
        public FactoryGrid Grid {get;}
        public RoomLayout Physics {get;}
        internal PreparedFactory(string level,FactoryGrid grid,RoomLayout physics) {Level=level;Grid=grid;Physics=physics;}
    }
    public static class FactoryPreparation
    {
        public static WorldFactory Create(WorldState world,FactoryCatalogAsset catalog,int index)
        {
            if(world==null || catalog==null) throw new ArgumentNullException(world==null?nameof(world):nameof(catalog));
            if(WorldFactories.Find(world,WorldFactories.Level(index))!=null)
                throw new InvalidOperationException($"Мир {world.worldId}: завод {index} уже создан; повторная генерация запрещена.");
            var layout=FactoryLayoutGenerator.Generate(new List<RoomFile>(catalog.Templates.Values),"plant",WorldFactories.Seed(world),index,4,21);
            return WorldFactories.Add(world,layout,FactoryCatalogAsset.DefinitionId,catalog.ContentFingerprint);
        }
        public static PreparedFactory Prepare(WorldState world,FactoryCatalogAsset catalog,string level)
        {
            if(catalog==null) throw new ArgumentNullException(nameof(catalog));
            var layout=WorldFactories.Require(world,level,FactoryCatalogAsset.DefinitionId,catalog.ContentFingerprint);
            if(layout.Land!="plant") throw new InvalidOperationException($"Мир {world.worldId}, завод {level}: земля {layout.Land} не поддерживается каталогом завода.");
            foreach(var cell in layout.Cells) catalog.Prefab(cell.Template,cell.Mirror);
            var grid=FactoryGridBuilder.Build(layout,catalog.Templates,catalog.Definitions,
                FactoryGridBuilder.BorderSolid(catalog.Lands,layout.Land,catalog.Definitions),true);
            foreach(string issue in layout.Issues) Debug.LogWarning($"Мир {world.worldId}, завод {level}, раскладка: {issue}",catalog);
            foreach(string issue in grid.Issues) Debug.LogWarning($"Мир {world.worldId}, завод {level}, сетка: {issue}",catalog);
            var physics=RoomTemplateLayout.Create(grid.Level,catalog.Definitions);
            physics.id=level;
            return new PreparedFactory(level,grid,physics);
        }
    }
}
