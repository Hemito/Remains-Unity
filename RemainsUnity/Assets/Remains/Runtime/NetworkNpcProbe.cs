using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Gameplay;
using Remains.Domain;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Runtime
{
    public struct NpcProbePhase : NetworkMessage {public int phase;}
    public struct NpcProbeFinish : NetworkMessage {public bool success;public string detail;}
    public struct NpcReplayProof : NetworkMessage {public string attack;}
    // Commands and connection lifecycle only; never writes actor state.
    public sealed class NetworkNpcProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private int phase;
        private float deadline,returnAt,canceledAt;
        private bool traveled,disconnected,returned,finished,deferred,transferred,canceled;
        private string firstAttack,canceledAttack;
        private bool fired,replayed;
        private bool combatMusicObserved;
        private bool perceptionHandoffVerified;
        private AttackEvent acceptedShot;
        private int replayProofs;
        private readonly Dictionary<string,float> shots=new();
        private readonly TrainingWeaponTrace weaponTrace=new("npc-shot");
        private InteractionVisuals interactionVisuals;
        private ProjectileVisuals projectileVisuals;
        private bool Host=>runtime.Options.Role=="host";
        private bool Source=>runtime.Options.Character=="session-client-1";
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+45;value.CommandSource=Command;
            interactionVisuals=FindFirstObjectByType<InteractionVisuals>();
            projectileVisuals=FindFirstObjectByType<ProjectileVisuals>();
            projectileVisuals.TrailDrawn+=ObserveTrail;
            value.Combat.NpcReplayRejected+=id=>{if(Source) NetworkClient.Send(new NpcReplayProof {attack=id});};
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<NpcReplayProof>((connection,message)=>
            {
                if(value.Session.ConnectedActor(connection)?.character=="session-client-1" && message.attack==acceptedShot?.id) replayProofs++;
            });
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<NpcProbePhase>(m=>phase=m.phase);
                NetworkClient.RegisterHandler<NpcProbeFinish>(m=>Finish(m.success,m.detail));
            };
            value.Session.AttackReceived+=attack=>
            {
                if(Host && attack.weapon=="energy" && attack.attacker=="player:session-host") acceptedShot=attack;
                if(attack.weapon!="npc-shot") return;
                weaponTrace.Observe(attack,value.Session.World?.Find(attack.attacker),attack.sourceOwner==value.Session.LocalOwner);
                if(shots.ContainsKey(attack.id)) {Finish(false,"NPC повторил ID выстрела.");return;}
                shots.Add(attack.id,Time.unscaledTime);
            };
        }
        private void ObserveTrail(ProjectileVisual view)=>weaponTrace.ObserveTrail(runtime.Session.World,view);
        private void LateUpdate()
        {
            if(!finished && runtime.Session.World!=null) weaponTrace.Draw(runtime.Session.World,interactionVisuals);
        }
        private void OnDestroy()
        {
            if(projectileVisuals!=null) projectileVisuals.TrailDrawn-=ObserveTrail;
        }
        private PlayerCommand Command()
        {
            if(!Host || fired || runtime.Session.LocalActor?.level!="factory:0") return default;
            fired=true;
            return new PlayerCommand {weapon="energy",fire=true,hasAim=true,
                aim=runtime.Session.World.Find("npc:training-guard").Position+Vector2.up*.55f};
        }
        private void Update()
        {
            if(finished) return;
            var session=runtime.Session;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут NPC: phase={phase}, deferred={deferred}, transfer={transferred}, cancel={canceled}, shots={shots.Count}.");return;}
            if(Source && disconnected && !returned && Time.unscaledTime>=returnAt)
            {returned=true;session.StartClient();return;}
            var self=session.LocalActor;if(self==null) return;
            if(self.level=="factory:0" && runtime.Music.CurrentTrack=="raider-combat") combatMusicObserved=true;
            if(self.level=="hub" && runtime.Music.CurrentTrack=="raider-combat")
                {Finish(false,"Музыка боя на заводе попала в хаб.");return;}
            if(!traveled && (Host && session.World.actors.Count(a=>a.player && a.connected)==runtime.Options.ProbePeers || Source && phase>=1))
            {traveled=true;session.Travel.RequestFactory();}
            var npc=session.World.Find("npc:training-guard");
            if(Source && !disconnected && npc.owner==session.LocalOwner && npc.attackBusy && npc.attackRemaining<.6f && npc.attackRemaining>.1f)
            {
                File.WriteAllText(runtime.Options.Report+".interrupted-attack.txt",npc.attackId);
                disconnected=true;returnAt=Time.unscaledTime+3.5f;session.StopClient();return;
            }
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
                    perceptionHandoffVerified=npc.perception.hasKnownPosition && npc.perception.remainingSeconds>0 &&
                        npc.target==source.id && npc.perception.candidate==source.id && npc.perception.exposureSeconds>0;
                    if(!perceptionHandoffVerified) {Finish(false,"При передаче владельца потеряны подтверждённая цель или память восприятия.");return;}
                    if(acceptedShot==null || npc.health!=132 || !npc.resolvedAttacks.Contains(acceptedShot.id))
                    {Finish(false,"Первое попадание по NPC не зафиксировано до передачи.");return;}
                    // Fault injection: replay one already accepted event to the new owner.
                    var repeat=new Remains.Networking.NpcHitMessage {target=npc.id,generation=npc.generation,
                        attackJson=Remains.Serialization.StateJson.ToJson(acceptedShot)};
                    session.ReportNpcHit(repeat);session.ReportNpcHit(repeat);replayed=true;
                }
                if(npc.attackBusy) canceledAttack=npc.attackId;
            }
            if(transferred && source?.connected==false && !canceled)
            {
                // Test evidence from the departing process, including an attack whose
                // ordinary replication was still in flight. Does not change gameplay state.
                string evidence=Path.Combine(Path.GetDirectoryName(runtime.Options.Report),"session-client-1-report.json.interrupted-attack.txt");
                canceledAttack=File.Exists(evidence)?File.ReadAllText(evidence):null;
                if(npc.owner!=session.LocalOwner || npc.attackBusy || npc.attackCooldown<=0 || string.IsNullOrEmpty(canceledAttack))
                {Finish(false,"Незавершённая атака не отменена при отключении владельца.");return;}
                canceled=true;canceledAt=Time.unscaledTime;
            }
            if(canceled && shots.ContainsKey(canceledAttack)) {Finish(false,"Выстрел отменённой атаки дошёл после обрыва.");return;}
            if(canceled && shots.Any(s=>s.Value>canceledAt && s.Value-canceledAt<1.5f)) {Finish(false,"После передачи потеряна пауза между атаками.");return;}
            if(!canceled || source?.connected!=true || !shots.Any(s=>s.Value>canceledAt)) return;
            bool valid=deferred && replayed && replayProofs>=2 && npc.health==132 && npc.resolvedAttacks.Count(id=>id==acceptedShot.id)==1 && shots.ContainsKey(firstAttack) && source.down==(source.health<=0) && source.level=="factory:0" && session.ServerWorld.actors.Count(a=>a.player && a.connected)==runtime.Options.ProbePeers &&
                session.ServerWorld.actors.Where(a=>a.player && a.character!="session-host" && a.character!="session-client-1").All(a=>a.level=="hub" && a.health==100);
            phase=2;
            NetworkServer.SendToAll(new NpcProbeFinish {success=valid,detail=valid?
                "NPC: атака завершена прежним владельцем, повтор принятого попадания после передачи не нанёс урон; обрыв отменил следующую атаку, игрок вернулся в своё тело, хаб изолирован.":
                $"Не подтверждён NPC: deferred={deferred}, replayProofs={replayProofs}, hp={npc.health}, down={source.down}, shots={shots.Count}."});
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            if(success && (Host || Source) && shots.Count>0 && !combatMusicObserved) {success=false;detail+=" Не включилась музыка боя.";}
            detail+=$" combatMusicObserved={combatMusicObserved}, perceptionHandoffVerified={perceptionHandoffVerified}.";
            weaponTrace.Save(runtime.Options.Report+".weapons.json");
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail},true));StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:1);}
        [Serializable] private sealed class Report {public bool success;public string detail;}
    }
}
