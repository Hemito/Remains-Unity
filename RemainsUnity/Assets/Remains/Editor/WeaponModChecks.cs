using System;
using System.IO;
using Remains.Content;
using UnityEngine;

namespace Remains.Editor
{
    public static class WeaponModChecks
    {
        public static void Run()
        {
            string root=Path.Combine(Path.GetTempPath(),"remains-mod-checks-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root,"first"));Directory.CreateDirectory(Path.Combine(root,"second"));
            string original="{\"weapons\":[{\"id\":\"pistol\",\"damage\":11,\"range\":20,\"cooldown\":0.3}]}";
            string flight="{\"weapons\":[{\"id\":\"flight\",\"range\":20,\"cooldown\":1,\"projectileSpeed\":90,\"projectileSpeedVariation\":0.2}]}";
            string noMods=Path.Combine(root,"none");
            var flightDefinition=WeaponCatalogLoader.Load(flight,noMods).definitions["flight"];
            Require(flightDefinition.ProjectileSpeedRange==new Vector2(72,108),"Диапазон скорости не следует данным оружия");
            Reject(()=>WeaponCatalogLoader.Load(flight.Replace("0.2","1"),noMods),"диапазон скорости");
            Reject(()=>WeaponCatalogLoader.Load(flight.Replace("90","0"),noMods),"диапазон скорости");
            Reject(()=>WeaponCatalogLoader.Load(flight.Replace("90","3.4e38"),noMods),"диапазон скорости");
            string replacement=original.Replace("11","25");
            var before=WeaponCatalog.Get("pistol");
            File.WriteAllText(Path.Combine(root,"load-order.json"),"{\"mods\":[\"first\"]}");
            File.WriteAllText(Path.Combine(root,"first/weapons.json"),replacement);
            File.WriteAllText(Path.Combine(root,"first/manifest.json"),Manifest("first",null));
            Reject(()=>WeaponCatalogLoader.Load(original,root),"Конфликт оружия pistol");
            Require(ReferenceEquals(before,WeaponCatalog.Get("pistol")),"Ошибка не изменяет рабочий каталог");
            File.WriteAllText(Path.Combine(root,"first/manifest.json"),Manifest("first","base"));
            var accepted=WeaponCatalogLoader.Load(original,root);
            Require(accepted.definitions["pistol"].damage==25 && accepted.description.Contains("замена pistol из base"),"Явная замена применяется и видима");
            File.WriteAllText(Path.Combine(root,"load-order.json"),"{\"mods\":[\"first\",\"second\"]}");
            File.WriteAllText(Path.Combine(root,"second/weapons.json"),original);
            File.WriteAllText(Path.Combine(root,"second/manifest.json"),Manifest("second","base"));
            Reject(()=>WeaponCatalogLoader.Load(original,root),"текущий источник=first");
            File.WriteAllText(Path.Combine(root,"second/manifest.json"),Manifest("second","first"));
            var chained=WeaponCatalogLoader.Load(original,root);
            Require(chained.definitions["pistol"].damage==11 && chained.fingerprint!=accepted.fingerprint,"Цепочка явных замен и отпечаток");
            Require(chained.manifest.packs.Count==3 && chained.manifest.packs[0].id=="base" && chained.manifest.packs[1].id=="first" &&
                chained.manifest.packs[2].id=="second" && chained.manifest.packs[2].version=="1" && chained.manifest.fingerprint==chained.fingerprint,
                "Структурированный манифест сохраняет порядок, версии и отпечаток");
            var previous=accepted.manifest.Copy();
            previous.extensions=new System.Collections.Generic.Dictionary<string,Newtonsoft.Json.Linq.JToken> {{"mod:unknown","kept"}};
            var history=new System.Collections.Generic.List<ContentManifest>();
            Remains.Persistence.ContentProvenance.Record(ref previous,ref history);
            Require(history.Count==1 && (string)history[0].extensions["mod:unknown"]=="kept" && previous.fingerprint==WeaponCatalog.ModFingerprint,
                "Прежний манифест с неизвестными полями сохраняется в истории");
            Remains.Persistence.ContentProvenance.Record(ref previous,ref history);
            Require(history.Count==1,"Повторное сохранение не раздувает историю контента");
            Require(ContentComparison.Compare(previous,previous.Copy()).Count==0,"Совпадающий манифест не требует подтверждения");
            Require(ContentComparison.Compare(null,previous).Count>0,"Неизвестный старый набор требует сравнения");
            var reordered=chained.manifest.Copy();reordered.packs.Reverse();
            Require(ContentComparison.Compare(chained.manifest,reordered).Count>0,"Изменение порядка видно даже при одинаковом общем hash");
            var malformed=previous.Copy();malformed.packs=null;
            Reject(()=>ContentComparison.Compare(malformed,previous),"список пакетов");
            malformed=previous.Copy();malformed.packs.Add(null);
            Reject(()=>ContentComparison.Compare(malformed,previous),"позиция=2");
            malformed=previous.Copy();malformed.packs.Add(malformed.packs[0]);
            Reject(()=>ContentComparison.Compare(malformed,previous),"повторный ID=base");
            malformed=previous.Copy();malformed.packs[0].id=" ";
            Reject(()=>ContentComparison.Compare(malformed,previous),"его ID");
            var stale=previous.Copy();stale.packs[0].version="old-metadata";
            var staleHistory=new System.Collections.Generic.List<ContentManifest>();
            Remains.Persistence.ContentProvenance.Record(ref stale,ref staleHistory);
            Require(staleHistory.Count==1 && staleHistory[0].packs[0].version=="old-metadata" && stale.packs[0].version==previous.packs[0].version,
                "Одинаковый общий hash не скрывает изменение версии пакета при записи");
            File.WriteAllText(Path.Combine(root,"second/weapons.json"),"{\"weapons\":[]}");
            Reject(()=>WeaponCatalogLoader.Load(original,root),"не содержит определения");
            File.WriteAllText(Path.Combine(root,"second/weapons.json"),"{\"weapons\":[{\"id\":\"pistol\",\"range\":20,\"cooldown\":1},{\"id\":\"pistol\",\"range\":20,\"cooldown\":1}]}");
            Reject(()=>WeaponCatalogLoader.Load(original,root),"в одном пакете");
            File.WriteAllText(Path.Combine(root,"second/manifest.json"),"{\"id\":\"second\",\"version\":\"1\",\"dependencies\":[\"missing\"]}");
            Reject(()=>WeaponCatalogLoader.Load(original,root),"зависимости missing");
            Require(ReferenceEquals(before,WeaponCatalog.Get("pistol")),"Частичные пакеты не опубликованы");
            Debug.Log("WeaponModChecks PASS: явные замены, конфликты источников, дубликаты, зависимости, целостность каталога. Фикстуры: "+root);
        }
        private static string Manifest(string id,string source)=>"{\"id\":\""+id+"\",\"version\":\"1\",\"weaponOverrides\":"+
            (source==null?"[]":"[{\"id\":\"pistol\",\"source\":\""+source+"\"}]")+"}";
        private static void Reject(Action action,string reason)
        {
            try {action();} catch(InvalidDataException failure)
            {Require(failure.Message.Contains(reason),"Диагностика: "+failure.Message);return;}
            throw new InvalidOperationException("Принят некорректный мод: "+reason);
        }
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException("WeaponModChecks: "+reason);}
    }
}
