using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Remains.Content
{
    [Serializable] public sealed class WeaponDefinition
    {
        public string id;
        public float damage,speedDamage,maxSpeedDamage,range,cooldown,manaCost;
        public string ammo;
        public int magazineSize,ammoPerShot;
        public float reloadSeconds;
    }
    [Serializable] public sealed class WeaponList { public WeaponDefinition[] weapons; }
    [Serializable] public sealed class ModOrder { public string[] mods; }
    [Serializable] public sealed class ModManifest { public string id,version; public string[] dependencies; }

    public static class WeaponCatalog
    {
        private static readonly Dictionary<string,WeaponDefinition> definitions=new();
        public static IEnumerable<WeaponDefinition> All => definitions.Values;
        public static string ModFingerprint { get; private set; }="";
        public static string ModDescription { get; private set; }="";
        public static WeaponDefinition Get(string id)=>definitions.TryGetValue(id,out var value)?value:
            throw new InvalidOperationException($"Отсутствует определение оружия: {id}.");

        public static void Load()
        {
            definitions.Clear();
            var asset=Resources.Load<TextAsset>("Content/weapons");
            if(asset==null) throw new InvalidOperationException("Не найдены определения оружия Content/weapons.");
            Apply(asset.text,"base",false);
            string root=Path.GetFullPath(Path.Combine(Application.streamingAssetsPath,"Mods"));
            string orderPath=Path.Combine(root,"load-order.json");
            var signature=new StringBuilder("prototype-content-v1").Append(asset.text);
            var description=new StringBuilder("Базовый контент v1");
            var loaded=new HashSet<string>();
            if(File.Exists(orderPath))
            {
                var order=JsonUtility.FromJson<ModOrder>(File.ReadAllText(orderPath));
                if(order?.mods==null) throw new InvalidDataException("В load-order.json отсутствует массив mods.");
                foreach(string folder in order.mods)
                {
                    string directory=Path.GetFullPath(Path.Combine(root,folder));
                    if(!directory.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Папка мода находится вне Mods: {folder}.");
                    var manifest=JsonUtility.FromJson<ModManifest>(File.ReadAllText(Path.Combine(directory,"manifest.json")));
                    if(string.IsNullOrWhiteSpace(manifest.id) || !loaded.Add(manifest.id))
                        throw new InvalidDataException($"Некорректный/повторный ID мода: {manifest.id}.");
                    foreach(string dependency in manifest.dependencies??Array.Empty<string>())
                        if(dependency==manifest.id || !loaded.Contains(dependency)) throw new InvalidDataException($"Мод {manifest.id} требует более ранней загрузки {dependency}.");
                    string path=Path.Combine(directory,"weapons.json");
                    string data=File.Exists(path)?File.ReadAllText(path):"";
                    if(data.Length>0) Apply(data,manifest.id,true);
                    signature.Append('\n').Append(manifest.id).Append('@').Append(manifest.version).Append(':').Append(data);
                    description.Append('\n').Append(manifest.id).Append(" · ").Append(manifest.version);
                }
            }
            using var hash=SHA256.Create();
            ModFingerprint=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(signature.ToString()))).Replace("-","");
            ModDescription=description.ToString();
        }
        private static void Apply(string json,string source,bool replace)
        {
            var list=JsonUtility.FromJson<WeaponList>(json);
            if(list?.weapons==null) throw new InvalidDataException($"Нет массива weapons: {source}.");
            foreach(var weapon in list.weapons)
            {
                if(string.IsNullOrEmpty(weapon.id) || weapon.damage<0 || weapon.speedDamage<0 || weapon.maxSpeedDamage<0 || weapon.range<=0 || weapon.cooldown<=0)
                    throw new InvalidDataException($"Некорректное оружие {weapon.id}, источник {source}.");
                if(weapon.manaCost<0 || (!string.IsNullOrEmpty(weapon.ammo) &&
                    (weapon.magazineSize<=0 || weapon.ammoPerShot<=0 || weapon.ammoPerShot>weapon.magazineSize || weapon.reloadSeconds<=0)))
                    throw new InvalidDataException($"Некорректный расход/магазин оружия {weapon.id}, источник {source}.");
                if(!replace && definitions.ContainsKey(weapon.id)) throw new InvalidDataException($"Повторное определение {weapon.id}, источник {source}.");
                definitions[weapon.id]=weapon;
            }
        }
    }
}
