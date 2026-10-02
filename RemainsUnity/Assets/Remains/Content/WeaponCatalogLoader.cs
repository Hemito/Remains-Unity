using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Remains.Serialization;

namespace Remains.Content
{
    public sealed class LoadedWeaponCatalog
    {
        public readonly Dictionary<string,WeaponDefinition> definitions=new();
        public string fingerprint,description;
        public readonly List<LoadedCodeMod> codeMods=new();
        public readonly ContentManifest manifest=new();
    }
    public static class WeaponCatalogLoader
    {
        public static LoadedWeaponCatalog Load(string baseJson,string modRoot)
        {
            var result=new LoadedWeaponCatalog();var sources=new Dictionary<string,string>();
            Apply(result,sources,baseJson,"base",Array.Empty<WeaponOverride>());
            result.manifest.packs.Add(new ContentPackRecord {id="base",version="prototype-content-v2",fingerprint=Hash(baseJson),dependencies=Array.Empty<string>()});
            string root=Path.GetFullPath(modRoot),orderPath=Path.Combine(root,"load-order.json");
            var signature=new StringBuilder("prototype-content-v2").Append(baseJson);
            var description=new StringBuilder("Базовый контент v2");var loaded=new HashSet<string>();
            if(File.Exists(orderPath))
            {
                string orderJson=File.ReadAllText(orderPath);signature.Append(orderJson);
                var order=StateJson.FromJson<ModOrder>(orderJson);
                if(order?.mods==null) throw new InvalidDataException("В load-order.json отсутствует массив mods.");
                for(int index=0;index<order.mods.Length;index++)
                {
                    string folder=order.mods[index],source="не прочитан";
                    try
                    {
                        if(string.IsNullOrWhiteSpace(folder)) throw new InvalidDataException("Пустая папка мода.");
                        string directory=Path.GetFullPath(Path.Combine(root,folder));
                        if(!directory.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("Папка находится вне Mods.");
                        string manifestJson=File.ReadAllText(Path.Combine(directory,"manifest.json"));
                        var manifest=StateJson.FromJson<ModManifest>(manifestJson);
                        source=manifest?.id;
                        if(manifest==null || string.IsNullOrWhiteSpace(source) || source=="base" || loaded.Contains(source) || string.IsNullOrWhiteSpace(manifest.version))
                            throw new InvalidDataException("Некорректный/повторный ID или отсутствует версия мода.");
                        foreach(string dependency in manifest.dependencies??Array.Empty<string>())
                            if(dependency==null || !loaded.Contains(dependency))
                                throw new InvalidDataException($"Требуется более ранняя загрузка зависимости {dependency}.");
                        string path=Path.Combine(directory,"weapons.json");
                        string data=File.Exists(path)?File.ReadAllText(path):"{\"weapons\":[]}";
                        Apply(result,sources,data,source,manifest.weaponOverrides??Array.Empty<WeaponOverride>());
                        string codeHash="";
                        if(manifest.code!=null)
                        {
                            var code=manifest.code;
                            if(code.apiVersion!=GameplayCodeMods.ApiVersion || string.IsNullOrWhiteSpace(code.entryType) ||
                                string.IsNullOrWhiteSpace(code.replacesRulesFrom) || string.IsNullOrWhiteSpace(code.assembly) ||
                                Path.GetFileName(code.assembly)!=code.assembly || !code.assembly.EndsWith(".dll",StringComparison.OrdinalIgnoreCase))
                                throw new InvalidDataException("Некорректное объявление code: API, DLL, entryType или replacesRulesFrom.");
                            byte[] bytes=File.ReadAllBytes(Path.Combine(directory,code.assembly));
                            using var codeDigest=SHA256.Create();
                            codeHash=BitConverter.ToString(codeDigest.ComputeHash(bytes)).Replace("-","");
                            result.codeMods.Add(new LoadedCodeMod {id=source,position=index+1,entry=code,assembly=bytes});
                        }
                        loaded.Add(source);signature.Append('\n').Append(manifestJson).Append('\n').Append(data);
                        if(manifest.code!=null) signature.Append("\ncode-sha256:").Append(codeHash);
                        result.manifest.packs.Add(new ContentPackRecord {id=source,version=manifest.version,
                            fingerprint=Hash(manifestJson+"\n"+data+(manifest.code==null?"":"\ncode-sha256:"+codeHash)),dependencies=manifest.dependencies??Array.Empty<string>()});
                        description.Append('\n').Append(index+1).Append(". ").Append(source).Append(" · ").Append(manifest.version);
                        foreach(var replacement in manifest.weaponOverrides??Array.Empty<WeaponOverride>())
                            description.Append("; замена ").Append(replacement.id).Append(" из ").Append(replacement.source);
                    }
                    catch(Exception failure) when(failure is IOException || failure is InvalidDataException || failure is ArgumentException)
                    {throw new InvalidDataException($"Ошибка загрузки мода: позиция={index+1}, папка={folder}, id={source}. {failure.Message}",failure);}
                }
            }
            using var hash=SHA256.Create();
            result.fingerprint=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(signature.ToString()))).Replace("-","");
            result.manifest.fingerprint=result.fingerprint;
            result.description=description.ToString();return result;
        }
        private static string Hash(string text)
        {using var hash=SHA256.Create();return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","");}
        private static void Apply(LoadedWeaponCatalog result,Dictionary<string,string> sources,string json,string source,WeaponOverride[] declarations)
        {
            var overrides=new Dictionary<string,string>();
            foreach(var declaration in declarations)
                if(declaration==null || string.IsNullOrWhiteSpace(declaration.id) || string.IsNullOrWhiteSpace(declaration.source) || !overrides.TryAdd(declaration.id,declaration.source))
                    throw new InvalidDataException($"Некорректное/повторное объявление weaponOverrides: мод={source}.");
            var list=StateJson.FromJson<WeaponList>(json);
            if(list?.weapons==null) throw new InvalidDataException($"Нет массива weapons: источник={source}.");
            var seen=new HashSet<string>();
            foreach(var weapon in list.weapons)
            {
                if(weapon==null || string.IsNullOrWhiteSpace(weapon.id) || !seen.Add(weapon.id))
                    throw new InvalidDataException($"Пустое/повторное определение оружия в одном пакете: источник={source}, id={weapon?.id}.");
                Validate(weapon,source);
                if(sources.TryGetValue(weapon.id,out string previous))
                {
                    if(!overrides.TryGetValue(weapon.id,out string expected) || expected!=previous)
                        throw new InvalidDataException($"Конфликт оружия {weapon.id}: мод={source}, текущий источник={previous}, объявленный источник={expected??"не указан"}. Требуется явный weaponOverrides.");
                    overrides.Remove(weapon.id);
                }
                else if(overrides.ContainsKey(weapon.id)) throw new InvalidDataException($"Замена отсутствующего оружия: мод={source}, id={weapon.id}.");
                result.definitions[weapon.id]=weapon;sources[weapon.id]=source;
            }
            foreach(var id in overrides.Keys) throw new InvalidDataException($"Объявленная замена не содержит определения: мод={source}, id={id}.");
        }
        private static void Validate(WeaponDefinition weapon,string source)
        {
            if(weapon.meleeMotion!=null || weapon.id is "spear" or "crowbar")
            {
                var keys=weapon.meleeMotion;
                if(keys==null || keys.Length<2 || keys[0]==null || keys[^1]==null || keys[0].time!=0 || keys[^1].time!=1 ||
                    !keys[0].rest || !keys[^1].rest || keys[0].angle!=0 || keys[^1].angle!=0)
                    throw new InvalidDataException($"Траектория должна начинаться и заканчиваться в покое: источник={source}, id={weapon.id}.");
                float previous=-1;
                foreach(var key in keys)
                {
                    if(key==null || !float.IsFinite(key.time) || key.time<=previous || key.time>1 ||
                        !float.IsFinite(key.angle) || !float.IsFinite(key.reach) || key.reach<0 || key.reach>1)
                        throw new InvalidDataException($"Некорректный ключ траектории: источник={source}, id={weapon.id}.");
                    previous=key.time;
                }
            }
            if(!float.IsFinite(weapon.meleeContactStart) || !float.IsFinite(weapon.meleeContactEnd) ||
                weapon.meleeContactStart<0 || weapon.meleeContactEnd>1 || weapon.meleeContactStart>=weapon.meleeContactEnd)
                throw new InvalidDataException($"Некорректное окно контакта ближнего оружия: источник={source}, id={weapon.id}.");
            foreach(float value in new[]{weapon.damage,weapon.speedDamage,weapon.maxSpeedDamage,weapon.range,weapon.cooldown,weapon.manaCost,weapon.reloadSeconds,weapon.projectileSpeed,weapon.projectileSpeedVariation,weapon.projectileLifetime})
                if(!float.IsFinite(value) || value<0) throw new InvalidDataException($"Некорректное число оружия: источник={source}, id={weapon.id}.");
            var speeds=weapon.ProjectileSpeedRange;
            if(weapon.projectileSpeedVariation>=1 || (weapon.projectileSpeedVariation>0 && weapon.projectileSpeed<=0) ||
                !float.IsFinite(speeds.y) || (weapon.projectileSpeed>0 && speeds.x<=0))
                throw new InvalidDataException($"Некорректный диапазон скорости снаряда: источник={source}, id={weapon.id}.");
            if((weapon.projectileLifetime>0 && weapon.projectileSpeed<=0) || !float.IsFinite(weapon.FlightRange(speeds.y)))
                throw new InvalidDataException($"Некорректное время или длина полёта: источник={source}, id={weapon.id}.");
            if(weapon.range<=0 || weapon.cooldown<=0 || (!string.IsNullOrEmpty(weapon.ammo) &&
                (weapon.magazineSize<=0 || weapon.ammoPerShot<=0 || weapon.ammoPerShot>weapon.magazineSize || weapon.reloadSeconds<=0)))
                throw new InvalidDataException($"Некорректная дальность, задержка или боезапас: источник={source}, id={weapon.id}.");
        }
    }
}
