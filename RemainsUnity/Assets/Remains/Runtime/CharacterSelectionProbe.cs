using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Mirror;
using Remains.Content;
using Remains.Domain;
using Remains.Persistence;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Runtime
{
    // Isolated native menu regression. All decisions use Unity's virtual pointer;
    // the saved profiles/world are declared initial fixtures, never a live actor override.
    public sealed class CharacterSelectionProbe : MonoBehaviour
    {
        private const string First="pony-one",Second="pony-two";
        private PrototypeRuntime runtime;
        private MainMenuView menu;
        private BackgroundSaveOperation preparation;
        private CharacterLibrary library;
        private ActorState expected;
        private RecoverySnapshot selectedWorld;
        private readonly List<FileProof> firstFiles=new();
        private readonly Evidence evidence=new();
        private string firstDirectory,secondDirectory,secondSource;
        private float deadline;
        private bool finished,queueArmed;
        private IDisposable queueLease;

        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+110;
            try
            {
                Require(value.Options.Smoke=="characters-selection" && value.Options.Role=="menu" && value.Options.Character==First,
                    "Пробе нужны --smoke characters-selection --role menu --character pony-one.");
                Require(!string.IsNullOrEmpty(value.Options.SaveDirectory) && !string.IsNullOrEmpty(value.Options.Report),
                    "Нужны отдельные --save-dir библиотеки и --report.");
                string root=Path.GetFullPath(value.Options.SaveDirectory);
                evidence.libraryRoot=root;
                library=new CharacterLibrary(Path.Combine(root,"Profiles"));
                firstDirectory=library.ProfilePath(First);secondDirectory=library.ProfilePath(Second);
                Require(!HasProfileFiles(firstDirectory) && !HasProfileFiles(secondDirectory) &&
                    !File.Exists(Path.Combine(root,"WorldLibrary","world.json")),
                    "Каталог уже содержит проверяемые профили/мир. Проба не удаляет и не заменяет исходные файлы.");
                Require(Path.GetFullPath(value.Session.Saves.CharacterDirectoryPath)==Path.GetFullPath(firstDirectory) &&
                    SavePath.Absolute(value.Session.Saves.WorldDirectoryPath)==SavePath.Absolute(Path.Combine(root,"WorldLibrary")),
                    "Runtime не использует отдельную библиотеку Profiles/WorldLibrary.");
                Require(!NetworkServer.active && !NetworkClient.active,"Проба должна начинаться в меню без сессии.");
                var first=Profile(First,0xA3A3A3,0x854609);
                expected=Profile(Second,0x2382BA,0xF4EFE0);
                // Valid saved metadata with an explicitly older pack version: normal consent is required.
                expected.weaponContent.packs[0].version="character-selection-fixture-old";
                expected.weaponContent.fingerprint=new string('A',64);
                Require(!PonyAppearance.Snapshot(first).Equals(PonyAppearance.Snapshot(expected)),"Внешности fixture совпали.");
                // Runtime pre-creates the selected profile directory. The explicit fixed-directory
                // creation contract accepts an empty directory while retaining no-overwrite publication.
                new CharacterLibrary(Path.Combine(root,"Profiles"),firstDirectory).Create(first);
                new CharacterLibrary(Path.Combine(root,"Profiles"),secondDirectory).Create(expected);
                var world=NewGameWorld.Create();
                var previous=first.Copy();CharacterPlacement.SpawnInHub(previous);
                previous.connected=false;previous.owner=null;world.actors.Add(previous);
                selectedWorld=value.Session.Saves.SaveWorld(world);
                evidence.worldId=selectedWorld.WorldId;evidence.selectedSnapshot=selectedWorld.Id;
                secondSource=File.ReadAllText(Path.Combine(secondDirectory,"character.json"));
                foreach(string path in Directory.GetFiles(firstDirectory,"*",SearchOption.AllDirectories))
                    firstFiles.Add(new FileProof {relativePath=path.Substring(firstDirectory.Length+1),sha256=Hash(path)});
                firstFiles.Sort((a,b)=>string.CompareOrdinal(a.relativePath,b.relativePath));
                evidence.firstSource=firstFiles.ToArray();
                value.Session.UseLobby=true;
                value.Hud.HostRequested+=HostRequested;
                menu=value.Hud.GetComponentInChildren<MainMenuView>(true);
                Require(menu!=null,"Не найден настоящий MainMenuView.");
                Require(value.Session.CharacterId==First && menu.Character.text==First,"До карточки выбран не исходный pony-one.");
                preparation=menu.GetComponent<BackgroundSaveOperation>();
                Require(preparation!=null,"Меню не содержит штатную очередь подготовки BackgroundSaveOperation.");
                Debug.Log($"Проверка выбора: INITIAL saved fixture world={selectedWorld.WorldId}, actors={First}; profiles={First}/{Second}, библиотека={root}. UI выбирает второй; initial metadata второго требует согласия. Очередь задерживается только SaveAccess lease отдельной библиотеки.");
            }
            catch(Exception error) {Finish(false,error.Message);}
        }

        private static bool HasProfileFiles(string directory)=>Directory.Exists(directory) &&
            Directory.GetFiles(directory,"*",SearchOption.AllDirectories).Length>0;

        private static ActorState Profile(string name,int fur,int hair)
        {
            var actor=new ActorState {id="player:"+name,character=name,player=true,level="hub",controlStateVersion=1,
                appearance=new CharacterAppearance {version=1,fur=fur,hair=hair}};
            PlayerStarterInventory.Give(actor);actor.weaponContent=WeaponCatalog.ManifestCopy();return actor;
        }
        private void HostRequested()=>evidence.hostCallbacks++;
        private IEnumerator Start()=>Drive(Run());
        private IEnumerator Run()
        {
            if(finished) yield break;
            evidence.stage="characters";
            yield return UiPointerProbe.Click(runtime.Hud.CharactersButtonRect);
            yield return Until(()=>runtime.Hud.Characters?.Ready==true);
            var view=runtime.Hud.Characters.View;
            int card=-1,firstCard=-1;
            for(int i=0;i<view.Names.Length;i++)
                if(view.Names[i].gameObject.activeInHierarchy)
                {if(view.Names[i].text==Second) card=i;if(view.Names[i].text==First) firstCard=i;}
            Require(card>=0 && firstCard>=0 && card!=firstCard,"Обе существующие карточки не представлены раздельно.");
            Capture("cards");
            yield return UiPointerProbe.Click(Rect(view.Cards[card]));
            yield return Until(()=>runtime.Hud.Characters==null && runtime.Hud.WorldDescriptionReady);
            CheckSelected("после карточки");CheckFirst();
            Require(runtime.Session.Saves.SelectedSnapshot()?.Id==selectedWorld.Id,"Выбор второго профиля сменил выбранную копию мира.");
            Require(File.ReadAllText(Path.Combine(secondDirectory,"character.json"))==secondSource,"Карточка изменила второй профиль.");
            evidence.cardSelected=true;Capture("selected");

            evidence.stage="queued-host";
            queueLease=SaveAccess.Enter(runtime.Session.Saves.WorldDirectoryPath);
            queueArmed=true;
            yield return UiPointerProbe.Click(runtime.Hud.HostButtonRect);
            for(int i=0;i<8;i++)
            {
                CheckQueued();evidence.queuedFrames++;yield return null;
            }
            Require(evidence.busyObservedFrames>0,"Не наблюдалась действительная фоновая подготовка.");
            CheckFirst();Capture("queued");ReleaseQueue();

            evidence.stage="character-consent";
            yield return Until(()=>runtime.Hud.CharacterContent.ConfirmRect!=null);
            CheckSelected("перед согласием");CheckFirst();
            Require(evidence.hostCallbacks==1 && runtime.Session.LocalActor==null && runtime.Session.ServerWorld==null,
                "Host callback не единственный или мир запущен до решений.");
            Require(File.ReadAllText(Path.Combine(secondDirectory,"character.json"))==secondSource,
                "Подготовка изменила профиль до согласия с контентом.");
            Require(runtime.Session.CharacterContent.Differences.Count>0,"Сравнение контента не показало различие fixture.");
            Capture("content");yield return UiPointerProbe.Click(runtime.Hud.CharacterContent.ConfirmRect);
            evidence.characterConsent=true;

            evidence.stage="lobby-selection";
            yield return Until(()=>runtime.Session.Lobby.ClientPlayers==1 && runtime.Session.Lobby.Copies.Count>0 &&
                runtime.Hud.NetworkLobby.StartButtonRect!=null);
            Require(runtime.Session.LocalActor==null && runtime.Session.ServerWorld==null,"Мир начался до выбора в лобби.");
            string choice=null;
            foreach(var copy in runtime.Session.Lobby.Copies)
                if(copy.summary.worldId==selectedWorld.WorldId && copy.summary.snapshot==selectedWorld.Id)
                {Require(choice==null,"Лобби содержит повтор выбранной копии.");choice=copy.key;Require(copy.holder==Second,"Копия опубликована именем первого профиля.");}
            Require(choice!=null,"Лобби не содержит независимо записанную копию исходного мира.");
            yield return UiPointerProbe.Click(runtime.Hud.NetworkLobby.ChoiceRect(choice));
            yield return Until(()=>runtime.Session.Lobby.ClientSelected==choice && !runtime.Session.Lobby.ClientBusy);
            evidence.lobbySelected=true;Capture("lobby");
            yield return UiPointerProbe.Click(runtime.Hud.NetworkLobby.StartButtonRect);
            yield return Until(()=>runtime.Session.LocalActor!=null || runtime.Hud.NetworkLobby.ContentConfirmRect!=null);
            Require(runtime.Hud.NetworkLobby.ContentConfirmRect==null,"Текущий fixture-мир неожиданно требует согласия с контентом.");
            CheckLive();
            yield return new WaitForSecondsRealtime(3);
            CheckLive();CheckFirst();CheckRender();Capture("live");
            evidence.liveVerified=true;

            evidence.stage="graceful-leave";
            runtime.Hud.ToggleMenu();yield return null;
            var pause=runtime.Hud.GetComponentInChildren<PauseMenuView>(true);
            Require(pause!=null && pause.Leave.gameObject.activeInHierarchy,"Нет доступной настоящей кнопки выхода из сессии.");
            yield return UiPointerProbe.Click(Rect(pause.Leave));
            yield return Until(()=>!NetworkClient.active && !NetworkServer.active && runtime.Session.LocalActor==null);
            CheckFirst();
            var saved=StateJson.FromJson<ActorState>(File.ReadAllText(Path.Combine(secondDirectory,"character.json")));
            Require(saved?.character==Second && saved.id=="player:"+Second && PonyAppearance.Snapshot(saved).Equals(PonyAppearance.Snapshot(expected)),
                "Завершение сессии записало чужую идентичность/внешность во второй профиль.");
            Require(runtime.Session.Saves.SelectedSnapshot()?.WorldId==selectedWorld.WorldId,"Сессия потеряла выбранный мир.");
            Require(!runtime.Session.CharacterSelectionLocked,"Выход не освободил зафиксированный выбор персонажа.");
            evidence.savedVerified=true;Finish(true,"Реальные карточка/Host/согласие/выбор мира/старт/выход использовали pony-two; очередь подготовки удерживала сеть до завершения, исходные файлы pony-one неизменны.");
        }

        private IEnumerator Until(Func<bool> ready)
        {while(!finished && !ready()) yield return null;if(finished) yield break;yield return null;}
        // Flatten nested pointer coroutines so exceptions produce a contextual report and
        // every enumerator's finally releases its virtual input device / fixture lease.
        private IEnumerator Drive(IEnumerator routine)
        {
            var pending=new Stack<IEnumerator>();pending.Push(routine);
            try
            {
                while(!finished && pending.Count>0)
                {
                    object next=null;bool advanced=false;Exception failure=null;
                    try {advanced=pending.Peek().MoveNext();if(advanced) next=pending.Peek().Current;}
                    catch(Exception error) {failure=error;}
                    if(failure!=null) {Finish(false,failure.Message);yield break;}
                    if(!advanced) {(pending.Pop() as IDisposable)?.Dispose();continue;}
                    if(next is IEnumerator child) pending.Push(child);else yield return next;
                }
            }
            finally {while(pending.Count>0) (pending.Pop() as IDisposable)?.Dispose();ReleaseQueue();}
        }
        private void CheckSelected(string context)
        {
            evidence.sessionCharacter=runtime.Session.CharacterId;
            evidence.profilePath=runtime.Session.Saves.CharacterDirectoryPath;
            Require(runtime.Session.CharacterId==Second && menu.Character.text==Second &&
                Path.GetFullPath(evidence.profilePath)==Path.GetFullPath(secondDirectory),
                $"Выбор рассогласован ({context}): Session={runtime.Session.CharacterId}, UI={menu.Character.text}, profile={evidence.profilePath}.");
        }
        private void CheckQueued()
        {
            CheckSelected("очередь подготовки");
            Require(preparation.Busy && runtime.Session.CharacterSelectionLocked && !menu.Host.interactable && !menu.Join.interactable && !menu.Characters.interactable &&
                evidence.hostCallbacks==0 && !NetworkServer.active && !NetworkClient.active && runtime.Session.LocalActor==null,
                "Незавершённая PrepareConnection разрешила действия/сеть или потеряла Busy.");
        }
        private void CheckLive()
        {
            CheckSelected("live");var actor=runtime.Session.LocalActor;
            Require(actor!=null && actor.player && actor.connected && actor.character==Second && actor.id=="player:"+Second &&
                actor.owner==runtime.Session.LocalOwner && PonyAppearance.Snapshot(actor).Equals(PonyAppearance.Snapshot(expected)),
                $"Игровой актёр не выбранный профиль: id={actor?.id}, character={actor?.character}, owner={actor?.owner}.");
            Require(runtime.Session.World?.worldId==selectedWorld.WorldId && runtime.Session.ServerWorld?.worldId==selectedWorld.WorldId,
                "Запущен другой мир вместо отдельно записанного.");
            foreach(var candidate in runtime.Session.World.actors)
                Require(!candidate.player || !candidate.connected || candidate.character!=First,"В мире подключён первый персонаж вместо второго.");
            evidence.actorId=actor.id;evidence.actorCharacter=actor.character;evidence.actorOwner=actor.owner;
            evidence.appearance=StateJson.ToJson(actor.appearance);
        }
        private void CheckRender()
        {
            var actor=runtime.Session.LocalActor;
            var pony=runtime.FindActorVisual(actor.id) as PonyVisual;
            Require(pony!=null && pony.ActorId==actor.id && pony.TryGetDrawnBounds(out _),"Нет фактически отрисованной пони выбранного актёра.");
            var key=PonyAppearance.Snapshot(expected);
            var parts=PonyRenderSelection.Frames(key,pony.DrawnClip)[pony.DrawnFrame];
            var renderers=pony.GetComponentsInChildren<MeshRenderer>(true);
            var property=new MaterialPropertyBlock();int actual=0;
            foreach(var renderer in renderers)
            {
                if(!renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy) continue;
                int index=renderer.sortingOrder-20;
                Require(index>=0 && index<parts.Length && parts[index].visible &&
                    renderer.GetComponent<MeshFilter>()?.sharedMesh==parts[index].mesh && renderer.sharedMaterial==parts[index].material,
                    "Фактический mesh/material пони не соответствует выбранной внешности.");
                renderer.GetPropertyBlock(property);
                Require(property.GetColor("_Color")==PonyAppearance.Tint(parts[index].slot,key),
                    "Фактический цвет части пони отличается от второго профиля: slot="+parts[index].slot);
                actual++;
            }
            Require(actual==pony.DrawnPartCount && actual>0,"Количество фактически видимых частей рассогласовано.");
            evidence.drawnParts=actual;evidence.drawnClip=pony.DrawnClip;evidence.drawnFrame=pony.DrawnFrame;
            evidence.renderVerified=true;
        }
        private void CheckFirst()
        {
            var paths=Directory.GetFiles(firstDirectory,"*",SearchOption.AllDirectories);
            Require(paths.Length==firstFiles.Count,"Изменился набор исходных файлов pony-one.");
            foreach(var file in firstFiles)
                Require(File.Exists(Path.Combine(firstDirectory,file.relativePath)) && Hash(Path.Combine(firstDirectory,file.relativePath))==file.sha256,
                    "Изменён исходный файл pony-one: "+file.relativePath);
            evidence.firstSourceUnchanged=true;
        }
        private static string Hash(string path)
        {using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
        private static RectTransform Rect(Selectable control)=>control.GetComponent<RectTransform>();
        private static void Require(bool condition,string reason) {if(!condition) throw new InvalidOperationException(reason);}
        private void Capture(string suffix)=>NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+"."+suffix+".png",runtime.Hud.GetComponent<Canvas>(),1920,1080);
        private void ReleaseQueue() {queueArmed=false;queueLease?.Dispose();queueLease=null;}
        private void Update()
        {
            if(finished || runtime==null) return;
            if(queueArmed && preparation!=null && preparation.Busy) evidence.busyObservedFrames++;
            if(Time.unscaledTime>deadline) Finish(false,$"Таймаут: stage={evidence.stage}, character={runtime.Session.CharacterId}, actor={runtime.Session.LocalActor?.id}, busy={preparation?.Busy}, lobby={runtime.Session.Lobby.ClientWaiting}/{runtime.Session.Lobby.ClientBusy}, error={runtime.Session.Lobby.ClientError}.");
        }
        private void OnDestroy() {ReleaseQueue();if(runtime!=null) runtime.Hud.HostRequested-=HostRequested;}
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;ReleaseQueue();
            evidence.success=success;evidence.detail=detail;
            evidence.sessionCharacter=runtime?.Session.CharacterId;evidence.profilePath=runtime?.Session.Saves?.CharacterDirectoryPath;
            Debug.Log((success?"PASS ":"FAIL ")+"characters-selection: "+detail);
            File.WriteAllText(runtime.Options.Report,StateJson.ToJson(evidence,true));Application.Quit(success?0:1);
        }
        [Serializable] private sealed class FileProof {public string relativePath,sha256;}
        [Serializable] private sealed class Evidence
        {
            public bool success,cardSelected,characterConsent,lobbySelected,liveVerified,renderVerified,savedVerified,firstSourceUnchanged;
            public string detail,stage,libraryRoot,worldId,selectedSnapshot,sessionCharacter,profilePath,actorId,actorCharacter,actorOwner,appearance,drawnClip;
            public int queuedFrames,busyObservedFrames,hostCallbacks,drawnParts,drawnFrame;
            public FileProof[] firstSource;
        }
    }
}