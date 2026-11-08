using System;
using System.Collections;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using UnityEngine;

namespace Remains.Runtime
{
    public sealed class NetworkGrenadeTelekinesisProbe : MonoBehaviour
    {
        private const string Catcher="player:session-host", Thrower="player:session-client-1";
        private const float StalePose=999;
        private const int StartPhase=1,ReadyPhase=2,FlightSeenPhase=3,HoldSeenPhase=4,MotionSeenPhase=5;
        private PrototypeRuntime runtime;
        private readonly Evidence evidence=new();
        private float deadline,settledAt,readyAt=-1;
        private bool start,threw,captureRequested,release,finished,staleSeen;
        private bool peerReady,peerFlightSeen,peerHoldSeen,peerMotionSeen;
        private bool Host=>runtime.Options.Role=="host";
        private bool Expire=>runtime.Options.Smoke.Contains("-expire-");

        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+60;runtime.CommandSource=Command;
            runtime.Interactions.GrenadeUpdated+=grenade=>{if(grenade.x==StalePose) staleSeen=true;};
            runtime.Session.ServerReady+=()=>runtime.Session.RegisterServerHandler<GrenadeProbePhase>((connection,message)=>
            {
                if(runtime.Session.ConnectedActor(connection)?.id!=Thrower)return;
                if(message.phase==ReadyPhase) {peerReady=true;return;}
                var grenade=runtime.Session.ServerWorld?.grenades.Find(candidate=>candidate.attacker==Thrower &&
                    (evidence.id==null || candidate.id==evidence.id));
                if(grenade==null)return;
                if(message.phase==FlightSeenPhase && grenade.generation==1 && grenade.holder==null &&
                    grenade.owner==runtime.Session.ServerWorld.Find(Thrower)?.owner)peerFlightSeen=true;
                if(!peerFlightSeen || grenade.generation!=2 || grenade.holder!=Catcher ||
                    grenade.owner!=runtime.Session.ServerWorld.Find(Catcher)?.owner)return;
                if(message.phase==HoldSeenPhase)peerHoldSeen=true;
                if(message.phase==MotionSeenPhase && peerHoldSeen && evidence.holdFuse-grenade.remaining>.4f &&
                    Vector2.Distance(evidence.holdPosition,grenade.Position)>1)peerMotionSeen=true;
            });
            runtime.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<GrenadeProbePhase>(message=>{if(message.phase==StartPhase)start=true;});
                NetworkClient.RegisterHandler<GrenadeProbeFinish>(_=>Finish(Validate(),"Сетевой сценарий завершён."));
            };
        }

        private PlayerCommand Command()
        {
            var actor=runtime.Session.LocalActor;
            if(!start || actor==null || finished) return default;
            if(!Host && !threw)
            {
                threw=true;evidence.originalOwner=actor.owner;
                return new PlayerCommand {grenade=true,hasAim=true,aim=actor.Position+Vector2.up*3};
            }
            if(!Host || release) return default;
            var flight=runtime.Grenades.Find(evidence.id);
            if(flight==null || flight.state.remaining<=0) return default;
            if(!captureRequested)
            {
                if(!peerFlightSeen || flight.state.Velocity.sqrMagnitude<1 || flight.state.remaining<1.5f) return default;
                captureRequested=true;evidence.airborneSpeed=flight.state.Velocity.magnitude;
                return new PlayerCommand {telekinesis=true,hasAim=true,aim=flight.position};
            }
            // Keep the real grenade at its captured position until the peer has recorded that live state.
            // Its fuse continues normally; no state or completed evidence is sent in the phase message.
            return new PlayerCommand {telekinesis=true,hasAim=true,aim=peerHoldSeen?actor.Position+new Vector2(4.5f,1.2f):flight.position};
        }

        private void Update()
        {
            if(finished) return;
            var session=runtime.Session;
            var world=Host?session.ServerWorld:session.World;
            if(Time.unscaledTime>deadline)
            {
                Finish(false,$"Таймаут. Связь: {NetworkClient.isConnected}. Старт: {start}. Бросок: {threw}. Захват: {captureRequested}. Отпускание: {release}. Peer ready/flight/hold/motion: {peerReady}/{peerFlightSeen}/{peerHoldSeen}/{peerMotionSeen}.");
                return;
            }
            if(world==null || session.World==null) return;
            if(!Host && !evidence.readySent)
            {
                // Finish entry preparation and give its asynchronous asset cleanup a quiet second before throwing.
                // This consumes only the existing bounded startup budget; no grenade exists yet.
                if(session.LocalActor==null || session.EntryPreparation.ClientPending || !runtime.LevelPresentationReady)readyAt=-1;
                else if(readyAt<0)readyAt=Time.unscaledTime;
                else if(Time.unscaledTime-readyAt>=1)
                {evidence.readySent=true;session.SendToServer(new GrenadeProbePhase {phase=ReadyPhase});}
            }
            if(Host && !start && peerReady && runtime.LevelPresentationReady && !session.EntryPreparation.ClientPending &&
                session.World.Find(Catcher)?.connected==true && session.World.Find(Thrower)?.connected==true)
            {
                start=true;NetworkServer.SendToAll(new GrenadeProbePhase {phase=StartPhase});
            }
            var grenade=world.grenades.Find(value=>value.attacker==Thrower);
            if(grenade!=null)
            {
                Observe(grenade,world);
                return;
            }
            var blast=world.explosions.Find(value=>value.attack.id==evidence.id);
            if(blast==null || !Host || settledAt<0) return;
            if(settledAt==0)
            {
                settledAt=Time.unscaledTime;
                // Replay the real notification. It must not duplicate visual effects or persistent victim decisions.
                NetworkServer.SendToAll(AttackMessage.From(blast.attack));
                return;
            }
            if(Time.unscaledTime-settledAt<1.25f) return;
            settledAt=-1;
            NetworkServer.SendToAll(new GrenadeProbeFinish());
        }

        private void Observe(GrenadeState grenade,WorldState world)
        {
            evidence.id=grenade.id;
            var flight=runtime.Grenades.Find(grenade.id);
            if(flight!=null)
            {
                if(ReferenceEquals(flight.state,grenade))
                {Finish(false,"Локальная симуляция гранаты изменяет канонический объект мира.");return;}
                evidence.detachedFlight=true;
            }
            if(Host) evidence.sharedWorld=ReferenceEquals(runtime.Session.World,runtime.Session.ServerWorld);
            if(grenade.x==StalePose) staleSeen=true;
            if(grenade.generation==1)
            {
                evidence.originalOwner=grenade.owner;
                evidence.originalGeneration=grenade.generation;
                if(!Host && !evidence.flightSeenSent)
                {evidence.flightSeenSent=true;runtime.Session.SendToServer(new GrenadeProbePhase {phase=FlightSeenPhase});}
            }
            if(grenade.holder==Catcher && grenade.owner==world.Find(Catcher)?.owner)
            {
                if(evidence.holdGeneration==0)
                {
                    evidence.holdGeneration=grenade.generation;evidence.heldOwner=grenade.owner;
                    evidence.holdPosition=grenade.Position;evidence.holdFuse=grenade.remaining;
                    if(Host)
                    {
                        var actor=world.Find(Catcher);
                        try
                        {
                            // Replay through the common request coordinator. On a shared host this must be a
                            // registered queued authority command; stale generation must still be rejected.
                            runtime.Session.SendToServer(new GrenadeHoldRequest {grenade=grenade.id,
                                generation=grenade.generation-1,actorGeneration=actor.generation,
                                actorLifecycle=actor.lifecycleSequence});
                            evidence.staleHoldReplaySent=true;
                        }
                        catch(Exception error) {Finish(false,"Координатор захвата гранаты: "+error.Message);return;}
                    }
                    else
                    {
                        NetworkClient.Send(new GrenadePoseMessage {id=grenade.id,generation=grenade.generation-1,
                            sequence=grenade.sequence+100,x=StalePose,y=StalePose,remaining=grenade.remaining});
                        evidence.stalePoseSent=true;
                        evidence.holdSeenSent=true;runtime.Session.SendToServer(new GrenadeProbePhase {phase=HoldSeenPhase});
                    }
                }
                evidence.lastHeldPosition=grenade.Position;evidence.lastHeldFuse=grenade.remaining;
                if(!Host && !evidence.motionSeenSent && evidence.holdFuse-grenade.remaining>.4f &&
                    Vector2.Distance(evidence.holdPosition,grenade.Position)>1)
                {evidence.motionSeenSent=true;runtime.Session.SendToServer(new GrenadeProbePhase {phase=MotionSeenPhase});}
                if(Host && !Expire && peerMotionSeen && evidence.holdFuse-grenade.remaining>.6f &&
                    Vector2.Distance(evidence.holdPosition,grenade.Position)>1) release=true;
            }
            if(grenade.holder==null && evidence.holdGeneration>0 && grenade.generation>evidence.holdGeneration)
            {
                if(evidence.releaseGeneration==0)
                {
                    evidence.releaseGeneration=grenade.generation;evidence.releaseOwner=grenade.owner;
                    evidence.releasePosition=grenade.Position;evidence.releaseFuse=grenade.remaining;
                }
                evidence.freeFlightDistance=Mathf.Max(evidence.freeFlightDistance,Vector2.Distance(grenade.Position,evidence.releasePosition));
            }
        }

        private bool Validate()
        {
            var world=runtime.Session.World;
            if(world==null || string.IsNullOrEmpty(evidence.id))
            {evidence.validationFailure="Нет мира или наблюдённой гранаты.";return false;}
            var catcher=world.Find(Catcher);var thrower=world.Find(Thrower);
            if(catcher==null || thrower==null)
            {evidence.validationFailure="Нет обоих участников сценария.";return false;}
            evidence.catcherGrenades=InventoryJournal.Count(catcher,"grenade");
            evidence.throwerGrenades=InventoryJournal.Count(thrower,"grenade");
            evidence.blastCount=world.explosions.Count(value=>value.attack.id==evidence.id);
            var blast=world.explosions.Find(value=>value.attack.id==evidence.id);
            evidence.blastGeneration=blast?.generation??0;
            evidence.visuals=runtime.Explosions.Shown;
            evidence.catcherReceipts=catcher.resolvedExplosions.Count(id=>id==evidence.id);
            evidence.throwerReceipts=thrower.resolvedExplosions.Count(id=>id==evidence.id);
            bool observed=Host?peerReady && peerFlightSeen && peerHoldSeen && peerMotionSeen:
                evidence.readySent && evidence.flightSeenSent && evidence.holdSeenSent && evidence.motionSeenSent;
            bool valid=observed && !staleSeen && evidence.detachedFlight && (!Host || evidence.sharedWorld==runtime.Session.SharedHostWorld && evidence.staleHoldReplaySent) &&
                (Host?evidence.airborneSpeed>1:evidence.stalePoseSent) &&
                evidence.holdGeneration==2 && evidence.originalGeneration==1 &&
                evidence.originalOwner!=evidence.heldOwner && evidence.holdFuse-evidence.lastHeldFuse>.4f &&
                Vector2.Distance(evidence.holdPosition,evidence.lastHeldPosition)>1 &&
                (Expire?evidence.releaseGeneration==0 && evidence.blastGeneration==evidence.holdGeneration:
                    evidence.releaseGeneration==evidence.holdGeneration+1 && evidence.blastGeneration==evidence.releaseGeneration &&
                    evidence.heldOwner==evidence.releaseOwner && evidence.releaseFuse<=evidence.lastHeldFuse+.001f &&
                    evidence.freeFlightDistance>.1f) &&
                evidence.blastCount==1 && blast.attack.attacker==Thrower && evidence.visuals==1 &&
                evidence.catcherReceipts==1 && evidence.throwerReceipts==1 &&
                evidence.throwerGrenades==4 && evidence.catcherGrenades==5 && world.grenades.Count==0 && WorldProps.Find(world,evidence.id)==null;
            if(!valid) evidence.validationFailure=$"Наблюдения подтверждены: {observed}; устаревшая поза: {staleSeen}; отдельный полёт: {evidence.detachedFlight}; "+
                $"поколения initial/held/release/blast: {evidence.originalGeneration}/{evidence.holdGeneration}/{evidence.releaseGeneration}/{evidence.blastGeneration}; "+
                $"перенос: {Vector2.Distance(evidence.holdPosition,evidence.lastHeldPosition):R}м; запал при удержании: {evidence.holdFuse-evidence.lastHeldFuse:R}с; "+
                $"полёт после отпускания: {evidence.freeFlightDistance:R}м; взрыв/эффект: {evidence.blastCount}/{evidence.visuals}; "+
                $"receipt catcher/thrower: {evidence.catcherReceipts}/{evidence.throwerReceipts}; инвентарь catcher/thrower: {evidence.catcherGrenades}/{evidence.throwerGrenades}.";
            return valid;
        }

        private void Finish(bool success,string detail)
        {
            if(finished) return;
            finished=true;evidence.success=success;evidence.detail=detail;evidence.expireWhileHeld=Expire;
            if(!success && !string.IsNullOrEmpty(evidence.validationFailure))evidence.detail+=" "+evidence.validationFailure;
            evidence.peerReady=peerReady;evidence.peerFlightSeen=peerFlightSeen;evidence.peerHoldSeen=peerHoldSeen;evidence.peerMotionSeen=peerMotionSeen;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(evidence,true));
            StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:1);}

        [Serializable] private sealed class Evidence
        {
            public bool success,expireWhileHeld,stalePoseSent,staleHoldReplaySent,detachedFlight,sharedWorld;
            public bool readySent,flightSeenSent,holdSeenSent,motionSeenSent,peerReady,peerFlightSeen,peerHoldSeen,peerMotionSeen;
            public string detail,validationFailure,id,originalOwner,heldOwner,releaseOwner;
            public int originalGeneration,holdGeneration,releaseGeneration,blastGeneration,blastCount,visuals,
                catcherReceipts,throwerReceipts,catcherGrenades,throwerGrenades;
            public float airborneSpeed,holdFuse,lastHeldFuse,releaseFuse,freeFlightDistance;
            public Vector2 holdPosition,lastHeldPosition,releasePosition;
        }
    }
}
