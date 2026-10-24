using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using Remains.Presentation;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class ItemLandingAudioChecks
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        [Serializable] private sealed class ResourceEvidence
        {public string item,sourceSound,sourceFile,importedFile,sha256;public int channels,sampleRate;public double seconds;}
        [Serializable] private sealed class Manifest {public int schema;public ResourceEvidence[] resources;}
        public static void Run()
        {
            var catalog=ItemLandingAudioCatalog.Load();
            foreach(var assignment in new[]{("crowbar","crowbar"),("caps","caps"),("heal","heal"),("p10","ammo"),("batt","ammo")})
            {
                string item=assignment.Item1;
                Require(catalog.TryGet(item,out var clip,out var reason) && reason==null &&
                    clip==GameAssets.Load<AudioClip>("Original/Audio/ItemLandings/"+assignment.Item2),"Неверный WAV: "+item);
                foreach(int fps in new[]{30,60,144}) CheckConsumer(catalog,item,clip,fps);
            }
            Require(!catalog.TryGet("spear",out var unresolved,out var detail) && unresolved==null && detail.Contains("falll_item"),
                "Неоднозначный исходный ключ копья получил неподтверждённую подстановку");
            Require(!catalog.TryGet("mod:missing",out var unknown,out var missing) && unknown==null && missing.Contains("action=landing"),
                "Неизвестный предмет получил универсальный звук");
            var settings=JsonUtility.FromJson<ItemLandingAudioCatalog.Settings>(GameAssets.Load<TextAsset>(ItemLandingAudioCatalog.Resource).text);
            int assetLoads=0;
            var cached=ItemLandingAudioCatalog.Create(settings,path=>{assetLoads++;return GameAssets.Load<AudioClip>(path);});
            Require(assetLoads==4,"Общий банк двух ammo был загружен повторно");
            settings.entries[0].clip="missing-after-build";
            Require(cached.TryGet("crowbar",out var retained,out _) && retained==GameAssets.Load<AudioClip>("Original/Audio/ItemLandings/crowbar"),
                "Mutable declaration изменила уже подготовленный каталог");
            CheckDeclarationFailures();CheckResources();
            Debug.Log("Звуки приземления личных вещей: PASS — 5 типов/4 исходных WAV, actual AudioSource, один физический контакт при30/60/144FPS; удержание/пауза/загрузка/дубликаты/поколение, без изменения инвентаря; spear явно не сопоставлен.");
        }
        private static void CheckConsumer(ItemLandingAudioCatalog catalog,string item,AudioClip expected,int fps)
        {
            var root=new GameObject("Проверка приземления личной вещи");
            SimulationGeometry geometry=null;
            try
            {
                var session=root.AddComponent<CoopSession>();
                var world=new WorldState();
                var actor=new ActorState {id="listener",player=true,connected=true,level="hub"};world.actors.Add(actor);
                var drop=DroppedItemTransfer.Preview(actor,"audio-check",item,new Vector2(1,2),Vector2.zero);
                drop.items.Add(new ItemStack {id="item",definition=item,count=3});world.containers.Add(drop);
                string inventoryBefore=StateJson.ToJson(drop.items),usesBefore=StateJson.ToJson(actor.inventoryUses);
                typeof(CoopSession).GetProperty("World").SetValue(session,world);
                typeof(CoopSession).GetProperty("LocalActorId").SetValue(session,actor.id);
                var room=new RoomLayout {id="audio-floor",left=-10,right=10,bottom=0,top=10,ladders=Array.Empty<RoomLadder>(),
                    platforms=new[]{new RoomSurface {x=-10,y=-1,width=20,height=1}}};
                geometry=new SimulationGeometry(_=>room);geometry.Reconcile(world);
                var playback=root.AddComponent<GameplayAudio>();
                playback.Initialize(session,new WeaponReloadController(session),root.AddComponent<Camera>());
                var contact=root.AddComponent<PropContactAudio>();contact.Initialize(session,geometry,playback,catalog);
                var reconcile=typeof(PropContactAudio).GetMethod("Reconcile",Private);
                var step=typeof(PropContactAudio).GetMethod("LateUpdate",Private);
                reconcile.Invoke(contact,null);step.Invoke(contact,null);
                Require(contact.Landings==0,"Первичная загрузка создала удар");
                Require(contact.TryGetLandingClip(new PropState {id="box",definition="mcrate1"},out var box,out _) &&
                    box==GameAssets.Load<AudioClip>("Original/Audio/crate-metal-fall"),"Личный каталог заменил банк обычного ящика");
                Require(!contact.TryGetLandingClip(new ContainerState {id="bad-drop",definition="loose-item"},out var bad,out var badReason) &&
                    bad==null && badReason.Contains("presentationItem"),"Сломанный drop получил банк по внешнему типу");
                int callbacks=0;
                contact.LandingPlayed+=(id,definition,clip)=>
                {Require(id==drop.id && definition==item && clip==expected,"Свидетельство потеряло настоящий item/prop/clip");callbacks++;};
                var neighbours=new PropState[]{drop};
                void Advance()
                {
                    for(int i=0;i<fps*3;i++)
                    {PropMotion.Step(drop,room,1f/fps,null,neighbours);step.Invoke(contact,null);}
                }
                Advance();
                Require(contact.Landings==1 && callbacks==1,"Падение/повторные неподвижные кадры дали иной счётчик");
                var voices=root.GetComponentsInChildren<AudioSource>();
                Require(voices.Length==1 && voices[0].clip==expected && voices[0].volume>0 &&
                    (!Application.isBatchMode || voices[0].mute),"Не подтверждён actual AudioSource исходного WAV");
                // Loading an already supported object establishes a fresh baseline, never replays its landing.
                var restored=new PropContactObservation();
                Require(restored.Sample(StateJson.Copy(drop),room,neighbours,false,3)==MotionContact.None,"Загрузка сохранившегося предмета повторила звук");
                drop.y=2;drop.vx=drop.vy=0;drop.held=true;drop.generation++;
                step.Invoke(contact,null);Advance();
                Require(contact.Landings==1,"Удерживаемая вещь озвучила контакт");
                drop.y=2;drop.vx=drop.vy=0;drop.held=false;drop.generation++;
                step.Invoke(contact,null);Require(contact.Landings==1,"Release/смена владельца создали удар до падения");
                Advance();Require(contact.Landings==2 && callbacks==2,"После release потеряно действительное приземление");
                var paused=(HashSet<string>)typeof(SessionPause).GetField("pausedLevels",Private).GetValue(session.Pause);
                paused.Add("hub");drop.y=2;drop.vx=drop.vy=0;step.Invoke(contact,null);Advance();
                Require(contact.Landings==2,"Подавленный контакт во время паузы прозвучал");
                paused.Remove("hub");step.Invoke(contact,null);
                Require(contact.Landings==2,"Выход из паузы воспроизвёл отложенное падение");
                drop.generation++;reconcile.Invoke(contact,null);step.Invoke(contact,null);
                Require(contact.Landings==2,"Неподвижный handoff повторил удар");
                actor.level="factory:0";reconcile.Invoke(contact,null);actor.level="hub";reconcile.Invoke(contact,null);step.Invoke(contact,null);
                Require(contact.Landings==2,"Возврат слушателя повторил удар");
                Require(StateJson.ToJson(drop.items)==inventoryBefore && StateJson.ToJson(actor.inventoryUses)==usesBefore &&
                    drop.sequence==0 && world.completedTransactions.Count==0,"Звуковое наблюдение изменило предмет/журнал");
            }
            finally {geometry?.Dispose();UnityEngine.Object.DestroyImmediate(root);}
        }
        private static ItemLandingAudioCatalog.Entry Entry(string item="caps",string clip="test")=>new()
        {item=item,action="landing",sourceItem="money",sourceSound="fall_caps",clip=clip};
        private static void CheckDeclarationFailures()
        {
            int loads=0;
            AudioClip Load(string _){loads++;return null;}
            foreach(var settings in new[]{
                new ItemLandingAudioCatalog.Settings {schema=2,entries=Array.Empty<ItemLandingAudioCatalog.Entry>()},
                new ItemLandingAudioCatalog.Settings {schema=1,entries=new[]{Entry(),Entry()}},
                new ItemLandingAudioCatalog.Settings {schema=1,entries=new[]{Entry(),new ItemLandingAudioCatalog.Entry {item="other",action="step",sourceItem="money",sourceSound="fall_caps",clip="test"}}},
                new ItemLandingAudioCatalog.Settings {schema=1,entries=new[]{Entry(),new ItemLandingAudioCatalog.Entry {item="other",action="landing",sourceItem="money",sourceSound="fall_caps",clip="test",unresolvedReason="не подтверждено"}}}})
                Reject(()=>ItemLandingAudioCatalog.Create(settings,Load));
            Require(loads==0,"Невалидная поздняя декларация вызвала частичную загрузку assets");
            Reject(()=>ItemLandingAudioCatalog.Create(new ItemLandingAudioCatalog.Settings {schema=1,entries=new[]{Entry()}},Load));
            Require(loads==1,"Отсутствующий declared WAV не отклонил каталог");
        }
        private static void CheckResources()
        {
            string project=Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
            var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(Path.Combine(project,"Tools/Research/ItemLandingAudio/manifest.json")));
            Require(manifest.schema==1 && manifest.resources?.Length==4,"Нет source manifest четырёх WAV");
            foreach(var entry in manifest.resources)
            {
                string path=Path.Combine(project,entry.importedFile);
                Require(Hash(File.ReadAllBytes(path))==entry.sha256,"Изменён исходный WAV: "+entry.item);
                string source=Path.Combine(project,entry.sourceFile);
                if(File.Exists(source)) Require(Hash(File.ReadAllBytes(source))==entry.sha256,"WAV не совпадает с доступным source: "+entry.item);
                string assetPath=entry.importedFile.Substring("RemainsUnity/".Length);
                var importer=(AudioImporter)AssetImporter.GetAtPath(assetPath);
                Require(importer!=null && !importer.forceToMono && !importer.loadInBackground &&
                    importer.defaultSampleSettings.compressionFormat==AudioCompressionFormat.PCM &&
                    importer.defaultSampleSettings.sampleRateSetting==AudioSampleRateSetting.PreserveSampleRate,
                    "Потеря native PCM/каналов/частоты при импорте: "+entry.item);
                var clip=AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
                Require(clip!=null && clip.channels==entry.channels && clip.frequency==entry.sampleRate &&
                    Math.Abs((double)clip.samples/clip.frequency-entry.seconds)<.00001,"Изменены native WAV dimensions: "+entry.item);
            }
        }
        private static string Hash(byte[] bytes)
        {using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        private static void Reject(Action action)
        {try {action();}catch(InvalidOperationException){return;}throw new InvalidOperationException("Каталог принял неподтверждённые данные.");}
        private static void Require(bool condition,string detail)
        {if(!condition) throw new InvalidOperationException("Звук приземления личной вещи: "+detail);}
    }
}
