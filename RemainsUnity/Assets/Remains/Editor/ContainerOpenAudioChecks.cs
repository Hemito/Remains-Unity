using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using Remains.Presentation;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Remains.Editor
{
    public static class ContainerOpenAudioChecks
    {
        private static readonly string[] ModulePaths={
            "Assets/Remains/Resources/Levels/hub.prefab",
            "Assets/Remains/RoomDrafts/ConnectedTrainingHub.prefab",
            "Assets/Remains/RoomDrafts/FactoryEntry46.prefab",
            "Assets/Remains/RoomDrafts/FactoryWorkshop18.prefab",
            "Assets/Remains/RoomDrafts/FactoryOffice53.prefab",
            "Assets/Remains/RoomDrafts/FactoryWarehouse40.prefab",
            "Assets/Remains/RoomDrafts/FactoryContinuation27.prefab"};
        public static void Run()
        {
            ContainerOpenAudioAuthoring.Check();
            var catalog=ContainerOpenAudio.Load();
            var declarations=ContainerOpenAudioAuthoring.ReadManifest().declarations;
            var authored=ReadAuthoredContainers();
            CheckCoverage(catalog,declarations,authored);
            var missing=new ContainerOpenAudio.Catalog {version=1,entries=catalog.entries.Where(e=>e.definition!="ammobox").ToArray()};
            string missingContext=null;
            try {CheckCoverage(missing,declarations,authored);}catch(InvalidOperationException error) {missingContext=error.Message;}
            Require(missingContext!=null && missingContext.Contains("type=ammobox") && missingContext.Contains("source=") &&
                missingContext.Contains(".prefab"),"Неполный actual saved coverage не назвал source/type/path");
            foreach(int fault in Enumerable.Range(0,6))
            {
                var invalid=new ContainerOpenAudio.Catalog {version=1,entries=new[]{new ContainerOpenAudio.Entry {definition="safe",clip="bank",gain=.5f}}};
                if(fault==0)invalid.version=0;
                if(fault==1)invalid.entries=null;
                if(fault==2)invalid.entries[0].definition="";
                if(fault==3)invalid.entries[0].clip="";
                if(fault==4)invalid.entries[0].gain=float.NaN;
                if(fault==5)invalid.entries=new[]{invalid.entries[0],invalid.entries[0]};
                bool refused=false;try {ContainerOpenAudio.Validate(invalid);}catch(InvalidOperationException){refused=true;}
                Require(refused,"Некорректный модовый звуковой каталог не отклонён: case="+fault);
            }
            CheckHud(catalog);
            int covered=authored.Count(value=>declarations.Any(e=>e.definition==value.container.definition && e.sourceOpen!=null));
            Debug.Log($"Открывание контейнеров: PASS — {catalog.entries.Length} source-confirmed типов/4 банка, {covered} saved placements с явным open; actual HUD success→один AudioSource, Refresh/реплика без повтора, новый open после close, отказы без голоса, source/PCM/native importer и missing-key negative. Original runtime/mix не проверены.");
        }
        private static List<(string path,ContainerState container)> ReadAuthoredContainers()
        {
            var paths=new HashSet<string>(ModulePaths,StringComparer.Ordinal);
            foreach(var entry in RoomCatalogAsset.Load().Entries) paths.Add(AssetDatabase.GetAssetPath(entry.room));
            var result=new List<(string,ContainerState)>();
            foreach(string path in paths.OrderBy(p=>p,StringComparer.Ordinal))
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var room=prefab==null?null:prefab.GetComponent<RoomAuthoring>();
                Require(room!=null,"Нет сохранённой авторской комнаты: "+path);
                var content=RoomPhysicalContent.Read(room,"container-open-coverage");
                if(content!=null) foreach(var container in content.containers) result.Add((path,container));
            }
            Require(result.Count>0,"Нет actual saved контейнеров для coverage");return result;
        }
        private static void CheckCoverage(ContainerOpenAudio.Catalog catalog,ContainerOpenAudioAuthoring.SourceDeclaration[] declarations,
            List<(string path,ContainerState container)> authored)
        {
            ContainerOpenAudio.Validate(catalog);
            var source=declarations.ToDictionary(e=>e.definition,StringComparer.Ordinal);
            var entries=catalog.entries.ToDictionary(e=>e.definition,StringComparer.Ordinal);
            foreach(var item in authored)
            {
                string type=item.container.definition,context=$"source={item.container.id}, type={type}, action=open, path={item.path}";
                Require(source.TryGetValue(type,out var declaration),"Не исследовано назначение actual saved container: "+context);
                if(declaration.sourceOpen==null)
                {
                    Require(!entries.ContainsKey(type),"Тип без исходного open получил вымышленный банк: "+context);continue;
                }
                Require(entries.TryGetValue(type,out var sound) && sound.clip==declaration.clip && sound.gain==.5f,
                    "Нет/изменён подтверждённый source open: "+context+", sourceSound="+declaration.sourceOpen);
            }
            foreach(var sound in catalog.entries)
            {
                Require(source.TryGetValue(sound.definition,out var declaration) && declaration.sourceOpen!=null && declaration.clip==sound.clip,
                    "Каталог содержит неподтверждённое source open: type="+sound.definition);
                Require(authored.Any(e=>e.container.definition==sound.definition),"Банк не имеет actual saved container binding: "+sound.definition);
                var clip=Resources.Load<AudioClip>(sound.clip);
                Require(clip!=null && clip.samples>0 && clip.frequency>0 && clip.channels>0,"Нет импортированного банка: "+sound.clip);
            }
            foreach(string type in new[]{"safe","locker","cup","wallcab"})
                Require(entries[type].clip==source[type].clip && entries[type].gain==.5f,"Изменён прежний действующий банк: "+type);
        }
        private static void CheckHud(ContainerOpenAudio.Catalog catalog)
        {
            Require(!Mirror.NetworkClient.active && !Mirror.NetworkServer.active,"HUD Editor fixture требует отключённую сеть");
            var owner=new GameObject("Неактивная сессия проверки звука контейнера");owner.SetActive(false);
            var hudObject=new GameObject("Настоящий HUD проверки открывания",typeof(RectTransform));
            var audioObject=new GameObject("Настоящие голоса открывания");
            var cameraObject=new GameObject("Камера проверки открывания",typeof(Camera));
            var previousEvents=new HashSet<EventSystem>(UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include,FindObjectsSortMode.None));
            PrototypeHud hud=null;
            try
            {
                var session=owner.AddComponent<CoopSession>();session.ConfigureRules(WeaponCatalog.Rules);
                var actor=new ActorState {id="container-open-listener",owner="fixture",character="Проверка открывания",level="hub",player=true,connected=true};
                PlayerStarterInventory.Give(actor);
                var world=new WorldState();world.actors.Add(actor);
                foreach(var entry in catalog.entries)
                    world.containers.Add(new ContainerState {id="open:"+entry.definition,definition=entry.definition,level=actor.level,x=.8f,y=0,
                        items=new List<ItemStack>{new() {id=entry.definition+":actual-cell",definition="caps",count=3}}});
                typeof(CoopSession).GetProperty(nameof(CoopSession.World)).SetValue(session,world);
                typeof(CoopSession).GetProperty(nameof(CoopSession.LocalActorId)).SetValue(session,actor.id);
                typeof(CoopSession).GetProperty(nameof(CoopSession.LocalOwner)).SetValue(session,actor.owner);
                var playback=audioObject.AddComponent<GameplayAudio>();
                playback.Initialize(session,new WeaponReloadController(session),cameraObject.GetComponent<Camera>());
                var audio=audioObject.AddComponent<ContainerOpenAudio>();audio.Initialize(playback);
                hud=hudObject.AddComponent<PrototypeHud>();hud.Initialize(session,worldCamera:cameraObject.GetComponent<Camera>());
                hud.ContainerOpened+=audio.Play;
                int callbacks=0;string observedId=null,observedType=null;AudioClip observedClip=null;
                audio.Opened+=(id,type,clip)=>{callbacks++;observedId=id;observedType=type;observedClip=clip;};
                var refresh=typeof(PrototypeHud).GetMethod("Refresh",BindingFlags.Instance|BindingFlags.NonPublic);
                string before=StateJson.ToJson(world);
                foreach(var entry in catalog.entries)
                {
                    int previous=audio.Openings;
                    var voiceIds=new HashSet<int>(audioObject.GetComponentsInChildren<AudioSource>().Select(s=>s.GetInstanceID()));
                    hud.OpenContainer("open:"+entry.definition);
                    Require(hud.ContainerOpen && audio.Openings==previous+1 && callbacks==audio.Openings && observedId=="open:"+entry.definition &&
                        observedType==entry.definition && audio.TryGet(entry.definition,out var clip,out var gain) && observedClip==clip,
                        "Настоящий успешный HUD open не создал ровно один source-bound voice: "+entry.definition);
                    var created=audioObject.GetComponentsInChildren<AudioSource>().Where(s=>!voiceIds.Contains(s.GetInstanceID())).ToArray();
                    Require(created.Length==1 && created[0].clip==observedClip && created[0].volume>0 &&
                        (!Application.isBatchMode || created[0].mute),"Не подтверждён actual AudioSource исходного банка: "+entry.definition);
                    // Re-rendering and a replacement world view are not new E opens.
                    refresh.Invoke(hud,null);refresh.Invoke(hud,null);
                    var replacement=StateJson.Copy(world);
                    typeof(CoopSession).GetProperty(nameof(CoopSession.World)).SetValue(session,replacement);refresh.Invoke(hud,null);
                    Require(hud.ContainerOpen && audio.Openings==previous+1 && audioObject.GetComponentsInChildren<AudioSource>().Length==voiceIds.Count+1,
                        "Refresh/реплика повторили voice: "+entry.definition);
                    typeof(CoopSession).GetProperty(nameof(CoopSession.World)).SetValue(session,world);hud.CloseContainer();
                }
                Require(StateJson.ToJson(world)==before,"HUD/звук изменил HP/инвентарь/appearance/журнал мира");
                int completed=audio.Openings;
                hud.SetRoomPresentationPending(true);hud.OpenContainer("open:ammobox");
                Require(!hud.ContainerOpen && audio.Openings==completed,"Pending HUD открыл контейнер/voice");hud.SetRoomPresentationPending(false);
                actor.down=true;hud.OpenContainer("open:ammobox");
                Require(!hud.ContainerOpen && audio.Openings==completed,"Down HUD открыл контейнер/voice");actor.down=false;
                actor.x=100;hud.OpenContainer("open:ammobox");
                Require(!hud.ContainerOpen && audio.Openings==completed,"Отказ реального ContainerAccess создал voice");actor.x=0;
                hud.OpenContainer("absent-source");
                Require(!hud.ContainerOpen && audio.Openings==completed,"Неизвестный source создал voice");
                var loose=new ContainerState {id="loose",definition="loose-item",presentationItem="caps",level="hub",x=.8f};world.containers.Add(loose);
                hud.OpenContainer(loose.id);audio.Play(loose);audio.Play(InventorySource.FromBody(actor));audio.Play(null);
                Require(!hud.ContainerOpen && audio.Openings==completed,"Body/loose item получил container open voice");world.containers.Remove(loose);
                var paused=(HashSet<string>)typeof(SessionPause).GetField("pausedLevels",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(session.Pause);
                paused.Add(actor.level);audio.Play(world.containers[0]);paused.Remove(actor.level);
                actor.level="factory:0";audio.Play(world.containers[0]);actor.level="hub";
                Require(audio.Openings==completed,"Пауза/иной уровень создали или отложили voice");
                hud.OpenContainer("open:ammobox");
                Require(hud.ContainerOpen && audio.Openings==completed+1,"Новое реальное открытие после close подавлено как replay");hud.CloseContainer();
                Require(StateJson.ToJson(world)==before,"Проверка отказов/повтора изменила world graph");
            }
            finally
            {
                hud?.Dispose();UnityEngine.Object.DestroyImmediate(hudObject);UnityEngine.Object.DestroyImmediate(audioObject);
                UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(owner);
                foreach(var events in UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                    if(!previousEvents.Contains(events)) UnityEngine.Object.DestroyImmediate(events.gameObject);
            }
        }
        private static void Require(bool condition,string reason)
        {if(!condition)throw new InvalidOperationException("Звуки контейнеров: "+reason);}
    }
}
