using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Runtime
{
    public struct MeleeProbePhase : NetworkMessage {public int phase;}
    public struct MeleeProbeFinish : NetworkMessage {public bool success;public string detail;}
    public sealed class NetworkMeleeProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private int phase;
        private float deadline,phaseAt;
        private bool fired,finished;
        private readonly Dictionary<string,int> sweeps=new();
        private bool Host=>runtime.Options.Role=="host";
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+40;value.CommandSource=Command;
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<MeleeProbePhase>(m=>{phase=m.phase;phaseAt=Time.unscaledTime;fired=false;});
                NetworkClient.RegisterHandler<MeleeProbeFinish>(m=>Finish(m.success,m.detail));
            };
            value.Session.AttackReceived+=attack=>
            {if(attack.attacker=="player:session-host" && (attack.weapon=="crowbar" || attack.weapon=="spear")) sweeps[attack.id]=sweeps.GetValueOrDefault(attack.id)+1;};
        }
        private PlayerCommand Command()
        {
            var self=runtime.Session.LocalActor;if(self==null) return default;
            if(phase==0)
            {
                float target=Host?5:runtime.Options.Character=="session-client-1"?6:12;
                float delta=target-self.x;
                return new PlayerCommand {movement=new Vector2(Mathf.Abs(delta)>.03f?Mathf.Sign(delta):0,0)};
            }
            if(!Host || phase>2 || fired) return default;
            fired=true;
            return new PlayerCommand {weapon=phase==1?"crowbar":"spear",fire=true,hasAim=true,aim=self.Position+new Vector2(5,.8f)};
        }
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут ближнего боя: phase={phase}, attacks={sweeps.Count}");return;}
            var session=runtime.Session;
            if(!Host || session.LocalActor==null || session.ServerWorld.actors.Count(a=>a.player && a.connected)!=runtime.Options.ProbePeers) return;
            var players=session.ServerWorld.actors.Where(a=>a.player && a.connected).ToArray();
            var victim=session.ServerWorld.Find("player:session-client-1");
            if(phase==0)
            {
                if(players.Any(a=>Mathf.Abs(a.x-(a.character=="session-host"?5:a.character=="session-client-1"?6:12))>.08f || Mathf.Abs(a.vx)>.1f)) return;
                NetworkServer.SendToAll(new MeleeProbePhase {phase=1});return;
            }
            if(phase>2 || Time.unscaledTime-phaseAt<1.5f) return;
            if(victim.damageSequence!=phase || victim.health>=100 || sweeps.Count!=phase || sweeps.Values.Any(count=>count<2))
            {Complete(false,$"Ожидался один урон за взмах: phase={phase}, damageSequence={victim.damageSequence}, hp={victim.health}, attacks={sweeps.Count}.");return;}
            if(players.Where(a=>a.id!=victim.id).Any(a=>a.health!=100) || players.Any(a=>a.mana!=100))
            {Complete(false,"Ближний бой повредил наблюдателя или изменил ману.");return;}
            if(phase==1) NetworkServer.SendToAll(new MeleeProbePhase {phase=2});
            else Complete(true,"Лом и копьё: обычные команды, несколько сегментов одного взмаха, ровно один урон пострадавшему за взмах, наблюдатели и мана не затронуты.");
        }
        private void Complete(bool success,string detail)
        {phase=3;NetworkServer.SendToAll(new MeleeProbeFinish {success=success,detail=detail});}
        private void Finish(bool success,string detail)
        {
            if(finished)return;finished=true;
            bool attacker=runtime.Options.Smoke.Contains("-attacker-");
            int expected=runtime.Options.Character=="session-client-1"?2:0;
            if(runtime.Combat.AttackerDecidedHits!=(attacker?expected:0) || runtime.Combat.VictimDecidedHits!=(attacker?0:expected)) success=false;
            detail+=$" Решения: attacker={runtime.Combat.AttackerDecidedHits}, victim={runtime.Combat.VictimDecidedHits}.";
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail},true));StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:2);}
        [Serializable] private sealed class Report {public bool success;public string detail;}
    }
}
