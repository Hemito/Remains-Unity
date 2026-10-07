using System;
using System.Collections.Generic;
using System.IO;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Content
{
    // Definitions and compiled graphics are content packs, not executable game rules.
    [Serializable] public sealed class CosmeticPackEntry
    {public string definitions,visuals,texts;}
    public sealed class LoadedCosmeticPack
    {public string id,data,texts;public int position;public byte[] visuals;}
    public static class CosmeticContentMods
    {
        private static string prepared;
        private static readonly List<AssetBundle> bundles=new();
        public static void Prepare(LoadedWeaponCatalog content)
        {
            if(prepared!=null)
            {
                if(prepared!=content.fingerprint) throw new InvalidOperationException(
                    "Состав визуальных модов изменён после подготовки. Для новой загрузки перезапустите игру.");
                return;
            }
            var definitions=CosmeticCatalog.Load();
            WearableRenderCatalog.Prepare();
            foreach(var pack in content.cosmetics)
            {
                try
                {
                    definitions.RegisterPack(pack.data,"мод="+pack.id+", позиция="+pack.position);
                    if(pack.texts!=null)
                    {
                        var text=StateJson.FromJson<ItemTextManifest>(pack.texts);
                        foreach(var entry in text.items) ItemTextCatalog.Load().Register(entry);
                    }
                    if(pack.visuals==null) continue; // Explicit definition-only pack; a separate declared dependency may provide graphics.
                    var bundle=AssetBundle.LoadFromMemory(pack.visuals);
                    if(bundle==null) throw new InvalidDataException("Не удалось прочитать AssetBundle визуального снаряжения.");
                    bundles.Add(bundle);
                    var assets=bundle.LoadAllAssets<WearableRenderAsset>();
                    if(assets.Length==0) throw new InvalidDataException("В AssetBundle нет подготовленных WearableRenderAsset.");
                    foreach(var asset in assets) WearableRenderCatalog.Register(asset,source:"мод="+pack.id+", позиция="+pack.position);
                    foreach(var icons in bundle.LoadAllAssets<InventoryIconAsset>())
                        foreach(var entry in icons.CreateLookup()) CosmeticItemArtwork.Register(entry.Key,entry.Value,"мод="+pack.id);
                }
                catch(Exception error)
                {throw new InvalidDataException($"Ошибка визуального мода: id={pack.id}, позиция={pack.position}. {error.Message}",error);}
            }
            prepared=content.fingerprint;
        }
    }
}
