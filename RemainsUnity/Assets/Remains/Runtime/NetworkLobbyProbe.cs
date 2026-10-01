using System;
using System.Collections;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Domain;
using Remains.Persistence;
using UnityEngine;

namespace Remains.Runtime
{
    public struct LobbyProbeFinish : NetworkMessage { }
    public sealed class NetworkLobbyProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private RecoverySnapshot original;
        private float deadline;
        private bool finished,wasWaiting;
        private bool Interrupt=>runtime.Options.Smoke.Contains("interrupt");
        private bool Host=>runtime.Options.Role=="host";
        private bool ContentScenario=>runtime.Options.Smoke.Contains("content");
        private bool GeometryScenario=>runtime.Options.Smoke.Contains("geometry");
        private bool LegacyScenario=>runtime.Options.Smoke.Contains("legacy");
        private bool CharacterScenario=>runtime.Options.Smoke.Contains("character");
        private bool MalformedScenario=>runtime.Options.Smoke.Contains("malformed");
        private string characterSource;
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+45;
            if(string.IsNullOrEmpty(value.Options.SaveDirectory)) throw new InvalidOperationException("Лобби-проверка требует отдельного --save-dir.");
            int index=Host?0:int.Parse(value.Options.Character.Split('-').Last());
            var world=new WorldState {worldId="11111111111111111111111111111111",branchId="22222222222222222222222222222222",historyVersion=1,
                checkpointSequence=index==1?9:4,revision=index==1?10:5,seed=100+index};
            if(index==2) WorldHistory.BeginContinuation(world);
            if(index==3) world.worldId="33333333333333333333333333333333";
            world.containers.Add(new ContainerState());
            for(int i=0;i<(Interrupt || value.Options.Smoke.Contains("large")?5000:1000);i++) world.containers[0].items.Add(new ItemStack {id="fixture-"+i,definition="caps",count=1});
            if((ContentScenario || MalformedScenario) && index==1)
            {
                world.weaponContent=Remains.Content.WeaponCatalog.ManifestCopy();
                world.weaponContent.fingerprint=new string('F',64);
                if(GeometryScenario) world.weaponContent.gameplayAssets.Single(a=>a.id=="Content/prototype-room").fingerprint="fixture-geometry";
                else if(LegacyScenario) {world.weaponContent.scope="weapon-catalog";world.weaponContent.gameplayAssets.Clear();}
                else world.weaponContent.packs[0].version="fixture-old";
                if(MalformedScenario) world.weaponContent.packs.Add(null);
                original=new RecoverySnapshot(Remains.Serialization.StateJson.ToJson(world));value.Session.Saves.SaveRecovery(original);
            }
            else original=value.Session.Saves.SaveWorld(world);
            if(CharacterScenario)
            {
                var actor=new ActorState {character=value.Options.Character,health=73,mana=64,weaponContent=Remains.Content.WeaponCatalog.ManifestCopy()};
                actor.weaponContent.packs[0].version="character-old";actor.weaponContent.fingerprint=new string('A',64);
                characterSource=Remains.Serialization.StateJson.ToJson(actor);
                File.WriteAllText(Path.Combine(value.Session.Saves.DirectoryPath,"character.json"),characterSource);
            }
            value.Session.ClientReady+=()=>NetworkClient.RegisterHandler<LobbyProbeFinish>(_=>Finish(ValidateLive(),Interrupt?"После обрыва неполная копия не загружена; через UI успешно выбрано другое продолжение.":"Переданная копия запущена; исходное продолжение сохранено в архиве."));
        }
        private IEnumerator Start()
        {
            if(CharacterScenario)
            {
                while(runtime.Hud.CharacterContent.ConfirmRect==null && !finished) yield return null;
                if(finished) yield break;
                string sourcePath=Path.Combine(runtime.Session.Saves.DirectoryPath,"character.json");
                if(runtime.Session.LocalActor!=null || File.ReadAllText(sourcePath)!=characterSource ||
                    Directory.Exists(Path.Combine(runtime.Session.Saves.DirectoryPath,"Recovery")))
                {Finish(false,"Персонаж подготовлен или исходник изменён до решения.");yield break;}
                if(!Host && runtime.Options.Character.EndsWith("-1"))
                {
                    yield return UiPointerProbe.Click(runtime.Hud.CharacterContent.CancelRect);
                    while(NetworkClient.active && !finished) yield return null;
                    if(File.ReadAllText(sourcePath)!=characterSource || runtime.Session.LocalActor!=null)
                    {Finish(false,"Отмена сравнения изменила персонажа.");yield break;}
                    runtime.Session.StartClient();
                    while(runtime.Hud.CharacterContent.ConfirmRect==null && !finished) yield return null;
                }
                if(finished) yield break;
                yield return UiPointerProbe.Click(runtime.Hud.CharacterContent.ConfirmRect);
            }
            if(!Host) yield break;
            var lobby=runtime.Session.Lobby;
            while(lobby.ClientPlayers!=runtime.Options.ProbePeers && !finished) yield return null;
            if(finished) yield break;
            if(runtime.Session.ServerWorld!=null || runtime.Session.LocalActor!=null)
            {Finish(false,"Мир запущен до выбора продолжения.");yield break;}
            var remote=lobby.Copies.Single(c=>c.holder=="lobby-client-1");
            var local=lobby.Copies.Single(c=>c.holder=="lobby-host");
            if(remote.summary.CompareTo(local.summary)!=WorldRelation.Newer)
            {Finish(false,"Не обнаружена более поздняя копия у другого игрока.");yield break;}
            yield return UiPointerProbe.Click(runtime.Hud.NetworkLobby.ChoiceRect(remote.key));
            while(lobby.ClientSelected!=remote.key && !finished) yield return null;
            if(runtime.Options.ProbePeers==4)
            {
                var divergent=lobby.Copies.Single(c=>c.holder=="lobby-client-2");
                var different=lobby.Copies.Single(c=>c.holder=="lobby-client-3");
                if(divergent.summary.CompareTo(remote.summary)!=WorldRelation.Divergent || different.summary.CompareTo(remote.summary)!=WorldRelation.DifferentWorld)
                {Finish(false,"Лобби не различило независимую ветвь и другой мир.");yield break;}
            }
            yield return UiPointerProbe.Click(runtime.Hud.NetworkLobby.StartButtonRect);
            if(MalformedScenario)
            {
                while((lobby.ClientBusy || string.IsNullOrEmpty(lobby.ClientError)) && !finished) yield return null;
                if(finished) yield break;
                if(runtime.Session.ServerWorld!=null || runtime.Session.Saves.SelectedSnapshot().Id!=original.Id ||
                    !lobby.ClientError.Contains("позиция=2"))
                {Finish(false,"Повреждённый манифест изменил мир или не дал точной ошибки.");yield break;}
                yield return UiPointerProbe.Click(runtime.Hud.NetworkLobby.ChoiceRect(local.key));
                while(lobby.ClientSelected!=local.key && !finished) yield return null;
                yield return UiPointerProbe.Click(runtime.Hud.NetworkLobby.StartButtonRect);
            }
            if(ContentScenario)
            {
                while(runtime.Hud.NetworkLobby.ContentConfirmRect==null && !finished) yield return null;
                if(finished) yield break;
                if(runtime.Session.ServerWorld!=null || runtime.Session.Saves.SelectedSnapshot().Id!=original.Id ||
                    !(GeometryScenario?lobby.ContentDifferences.Any(row=>row.id=="Ресурс: Content/prototype-room" && row.saved=="fixture-geometry"):
                        LegacyScenario?lobby.ContentDifferences.Count(row=>row.id.StartsWith("Ресурс: ") && row.saved=="Не записан")==4:
                        lobby.ContentDifferences.Any(row=>row.id=="base" && row.saved.Contains("fixture-old"))))
                {Finish(false,"Мир изменён до подтверждения или не показана сохранённая версия контента.");yield break;}
                yield return UiPointerProbe.Click(runtime.Hud.NetworkLobby.ContentCancelRect);
                while((lobby.ClientBusy || runtime.Hud.NetworkLobby.ContentCancelRect!=null) && !finished) yield return null;
                if(finished) yield break;
                if(runtime.Session.ServerWorld!=null || runtime.Session.Saves.SelectedSnapshot().Id!=original.Id)
                {Finish(false,"Отмена сравнения изменила выбранный мир.");yield break;}
                yield return UiPointerProbe.Click(runtime.Hud.NetworkLobby.StartButtonRect);
                while(runtime.Hud.NetworkLobby.ContentConfirmRect==null && !finished) yield return null;
                if(finished) yield break;
                yield return UiPointerProbe.Click(runtime.Hud.NetworkLobby.ContentConfirmRect);
            }
            if(Interrupt)
            {
                while(lobby.ReceivedBytes==0 && runtime.Session.ServerWorld==null && !finished) yield return null;
                if(finished) yield break;
                if(runtime.Session.ServerWorld!=null) {Finish(false,"Передача завершилась до проверки обрыва.");yield break;}
                NetworkServer.connections[remote.connection].Disconnect();
                while((lobby.ClientBusy || lobby.ClientPlayers!=1 || string.IsNullOrEmpty(lobby.ClientError)) && !finished) yield return null;
                if(finished) yield break;
                if(runtime.Session.ServerWorld!=null || runtime.Session.Saves.SelectedSnapshot().Id!=original.Id)
                {Finish(false,"Неполная передача заменила исходный мир.");yield break;}
                yield return UiPointerProbe.Click(runtime.Hud.NetworkLobby.ChoiceRect(local.key));
                while(lobby.ClientSelected!=local.key && !finished) yield return null;
                yield return UiPointerProbe.Click(runtime.Hud.NetworkLobby.StartButtonRect);
            }
            int expected=Interrupt?1:runtime.Options.ProbePeers;
            while((runtime.Session.LocalActor==null || runtime.Session.World?.actors.Count(a=>a.player && a.connected)!=expected) && !finished) yield return null;
            if(finished) yield break;
            if(!ValidateLive()) {Finish(false,"Запущена не выбранная копия или потерян исходный архив.");yield break;}
            NetworkServer.SendToAll(new LobbyProbeFinish());
        }
        private bool ValidateLive()
        {
            var world=runtime.Session.World;
            var archive=Path.Combine(runtime.Session.Saves.DirectoryPath,"Worlds",original.WorldId,"Snapshots",original.Id+".json");
            if(CharacterScenario)
            {
                var actor=runtime.Session.LocalActor;
                string backups=Path.Combine(runtime.Session.Saves.DirectoryPath,"Recovery");
                if(actor==null || actor.health!=73 || actor.mana!=64 || actor.loadoutVersion!=1 ||
                    !Directory.Exists(backups) || !Directory.GetFiles(backups,"character-*.json").Any(p=>File.ReadAllText(p)==characterSource)) return false;
            }
            if(GeometryScenario || LegacyScenario)
            {
                if(world?.weaponContent?.scope!="prototype-gameplay" || world.weaponContent.gameplayAssets.Count!=4 ||
                    world.weaponContentHistory==null || !world.weaponContentHistory.Any(m=>GeometryScenario?
                        m.gameplayAssets.Any(a=>a.id=="Content/prototype-room" && a.fingerprint=="fixture-geometry"):
                        m.scope=="weapon-catalog" && m.gameplayAssets.Count==0)) return false;
            }
            return world!=null && world.seed==(Interrupt || MalformedScenario?100:101) && world.worldId=="11111111111111111111111111111111" &&
                world.ancestors.Any(a=>a.branchId=="22222222222222222222222222222222" && a.sequence==(Interrupt || MalformedScenario?5:ContentScenario?9:10)) &&
                File.Exists(archive) && File.ReadAllText(archive)==original.Json;
        }
        private void Update()
        {
            wasWaiting|=runtime.Session.Lobby.ClientWaiting;
            if(Interrupt && !Host && wasWaiting && !NetworkClient.isConnected && !finished)
                Finish(runtime.Session.Saves.SelectedSnapshot()?.Id==original.Id && runtime.Session.World==null,
                    "Отключение во время передачи: исходная копия не заменена, персонаж не создан.");
            if(!finished && Time.unscaledTime>deadline) Finish(false,$"Таймаут сетевого выбора: waiting={runtime.Session.Lobby.ClientWaiting}, peers={runtime.Session.Lobby.ClientPlayers}, world={runtime.Session.World?.worldId}, seed={runtime.Session.World?.seed}.");
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail},true));
            StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:1);}
        [Serializable] private sealed class Report {public bool success;public string detail;}
    }
}
