using System;
using System.Collections;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Runtime
{
    public struct GrenadeProbePhase : NetworkMessage {public int phase;}
    public struct GrenadeProbeFinish : NetworkMessage { }
    public sealed class NetworkGrenadeProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private int phase,explosions;
        private float deadline,returnAt,settledAt;
        private bool threw,disconnected,returned,finished,handoff,startRequested;
        private string grenadeId;
        private bool Resume=>runtime.Options.Smoke.Contains("-resume-");
        private bool Host=>runtime.Options.Role=="host";
        private bool Source=>runtime.Options.Character=="session-client-1";
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+45;value.CommandSource=Command;
            value.Session.AttackReceived+=attack=>{if(attack.weapon=="grenade") explosions++;};
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<GrenadeProbePhase>(message=>phase=message.phase);
                NetworkClient.RegisterHandler<GrenadeProbeFinish>(_=>Finish(Validate(),Resume?"Новый запуск из снимка активной гранаты: запал продолжен, одна детонация, расход предмета не повторён.":"Граната пережила отключение автора, один владелец завершил взрыв; повторные снимки и возвращение автора не повторили урон."));
            };
        }
        private PlayerCommand Command()
        {
            if(Resume || phase!=1 || !Source || threw || runtime.Session.LocalActor==null) return default;
            threw=true;return new PlayerCommand {grenade=true,hasAim=true,aim=runtime.Session.LocalActor.Position+Vector2.down*3};
        }
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут гранаты: phase={phase}, handoff={handoff}, explosions={explosions}, active={runtime.Session.World?.grenades.Count}.");return;}
            var session=runtime.Session;
            if(Host && Resume && session.Lobby.ClientWaiting && !startRequested && session.Lobby.ClientPlayers==runtime.Options.ProbePeers)
            {startRequested=true;session.Lobby.StartSelected();}
            if(!Resume && Source && !disconnected && threw && session.World?.grenades.Count>0 && runtime.Grenades.Flights.Any(f=>f.state.remaining<1.3f && f.state.remaining>.3f) &&
                session.LocalActor.inventory.Where(i=>i.definition=="grenade").Sum(i=>i.count)==4)
            {disconnected=true;returnAt=Time.unscaledTime+4;session.StopClient();return;}
            if(Source && disconnected && !returned && Time.unscaledTime>=returnAt)
            {returned=true;session.StartClient();return;}
            if(!Host || session.World==null) return;
            if(Resume && session.ServerWorld.grenades.Count>0) {handoff=true;grenadeId=session.ServerWorld.grenades[0].id;}
            if(phase==0 && session.World.actors.Count(a=>a.player && a.connected)==runtime.Options.ProbePeers)
            {phase=1;NetworkServer.SendToAll(new GrenadeProbePhase {phase=1});}
            if(phase!=1) return;
            var active=session.ServerWorld.grenades.FirstOrDefault();
            if(active!=null)
            {
                grenadeId=active.id;
                if(active.generation>1 && session.ServerWorld.Find("player:session-client-1")==null)
                {
                    handoff=true;
                    // Exact canonical checkpoint, captured while the fuse is still live.
                    string proof=runtime.Options.Report+".active-world.json";
                    if(!File.Exists(proof)) File.WriteAllText(proof,session.Saves.SelectedSnapshot().Json);
                }
            }
            if(!handoff || session.World.grenades.Count!=0 || session.World.explosions.Count!=1 ||
                session.World.actors.Count(a=>a.player && a.connected)!=runtime.Options.ProbePeers) return;
            if(settledAt==0) settledAt=Time.unscaledTime;
            if(Time.unscaledTime-settledAt>4)
            {
                if(explosions!=1 || !Validate()) {Finish(false,"Неверный результат взрыва после передачи владельца.");return;}
                phase=2;NetworkServer.SendToAll(new GrenadeProbeFinish());
            }
        }
        private bool Validate()
        {
            var world=runtime.Session.World;
            if(world==null || world.grenades.Count!=0 || world.explosions.Count!=1) return false;
            var id=world.explosions[0].attack.id;
            return world.actors.Where(a=>a.player && a.connected).All(a=>!Resume && a.character=="session-client-1"?
                a.health==100 && !a.resolvedExplosions.Contains(id) && a.inventory.Where(i=>i.definition=="grenade").Sum(i=>i.count)==4:
                a.health==30 && a.resolvedExplosions.Count(x=>x==id)==1 && (a.character!="session-client-1" || a.inventory.Where(i=>i.definition=="grenade").Sum(i=>i.count)==4));
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail},true));StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:1);}
        [Serializable] private sealed class Report {public bool success;public string detail;}
    }
}
