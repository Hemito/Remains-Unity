using Remains.Domain;
using System;
using UnityEngine;

namespace Remains.Content
{
    public static class NewGameWorld
    {
        public const string FactoryDefinitionId=FactoryCatalogAsset.DefinitionId;
        public static WorldState Create()
        {
            var world=new WorldState();
            var catalog=GameAssets.Load<FactoryCatalogAsset>(FactoryCatalogAsset.ResourceKey)
                ??throw new InvalidOperationException("Новый мир: отсутствует каталог игрового завода.");
            FactoryPreparation.Create(world,catalog,0);
            return world;
        }
    }
}
