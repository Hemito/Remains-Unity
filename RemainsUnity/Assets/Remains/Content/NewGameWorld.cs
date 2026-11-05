using Remains.Domain;
using System;
using System.Security.Cryptography;
using UnityEngine;

namespace Remains.Content
{
    public static class NewGameWorld
    {
        public const string FactoryDefinitionId=FactoryCatalogAsset.DefinitionId;
        public static WorldState Create()
        {
            // Only explicit creation chooses entropy. Loading/copying a WorldState never changes its saved seed.
            using var random=RandomNumberGenerator.Create();var bytes=new byte[4];random.GetBytes(bytes);
            return Create(BitConverter.ToInt32(bytes,0)&int.MaxValue);
        }
        // Reproducible worlds/tools use an explicit seed, including the established seed104 fixture.
        public static WorldState Create(int seed)
        {
            var world=new WorldState{seed=seed};
            var catalog=GameAssets.Load<FactoryCatalogAsset>(FactoryCatalogAsset.ResourceKey)
                ??throw new InvalidOperationException("Новый мир: отсутствует каталог игрового завода.");
            FactoryPreparation.Create(world,catalog,0);
            return world;
        }
    }
}
