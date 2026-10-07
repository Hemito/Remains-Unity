using System;
using System.IO;
using Remains.Content;
using UnityEngine;
namespace Remains.Editor
{
    public static class CosmeticContentModChecks
    {
        public static void Run()
        {
            string temp=Path.GetFullPath(Path.GetTempPath()),root=Path.Combine(temp,"remains-cosmetic-packs-"+Guid.NewGuid().ToString("N"));
            if(Directory.Exists(root)) throw new InvalidOperationException("Папка проверки визуальных модов уже существует.");
            Directory.CreateDirectory(Path.Combine(root,"outfits"));
            try
            {
                Write(root,"load-order.json","{\"mods\":[\"outfits\"]}");
                string manifest="{\"id\":\"check:outfits\",\"version\":\"1\",\"dependencies\":[],\"cosmetics\":{\"definitions\":\"cosmetics.json\",\"texts\":\"texts.json\",\"visuals\":\"outfits.bundle\"}}";
                Write(root,"outfits/manifest.json",manifest);
                Write(root,"outfits/cosmetics.json","{\"schema\":1,\"slots\":[],\"items\":[{\"id\":\"check:coat\",\"slot\":\"armor.body\",\"visual\":\"check:coat-visual\",\"anatomy\":\"pony\"}]}");
                Write(root,"outfits/texts.json","{\"schema\":1,\"items\":[{\"id\":\"check:coat\",\"name\":\"Плащ\",\"description\":\"Визуальный плащ.\"}]}");
                File.WriteAllBytes(Path.Combine(root,"outfits/outfits.bundle"),new byte[]{1,2,3});
                var first=WeaponCatalogLoader.Load("{\"weapons\":[]}",root);
                Require(first.cosmetics.Count==1 && first.cosmetics[0].id=="check:outfits" && first.cosmetics[0].position==1 &&
                    first.cosmetics[0].texts.Contains("Плащ") && first.manifest.packs.Count==2,"пакет потерял данные, порядок или русский текст");
                File.WriteAllBytes(Path.Combine(root,"outfits/outfits.bundle"),new byte[]{1,2,4});
                var graphicsChanged=WeaponCatalogLoader.Load("{\"weapons\":[]}",root);
                Require(first.fingerprint!=graphicsChanged.fingerprint && first.manifest.packs[1].fingerprint!=graphicsChanged.manifest.packs[1].fingerprint,
                    "изменение графики не меняет отпечаток пакета и контента");
                Write(root,"outfits/texts.json","{\"schema\":1,\"items\":[{\"id\":\"check:coat\",\"name\":\"Плащ\",\"description\":\"Другой русский текст.\"}]}");
                Require(graphicsChanged.fingerprint!=WeaponCatalogLoader.Load("{\"weapons\":[]}",root).fingerprint,"изменение текста не входит в отпечаток");
                foreach(string bad in new[]{"../outside.json","","C:/outside.json"})
                {
                    Write(root,"outfits/manifest.json",manifest.Replace("cosmetics.json",bad));
                    Reject(()=>WeaponCatalogLoader.Load("{\"weapons\":[]}",root),"check:outfits");
                }
                Write(root,"outfits/manifest.json",manifest);
                Write(root,"outfits/texts.json","{\"schema\":1,\"items\":[]}");
                Reject(()=>WeaponCatalogLoader.Load("{\"weapons\":[]}",root),"check:outfits");
                Write(root,"outfits/texts.json","{\"schema\":1,\"items\":[{\"id\":\"check:coat\",\"name\":\"Плащ\",\"description\":\"Визуальный плащ.\"}]}");
                Write(root,"outfits/cosmetics.json","{\"schema\":2,\"slots\":[],\"items\":[]}");
                Reject(()=>WeaponCatalogLoader.Load("{\"weapons\":[]}",root),"check:outfits");
                Debug.Log("CosmeticContentModChecks PASS: отдельный пакет определений/русских текстов/AssetBundle bytes, отпечатки графики и текста, отказ schema/path/повреждённого текста с ID ответственного мода. Загрузка реального AssetBundle проверяется отдельно.");
            }
            finally
            {
                string absolute=Path.GetFullPath(root);
                if(!absolute.StartsWith(temp.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) ||
                    !Path.GetFileName(absolute).StartsWith("remains-cosmetic-packs-",StringComparison.Ordinal))
                    throw new InvalidOperationException("Небезопасная папка очистки проверки визуальных модов.");
                Directory.Delete(absolute,true);
            }
        }
        private static void Write(string root,string path,string text)=>File.WriteAllText(Path.Combine(root,path),text);
        private static void Require(bool value,string reason) {if(!value) throw new InvalidOperationException("Визуальные моды: "+reason+".");}
        private static void Reject(Action action,string context)
        {
            try {action();} catch(InvalidDataException error) {Require(error.Message.Contains(context),"ошибка не содержит ID пакета");return;}
            throw new InvalidOperationException("Повреждённый визуальный пакет был принят: "+context);
        }
    }
}
