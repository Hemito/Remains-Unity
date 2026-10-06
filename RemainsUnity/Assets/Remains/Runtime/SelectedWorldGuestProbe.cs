using System;
using System.Collections;
using System.IO;
using Remains.Content;
using Remains.Domain;
using Remains.Persistence;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Runtime
{
    // A separate real KCP client joins an already selected host world. Its own
    // different local world is a declared fixture and must remain recoverable.
    public sealed class SelectedWorldGuestProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private RecoverySnapshot original;
        private string joinedWorld;
        private float deadline;
        private bool finished,stopped;
        private double playTimeSeconds;
        private bool playTimePersisted;
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+100;
            try
            {
                Require(value.Options.Role=="client" && value.Options.Character=="pony-guest" && !string.IsNullOrEmpty(value.Options.SaveDirectory),"Нужен отдельный client/pony-guest/save-dir.");
                var library=new CharacterLibrary(Path.Combine(value.Options.SaveDirectory,"Profiles"),value.Session.Saves.CharacterDirectoryPath);
                var actor=new ActorState {id="player:pony-guest",character="pony-guest",player=true,health=73,level="hub",controlStateVersion=1,
                    appearance=new CharacterAppearance {version=1,fur=0xBD8251,hair=0x4335A5},weaponContent=WeaponCatalog.ManifestCopy()};
                PlayerStarterInventory.Give(actor);library.Create(actor);
                original=value.Session.Saves.SaveWorld(NewGameWorld.Create());
                value.Session.SessionStopped+=OnStopped;
            }
            catch(Exception error) {Finish(false,error.Message);}
        }
        private void OnStopped()=>stopped=true;
        private IEnumerator Start()
        {
            while(!finished && runtime.Session.LocalActor==null) yield return null;
            if(finished) yield break;
            try
            {
                joinedWorld=runtime.Session.World.worldId;
                Require(joinedWorld!=original.WorldId,"Подключение запустило собственный мир вместо выбранного хостом.");
                Require(runtime.Session.LocalActor.character=="pony-guest" && runtime.Session.LocalActor.health==73,"Вход заменил профиль или состояние гостя.");
                Require(runtime.Session.Lobby.SentBytes==0 && !runtime.Session.Lobby.ClientChoosingCopies,"Обычный вход передаёт или повторно выбирает локальную копию.");
            }
            catch(Exception error) {Finish(false,error.Message);yield break;}
            while(!finished && !stopped) yield return null;
            while(!finished && !runtime.Hud.WorldDescriptionReady) yield return null;
            if(finished) yield break;
            try
            {
                string archive=Path.Combine(runtime.Session.Saves.WorldDirectoryPath,"Worlds",original.WorldId,"Snapshots",original.Id+".json");
                Require(File.ReadAllText(archive)==original.Json,"Мир гостя не сохранён отдельной исходной копией.");
                var menu=runtime.Hud.GetComponentInChildren<MainMenuView>(true);
                Require(!runtime.Session.CharacterSelectionLocked && menu.Controls.activeInHierarchy && menu.Characters.IsInteractable() &&
                    menu.Worlds.IsInteractable() && menu.CompareCopies.IsInteractable(),"Полный выход клиента оставил меню заблокированным.");
                var time=CharacterTimeRecord.Read(runtime.Session.Saves.CharacterDirectoryPath);
                Require(time!=null && time.completeHistory && time.seconds>=1,
                    "Полный выход клиента не завершил запись его личного игрового времени.");
                playTimeSeconds=time.seconds;playTimePersisted=true;
                Finish(true,"Поздний KCP-клиент вошёл в выбранный мир хоста со своим профилем; своя копия сохранена, передачи/повторного выбора нет, после выхода клиента меню доступно.");
            }
            catch(Exception error) {Finish(false,error.Message);}
        }
        private void Update() {if(!finished && Time.unscaledTime>deadline) Finish(false,"Таймаут позднего входа/выхода клиента.");}
        private void OnDestroy() {if(runtime!=null) runtime.Session.SessionStopped-=OnStopped;}
        private static void Require(bool condition,string reason) {if(!condition) throw new InvalidOperationException(reason);}
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            File.WriteAllText(runtime.Options.Report,StateJson.ToJson(new Evidence {success=success,detail=detail,worldId=joinedWorld,originalWorldId=original?.WorldId,
                playTimeSeconds=playTimeSeconds,playTimePersisted=playTimePersisted},true));
            Debug.Log((success?"PASS ":"FAIL ")+"selected-world-guest: "+detail);Application.Quit(success?0:1);
        }
        [Serializable] private sealed class Evidence {public bool success,playTimePersisted;public double playTimeSeconds;public string detail,worldId,originalWorldId;}
    }
}
