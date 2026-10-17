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
        public readonly List<LoadedCosmeticPack> cosmetics=new();
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
                        string cosmeticSignature="";
                        if(manifest.cosmetics!=null)
                        {
                            var declaration=manifest.cosmetics;
                            string cosmeticData=File.ReadAllText(PackFile(directory,declaration.definitions,"cosmetics.definitions"));
                            // Pure validation; applying the pack to the startup registry happens after all manifests are read.
                            var cosmeticShape=StateJson.FromJson<CosmeticPackShape>(cosmeticData);
                            if(cosmeticShape==null || cosmeticShape.schema!=1 || cosmeticShape.slots==null || cosmeticShape.items==null)
                                throw new InvalidDataException("Некорректные данные cosmetics.definitions: нужны schema=1, slots, items.");
                            byte[] visualBytes=declaration.visuals==null?null:File.ReadAllBytes(PackFile(directory,declaration.visuals,"cosmetics.visuals"));
                            string texts=declaration.texts==null?null:File.ReadAllText(PackFile(directory,declaration.texts,"cosmetics.texts"));
                            if(texts!=null) _=new ItemTextCatalog(texts); // Pure validation before touching the live presentation catalogs.
                            cosmeticSignature="\ncosmetics-definitions:\n"+cosmeticData;
                            if(texts!=null) cosmeticSignature+="\ncosmetics-texts:\n"+texts;
                            if(visualBytes!=null) cosmeticSignature+="\ncosmetics-visuals-sha256:"+Hash(visualBytes);
                            result.cosmetics.Add(new LoadedCosmeticPack {id=source,position=index+1,data=cosmeticData,texts=texts,visuals=visualBytes});
                        }
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
                        signature.Append(cosmeticSignature);
                        if(manifest.code!=null) signature.Append("\ncode-sha256:").Append(codeHash);
                        result.manifest.packs.Add(new ContentPackRecord {id=source,version=manifest.version,
                            fingerprint=Hash(manifestJson+"\n"+data+cosmeticSignature+(manifest.code==null?"":"\ncode-sha256:"+codeHash)),dependencies=manifest.dependencies??Array.Empty<string>()});
                        description.Append('\n').Append(index+1).Append(". ").Append(source).Append(" · ").Append(manifest.version);
                        foreach(var replacement in manifest.weaponOverrides??Array.Empty<WeaponOverride>())
                            description.Append("; замена ").Append(replacement.id).Append(" из ").Append(replacement.source);
                    }
                    catch(Exception failure) when(failure is IOException || failure is InvalidDataException || failure is ArgumentException ||
                        failure is InvalidOperationException || failure is Newtonsoft.Json.JsonException)
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
        private static string Hash(byte[] bytes)
        {using var hash=SHA256.Create();return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","");}
        [Serializable] private sealed class CosmeticPackShape {public int schema;public string[] slots;public object[] items;}
        private static string PackFile(string directory,string path,string field)
        {
            if(string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path)) throw new InvalidDataException("Не задан относительный файл "+field+".");
            string absolute=Path.GetFullPath(Path.Combine(directory,path));
            if(!absolute.StartsWith(directory+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Файл "+field+" находится вне папки мода: "+path);
            return absolute;
        }
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
        internal static void Validate(WeaponDefinition weapon,string source)
        {
            WeaponRecoilProfile.ValidateForWeapon(weapon,$"источник={source}");
            bool usesHoldingArea=weapon.meleeMotion!=null || weapon.id is "spear" or "crowbar" or "grenade";
            if(usesHoldingArea) weapon.holdingArea.Validate($"источник={source}, weapon={weapon.id}");
            else if(!weapon.holdingArea.Inapplicable)
                throw new InvalidDataException($"Двумерная область удержания неприменима к огнестрельному определению: источник={source}, weapon={weapon.id}.");
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
            if(weapon.unlimitedReserve && (!string.IsNullOrEmpty(weapon.ammo) || weapon.magazineSize<=0 || weapon.ammoPerShot<=0 || weapon.ammoPerShot>weapon.magazineSize || weapon.reloadSeconds<=0))
                throw new InvalidDataException($"Бесконечный запас требует магазин без патронов: источник={source}, weapon={weapon.id}.");
            if(weapon.pellets<0 || weapon.pellets>20 || weapon.pellets>1 && weapon.projectileSpeed<=0)
                throw new InvalidDataException($"Некорректное число снарядов залпа: источник={source}, weapon={weapon.id}, pellets={weapon.pellets}. Допустимо 0–20, только для снарядов.");
            var speeds=weapon.ProjectileSpeedRange;
            if(!float.IsFinite(weapon.shotSpreadHalfAngleDegrees) || weapon.shotSpreadHalfAngleDegrees<0 ||
                weapon.shotSpreadHalfAngleDegrees>90 || (weapon.shotSpreadHalfAngleDegrees>0 && weapon.projectileSpeed<=0))
                throw new InvalidDataException($"Некорректная полуширина разброса выстрела: источник={source}, weapon={weapon.id}, degrees={weapon.shotSpreadHalfAngleDegrees}. Допустимо 0–90° только для снарядов.");
            if(!float.IsFinite(weapon.grenadeSpinRate) || weapon.grenadeSpinRate<0 ||
                !float.IsFinite(weapon.grenadeAngularRetention) || weapon.grenadeAngularRetention<0 || weapon.grenadeAngularRetention>1)
                throw new InvalidDataException($"Некорректные параметры вращения гранаты: источник={source}, id={weapon.id}.");
            if(!float.IsFinite(weapon.grenadeMaxLaunchSpeed) || weapon.grenadeMaxLaunchSpeed<0 ||
                !float.IsFinite(weapon.grenadeFullEffortDistance) || weapon.grenadeFullEffortDistance<0 ||
                !float.IsFinite(weapon.grenadeAimLift) || weapon.grenadeAimLift<0 || weapon.grenadeAimLift>1 ||
                (weapon.id=="grenade" && (weapon.grenadeMaxLaunchSpeed<=0 || weapon.grenadeFullEffortDistance<=0)))
                throw new InvalidDataException($"Некорректные параметры броска гранаты: источник={source}, id={weapon.id}.");
            if(!float.IsFinite(weapon.grenadeFuseSeconds) || weapon.grenadeFuseSeconds<0 ||
                !float.IsFinite(weapon.grenadeGravity) || weapon.grenadeGravity<0 ||
                !float.IsFinite(weapon.grenadeRestitution) || weapon.grenadeRestitution<0 || weapon.grenadeRestitution>1 ||
                !float.IsFinite(weapon.grenadeGroundRetention) || weapon.grenadeGroundRetention<0 || weapon.grenadeGroundRetention>1 ||
                (weapon.id=="grenade" && (weapon.grenadeFuseSeconds<=0 || weapon.grenadeGravity<=0)))
                throw new InvalidDataException($"Некорректный запал или отскок гранаты: источник={source}, id={weapon.id}.");
            if(weapon.projectileSpeedVariation>=1 || (weapon.projectileSpeedVariation>0 && weapon.projectileSpeed<=0) ||
                !float.IsFinite(speeds.y) || (weapon.projectileSpeed>0 && speeds.x<=0))
                throw new InvalidDataException($"Некорректный диапазон скорости снаряда: источник={source}, id={weapon.id}.");
            if((weapon.projectileLifetime>0 && weapon.projectileSpeed<=0) || !float.IsFinite(weapon.FlightRange(speeds.y)))
                throw new InvalidDataException($"Некорректное время или длина полёта: источник={source}, id={weapon.id}.");
            if(weapon.mine!=null && weapon.mine.Configured)
            {
                var mine=weapon.mine;
                if(!weapon.explosive || !float.IsFinite(mine.sensitivity) || !float.IsFinite(mine.delaySeconds) || mine.delaySeconds<=0 ||
                    !float.IsFinite(mine.visibility) || mine.visibility<=0 || !float.IsFinite(mine.width) || mine.width<=0 ||
                    !float.IsFinite(mine.height) || mine.height<=0 || !float.IsFinite(mine.mass) || mine.mass<=0 || string.IsNullOrEmpty(mine.artwork) || string.IsNullOrEmpty(mine.beepSound) ||
                    string.IsNullOrEmpty(mine.defuseSound))
                    throw new InvalidDataException($"Некорректный профиль мины: источник={source}, id={weapon.id}. Нужны взрывное оружие, положительные зона/задержка/видимость/размер, графика и звуки.");
            }
            if(weapon.range<=0 || weapon.cooldown<=0 || (!string.IsNullOrEmpty(weapon.ammo) &&
                (weapon.magazineSize<=0 || weapon.ammoPerShot<=0 || weapon.ammoPerShot>weapon.magazineSize || weapon.reloadSeconds<=0)))
                throw new InvalidDataException($"Некорректная дальность, задержка или боезапас: источник={source}, id={weapon.id}.");
        }
    }
}
