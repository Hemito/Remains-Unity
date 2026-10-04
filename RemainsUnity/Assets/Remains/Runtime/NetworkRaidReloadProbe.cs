using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Persistence;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Runtime
{
    public struct RaidReloadReady : NetworkMessage {}
    public struct RaidReloadFinish : NetworkMessage {public bool success;public string detail;}
    public struct RaidReloadDisconnect : NetworkMessage {}

    // Controlled saved raid, separate profiles and fresh OS processes. Enemy attacks are
    // disabled in this fixture so a real hit cannot mask a connection-state regression.
    [DefaultExecutionOrder(10000)]
    public sealed class NetworkRaidReloadProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private ActorState expected;
        private readonly HashSet<int> ready=new();
        private float entered,deadline;
        private bool sent,finished,disconnectRequested;
        private bool Host=>runtime.Options.Role=="host";
        private bool Resume=>runtime.Options.Smoke.Contains("-resume-");
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+40;
            if(string.IsNullOrEmpty(value.Options.SaveDirectory)) throw new InvalidOperationException("Проверка загрузки рейда требует отдельного сохранения.");
            if(Host && !Resume)
            {
                if(value.Session.Saves.SelectedSnapshot()!=null) throw new InvalidOperationException("Проверка не заменяет существующий мир.");
                var world=NewGameWorld.Create();var room=RoomLayout.Load("factory:0",world);
                for(int index=0;index<value.Options.ProbePeers;index++)
                {
                    string character=index==0?"session-host":"session-client-"+index;
                    var actor=new ActorState {id="player:"+character,character=character,player=true,level="factory:0",
                        x=room.spawnX+index*.6f,y=room.spawnY,health=11+index*20,controlStateVersion=ActorSessionRules.ControlStateVersion};
                    actor.inventory.Add(SharedInventory.Create("heal",4));
                    WeaponAmmunition.Initialize(actor,true);actor=WeaponInventoryMigration.Prepare(actor);
                    actor.resolvedAttacks.Add("before-save");world.actors.Add(actor);
                }
                value.Session.Saves.SaveWorld(world);
            }
            value.CommandSource=()=>default;
            value.Session.ServerReady+=()=>
            {
                foreach(var npc in value.Session.ServerWorld.actors.Where(a=>!a.player)) {npc.health=0;npc.down=true;}
                NetworkServer.RegisterHandler<RaidReloadReady>((connection,_)=>ready.Add(connection.connectionId));
            };
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<RaidReloadFinish>(message=>Finish(message.success,message.detail));
                NetworkClient.RegisterHandler<RaidReloadDisconnect>(_=>
                {if(!Host) Finish(true,"Живой участник отключён в рейде перед сохранением хостом.");});
            };
        }
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false,"Таймаут проверки загрузки рейда.");return;}
            var actor=runtime.Session.LocalActor;if(actor==null) return;
            if(entered==0)
            {
                entered=Time.unscaledTime;
                expected=Resume?StateJson.FromJson<ActorState>(File.ReadAllText(Path.Combine(runtime.Session.Saves.DirectoryPath,"raid-expected.json"))):actor.Copy();
            }
            if(actor.down || actor.level!="factory:0" || actor.health!=expected.health || actor.Position!=expected.Position ||
                actor.damageSequence!=expected.damageSequence || actor.id!=expected.id ||
                StateJson.ToJson(actor.inventory)!=StateJson.ToJson(expected.inventory) ||
                StateJson.ToJson(actor.resolvedAttacks)!=StateJson.ToJson(expected.resolvedAttacks))
            {Finish(false,$"Загрузка/ожидание изменили рейд: actor={actor.id}, level={actor.level}, down={actor.down}, HP={actor.health}, position={actor.Position}.");return;}
            // Wait beyond the three-second party-defeat/checkpoint cadence.
            if(Time.unscaledTime-entered<5) return;
            if(!sent)
            {
                sent=true;File.WriteAllText(Path.Combine(runtime.Session.Saves.DirectoryPath,"raid-expected.json"),StateJson.ToJson(expected));
                NetworkClient.Send(new RaidReloadReady());
            }
            if(Host && ready.Count==runtime.Options.ProbePeers)
            {
                if(!Resume)
                {
                    if(!disconnectRequested)
                    {disconnectRequested=true;NetworkServer.SendToAll(new RaidReloadDisconnect());}
                    var offline=runtime.Session.ServerWorld.actors.Where(a=>a.player && a.character!=runtime.Options.Character).ToArray();
                    if(offline.Any(a=>a.connected)) return;
                    if(offline.Any(a=>a.down || !ActorSessionRules.IsDisconnectedBody(a) || a.health<=0))
                    {Finish(false,"Отключение живого участника стало настоящим нокдауном.");return;}
                }
                runtime.Session.CommitWorld();
                NetworkServer.SendToAll(new RaidReloadFinish {success=true,detail=Resume?
                    "Новые процессы вернули живых игроков в тот же рейд с прежними HP, позицией, предметами и историей урона; проверка поражения не отправила их в хаб.":
                    "Живые игроки сохранены в рейде для перезапуска процессов."});
            }
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            File.WriteAllText(runtime.Options.Report,StateJson.ToJson(new Report {success=success,detail=detail},true));
            StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success)
        {
            yield return null;
            if(!Resume)
            {
                if(Host) runtime.Session.StopHost();else runtime.Session.StopClient();
                if(Host && runtime.Session.Saves.LoadWorld(false).actors.Any(a=>a.player && (a.down || a.health<=0)))
                {
                    success=false;File.WriteAllText(runtime.Options.Report,StateJson.ToJson(new Report
                        {success=false,detail="Завершение хоста записало ложный нокдаун живого игрока."},true));
                }
            }
            yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:1);
        }
        [Serializable] private sealed class Report {public bool success;public string detail;}
    }
}
