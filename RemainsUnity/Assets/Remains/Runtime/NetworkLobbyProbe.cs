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
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+45;
            if(string.IsNullOrEmpty(value.Options.SaveDirectory)) throw new InvalidOperationException("Лобби-проверка требует отдельного --save-dir.");
            int index=Host?0:int.Parse(value.Options.Character.Split('-').Last());
            var world=new WorldState {worldId="11111111111111111111111111111111",branchId="22222222222222222222222222222222",historyVersion=1,
                checkpointSequence=index==1?9:4,revision=index==1?10:5,seed=100+index};
            if(index==2) WorldHistory.BeginContinuation(world);
            if(index==3) world.worldId="33333333333333333333333333333333";
            for(int i=0;i<(Interrupt || value.Options.Smoke.Contains("large")?5000:1000);i++) world.container.Add(new ItemStack {id="fixture-"+i,definition="caps",count=1});
            original=value.Session.Saves.SaveWorld(world);
            value.Session.ClientReady+=()=>NetworkClient.RegisterHandler<LobbyProbeFinish>(_=>Finish(ValidateLive(),Interrupt?"После обрыва неполная копия не загружена; через UI успешно выбрано другое продолжение.":"Переданная копия запущена; исходное продолжение сохранено в архиве."));
        }
        private IEnumerator Start()
        {
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
            return world!=null && world.seed==(Interrupt?100:101) && world.worldId=="11111111111111111111111111111111" &&
                world.ancestors.Any(a=>a.branchId=="22222222222222222222222222222222" && a.sequence==(Interrupt?5:10)) &&
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
