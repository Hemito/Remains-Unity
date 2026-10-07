using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Domain;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class ImpactAudioCoverageChecks
    {
        // Resource/material declarations, not a rule inferred from texture names.
        internal static readonly (string key,string clip,float gain,int count)[] AddedSurfaces={
            ("TrainingRoom/steel","hit-metal",.4f,46),
            ("Factory46/16_tMetal","hit-metal",.4f,13),
            ("Factory46/41_tRock","hit-concrete",.5f,3),
            ("Factory18/8_tWood","hit-wood",.5f,3),
            ("Factory53/16_tMetal","hit-metal",.4f,34),
            ("Factory53/23_tCracked","hit-concrete",.5f,9),
            ("Factory53/24_tConcreteD","hit-concrete",.5f,4),
            ("Factory53/26_tConcrete","hit-concrete",.5f,46),
            ("Factory40/24_tConcreteD","hit-concrete",.5f,5),
            ("Factory40/26_tConcrete","hit-concrete",.5f,31),
            ("Factory40/8_tWood","hit-wood",.5f,3)};
        private static readonly string[] ModulePaths={
            "Assets/Remains/Resources/Levels/hub.prefab",
            "Assets/Remains/RoomDrafts/FactoryEntry46.prefab",
            "Assets/Remains/RoomDrafts/FactoryWorkshop18.prefab",
            "Assets/Remains/RoomDrafts/FactoryOffice53.prefab",
            "Assets/Remains/RoomDrafts/FactoryWarehouse40.prefab"};
        private static readonly string[] MeleeWeapons={"crowbar","spear"};
        private readonly struct Sound
        {
            public readonly string Clip;
            public readonly float Gain;
            public Sound(JToken entry)
            {
                Clip=(string)entry["clip"];Gain=(float?)entry["gain"]??float.NaN;
                Require(!string.IsNullOrEmpty(Clip) && float.IsFinite(Gain) && Gain>0 && Gain<=1,
                    "Некорректное назначение: "+entry);
                Require(Resources.Load<AudioClip>(Clip)?.samples>0,"Нет импортированного WAV: "+Clip);
            }
        }
        public static void Run()
        {
            var projectile=ReadProjectile();var melee=ReadMelee();
            foreach(var assignment in AddedSurfaces)
            {
                CheckAssignment(projectile,"surface:"+assignment.key,assignment.clip,assignment.gain);
                foreach(var weapon in MeleeWeapons)
                    CheckAssignment(melee,weapon+":surface:"+assignment.key,assignment.clip,assignment.gain);
            }
            var restoredCounts=AddedSurfaces.ToDictionary(a=>a.key,_=>0,StringComparer.Ordinal);
            int solids=0,passing=0,doors=0;
            var paths=new HashSet<string>(ModulePaths,StringComparer.Ordinal);
            paths.Add("Assets/Remains/RoomDrafts/ConnectedTrainingHub.prefab");
            foreach(var entry in RoomCatalogAsset.Load().Entries)
            {
                string path=AssetDatabase.GetAssetPath(entry.room);
                Require(!string.IsNullOrEmpty(path),"Комната каталога не сохранена: "+entry.id);
                paths.Add(path);
            }
            foreach(string path in paths.OrderBy(p=>p,StringComparer.Ordinal))
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var room=prefab==null?null:prefab.GetComponent<RoomAuthoring>();
                Require(room!=null,"Нет сохранённой авторской комнаты: "+path);
                var layout=room.CreateLayout(false);
                var authored=room.GetComponentsInChildren<RoomSurfaceAuthoring>(true).Where(room.Included).ToArray();
                Require(authored.Length==layout.platforms.Length,"Данные поверхности не совпадают с layout: "+path);
                foreach(var component in authored)
                {
                    var surface=component.Read(room);
                    string context=path+"/"+component.name+", bounds="+new Rect(surface.x,surface.y,surface.width,surface.height);
                    CheckSurface(surface,context,projectile,melee);
                    if(surface.shotsPassThrough) {passing++;continue;}
                    solids++;
                    if(ModulePaths.Contains(path) && restoredCounts.ContainsKey(surface.texture)) restoredCounts[surface.texture]++;
                }
                var doorLayout=RoomDoorContent.Read(room,"impact-audio-coverage");
                if(doorLayout!=null) foreach(var door in doorLayout.doors)
                {
                    CheckSurface(new RoomSurface {texture=door.surfaceTexture},
                        path+"/дверь="+door.id+", definition="+door.definition,projectile,melee);
                    doors++;
                }
            }
            foreach(var assignment in AddedSurfaces)
                Require(restoredCounts[assignment.key]==assignment.count,
                    $"Изменилось проверяемое source-размещение {assignment.key}: {restoredCounts[assignment.key]}, ожидалось {assignment.count}.");
            Require(restoredCounts.Values.Sum()==197,"Не проверены все 197 ранее пропущенных solid declarations.");
            foreach(var asset in MechanismCatalogAsset.Load().Platforms)
            {
                var type=asset.CreateDefinition();
                CheckSurface(new RoomSurface {texture=type.surfaceTexture,oneWay=type.oneWay,shotsPassThrough=type.shotsPassThrough},
                    "Платформа каталога "+type.id,projectile,melee);
            }
            PropCatalog.Load();int props=0,withoutBody=0;
            foreach(var type in PropCatalog.All)
            {
                // Same eligibility as CombatController's world-object contact producer.
                if(!type.hasPhysicalBody) {withoutBody++;continue;}
                props++;
                foreach(var weapon in MeleeWeapons) RequireBinding(melee,weapon+":prop:"+type.id,"PropCatalog "+type.id);
            }
            foreach(var weapon in MeleeWeapons)
            {
                CheckAssignment(melee,weapon+":prop:filecab","hit-metal",.4f);
                CheckAssignment(melee,weapon+":prop:case","hit-metal",.4f);
            }
            CheckMissingKey(projectile,melee);
            Debug.Log($"Назначения ударных звуков: PASS — {paths.Count} сохранённых комнат, {solids} ударяемых/{passing} проходимых поверхностей, {doors} дверей, 197 восстановленных declarations, {props} физических типов/{withoutBody} без тела; отрицательные missing-key и policy checks.");
        }
        private static Dictionary<string,Sound> ReadProjectile()
        {
            var root=Read("Presentation/projectile-audio");
            var weapons=(JArray)root["weapons"];
            Require(weapons!=null && weapons.Values<string>().Contains("pistol") && weapons.Values<string>().Contains("npc-shot"),
                "Каталог снарядов не охватывает текущие ударяемые виды оружия.");
            var result=new Dictionary<string,Sound>(StringComparer.Ordinal);
            foreach(var entry in (JArray)root["entries"])
                Add(result,(string)entry["kind"]+":"+(string)entry["key"],new Sound(entry));
            return result;
        }
        private static Dictionary<string,Sound> ReadMelee()
        {
            var result=new Dictionary<string,Sound>(StringComparer.Ordinal);
            foreach(var entry in (JArray)Read("Presentation/melee-impact-audio")["entries"])
            {
                var sound=new Sound(entry);string weapon=(string)entry["weapon"];
                foreach(string kind in new[]{"actors","surfaces","props"})
                    foreach(var key in entry[kind] as JArray??new JArray())
                        Add(result,weapon+":"+kind.Substring(0,kind.Length-1)+":"+(string)key,sound);
            }
            return result;
        }
        private static JObject Read(string path)
        {
            var asset=Resources.Load<TextAsset>(path);Require(asset!=null,"Нет каталога "+path);
            var value=JObject.Parse(asset.text);Require(value["entries"] is JArray,"Нет entries: "+path);return value;
        }
        private static void Add(Dictionary<string,Sound> values,string key,Sound sound)
        {Require(!values.ContainsKey(key),"Повтор назначения: "+key);values.Add(key,sound);}
        private static void CheckSurface(RoomSurface surface,string context,Dictionary<string,Sound> projectile,Dictionary<string,Sound> melee)
        {
            if(surface.shotsPassThrough) return;
            RequireBinding(projectile,"surface:"+surface.texture,context);
            foreach(string weapon in MeleeWeapons) RequireBinding(melee,weapon+":surface:"+surface.texture,context);
        }
        private static void RequireBinding(Dictionary<string,Sound> values,string key,string context)
        {Require(values.ContainsKey(key),"Нет назначения key="+key+", источник="+context);}
        private static void CheckAssignment(Dictionary<string,Sound> values,string key,string clip,float gain)
        {
            RequireBinding(values,key,"Подтверждённая декларация материала/ресурса");var sound=values[key];
            Require(sound.Clip=="Original/Audio/"+clip && Mathf.Abs(sound.Gain-gain)<.00001f,"Искажено назначение материала: "+key);
        }
        private static void CheckMissingKey(Dictionary<string,Sound> projectile,Dictionary<string,Sound> melee)
        {
            var copy=new Dictionary<string,Sound>(projectile,StringComparer.Ordinal);
            copy.Remove("surface:Factory53/26_tConcrete");
            ExpectMissing(()=>CheckSurface(new RoomSurface {texture="Factory53/26_tConcrete"},"negative office binding",copy,melee),
                "surface:Factory53/26_tConcrete","negative office binding");
            var propCopy=new Dictionary<string,Sound>(melee,StringComparer.Ordinal);propCopy.Remove("spear:prop:filecab");
            ExpectMissing(()=>RequireBinding(propCopy,"spear:prop:filecab","negative catalog binding"),"spear:prop:filecab","negative catalog binding");
            var surface=new RoomSurface {texture="negative-unassigned",x=0,y=0,width=1,height=1,oneWay=true,shotsPassThrough=true};
            var room=new RoomLayout {platforms=new[]{surface}};
            Require(!ShotObstacles.FirstContact(room,new Vector2(-1,.5f),new Vector2(2,.5f)).HasValue,"Проходимая поверхность дала контакт.");
            CheckSurface(surface,"passing policy",projectile,melee);
            surface.shotsPassThrough=false;
            Require(ShotObstacles.FirstContact(room,new Vector2(-1,.5f),new Vector2(2,.5f)).HasValue,"oneWay скрыл ударяемую поверхность.");
            ExpectMissing(()=>CheckSurface(surface,"oneWay impact policy",projectile,melee),"negative-unassigned","oneWay impact policy");
        }
        private static void ExpectMissing(Action action,string key,string context)
        {
            try {action();} catch(InvalidOperationException error)
            {Require(error.Message.Contains(key) && error.Message.Contains(context),"Missing-key потерял контекст: "+error.Message);return;}
            throw new InvalidOperationException("Пропущенное аудионазначение прошло проверку: "+key);
        }
        private static void Require(bool valid,string reason)
        {if(!valid) throw new InvalidOperationException("Покрытие ударных звуков: "+reason);}
    }
}
