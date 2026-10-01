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
    public struct NpcProbePhase : NetworkMessage {public int phase;}
    public struct NpcProbeFinish : NetworkMessage {public bool success;public string detail;}
    // Commands and connection lifecycle only; never writes actor state.
    public sealed class NetworkNpcProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private int phase;
        private float deadline,returnAt,canceledAt;
        private bool traveled,disconnected,returned,finished,deferred,transferred,canceled;
        private string firstAttack,canceledAttack;
        private readonly Dictionary<string,float> shots=new();
        private bool Host=>runtime.Options.Role=="host";
        private bool Source=>runtime.Options.Character=="session-client-1";
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+45;value.CommandSource=()=>default(PlayerCommand);
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<NpcProbePhase>(m=>phase=m.phase);
                NetworkClient.RegisterHandler<NpcProbeFinish>(m=>Finish(m.success,m.detail));
            };
            value.Session.AttackReceived+=attack=>
            {
                if(attack.weapon!="npc-shot") return;
                if(shots.ContainsKey(attack.id)) {Finish(false,"NPC повторил ID выстрела.");return;}
                shots.Add(attack.id,Time.unscaledTime);
            };
        }
        private void Update()
        {
            if(finished) return;
            var session=runtime.Session;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут NPC: phase={phase}, deferred={deferred}, transfer={transferred}, cancel={canceled}, shots={shots.Count}.");return;}
            if(Source && disconnected && !returned && Time.unscaledTime>=returnAt)
            {returned=true;session.StartClient();return;}
            var self=session.LocalActor;if(self==null) return;
            if(!traveled && (Host && session.World.actors.Count(a=>a.player && a.connected)==runtime.Options.ProbePeers || Source && phase>=1))
            {traveled=true;session.Travel.RequestFactory();}
            var npc=session.World.Find("npc:training-guard");
            if(Source && !disconnected && npc.owner==session.LocalOwner && npc.attackBusy && npc.attackRemaining<.6f && npc.attackRemaining>.1f)
            {disconnected=true;returnAt=Time.unscaledTime+3.5f;session.StopClient();return;}
            if(!Host) return;
            npc=session.ServerWorld.Find(npc.id);
            var source=session.ServerWorld.Find("player:session-client-1");
            if(phase==0 && npc.attackBusy && npc.owner==session.LocalOwner)
            {firstAttack=npc.attackId;phase=1;NetworkServer.SendToAll(new NpcProbePhase {phase=1});}
            if(phase!=1) return;
            if(source?.level==npc.level && npc.attackBusy && npc.attackId==firstAttack && npc.owner==session.LocalOwner) deferred=true;
            if(source!=null && npc.owner==source.owner && source.connected)
            {
                if(!transferred)
                {
                    if(npc.attackBusy || npc.attackCooldown<=0)
                    {Finish(false,"NPC передан до завершения атаки или без паузы.");return;}
                    transferred=true;
                }
                if(npc.attackBusy) canceledAttack=npc.attackId;
            }
            if(transferred && source?.connected==false && !canceled)
            {
                if(npc.owner!=session.LocalOwner || npc.attackBusy || npc.attackCooldown<=0 || string.IsNullOrEmpty(canceledAttack))
                {Finish(false,"Незавершённая атака не отменена при отключении владельца.");return;}
                canceled=true;canceledAt=Time.unscaledTime;
            }
            if(canceled && shots.ContainsKey(canceledAttack)) {Finish(false,"Выстрел отменённой атаки дошёл после обрыва.");return;}
            if(canceled && shots.Any(s=>s.Value>canceledAt && s.Value-canceledAt<1.5f)) {Finish(false,"После передачи потеряна пауза между атаками.");return;}
            if(!canceled || source?.connected!=true || !shots.Any(s=>s.Value>canceledAt)) return;
            bool valid=deferred && shots.ContainsKey(firstAttack) && source.down && source.level=="factory:0" && session.ServerWorld.actors.Count(a=>a.player && a.connected)==runtime.Options.ProbePeers &&
                session.ServerWorld.actors.Where(a=>a.player && a.character!="session-host" && a.character!="session-client-1").All(a=>a.level=="hub" && a.health==100);
            phase=2;
            NetworkServer.SendToAll(new NpcProbeFinish {success=valid,detail=valid?
                "Обычный NPC: атака завершена прежним владельцем, пауза передана; обрыв отменил следующую атаку, игрок вернулся в своё тело, хаб изолирован.":
                "Не подтверждены задержанная передача, возвращение в тело или изоляция хаба."});
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
