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
        private PrototypeRuntime runtime;
        private readonly Evidence evidence=new();
        private float deadline,settledAt;
        private bool start,threw,captureRequested,release,finished,staleSeen;
        private bool Host=>runtime.Options.Role=="host";
        private bool Expire=>runtime.Options.Smoke.Contains("-expire-");

        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+60;runtime.CommandSource=Command;
            runtime.Interactions.GrenadeUpdated+=grenade=>{if(grenade.x==StalePose) staleSeen=true;};
            runtime.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<GrenadeProbePhase>(_=>start=true);
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
                if(flight.state.Velocity.sqrMagnitude<1 || flight.state.remaining<1.5f) return default;
                captureRequested=true;evidence.airborneSpeed=flight.state.Velocity.magnitude;
                return new PlayerCommand {telekinesis=true,hasAim=true,aim=flight.position};
            }
            return new PlayerCommand {telekinesis=true,hasAim=true,aim=actor.Position+new Vector2(4.5f,1.2f)};
        }

        private void Update()
        {
            if(finished) return;
            var session=runtime.Session;
            var world=Host?session.ServerWorld:session.World;
            if(Time.unscaledTime>deadline)
            {
                Finish(false,$"Таймаут. Связь: {NetworkClient.isConnected}. Старт: {start}. Бросок: {threw}. Захват: {captureRequested}. Отпускание: {release}.");
                return;
            }
            if(world==null || session.World==null) return;
            if(Host && !start && session.World.Find(Catcher)?.connected==true && session.World.Find(Thrower)?.connected==true)
            {
                start=true;NetworkServer.SendToAll(new GrenadeProbePhase {phase=1});
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
                    }
                }
                evidence.lastHeldPosition=grenade.Position;evidence.lastHeldFuse=grenade.remaining;
                if(Host && !Expire && evidence.holdFuse-grenade.remaining>.6f &&
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
            if(world==null || string.IsNullOrEmpty(evidence.id)) return false;
            var catcher=world.Find(Catcher);var thrower=world.Find(Thrower);
            evidence.catcherGrenades=InventoryJournal.Count(catcher,"grenade");
            evidence.throwerGrenades=InventoryJournal.Count(thrower,"grenade");
            evidence.blastCount=world.explosions.Count(value=>value.attack.id==evidence.id);
            var blast=world.explosions.Find(value=>value.attack.id==evidence.id);
            evidence.blastGeneration=blast?.generation??0;
            evidence.visuals=runtime.Explosions.Shown;
            evidence.catcherReceipts=catcher.resolvedExplosions.Count(id=>id==evidence.id);
            evidence.throwerReceipts=thrower.resolvedExplosions.Count(id=>id==evidence.id);
            return !staleSeen && evidence.detachedFlight && (!Host || evidence.sharedWorld==runtime.Session.SharedHostWorld && evidence.staleHoldReplaySent) &&
                (Host?evidence.airborneSpeed>1:evidence.stalePoseSent) &&
                evidence.holdGeneration==2 && (!Host || evidence.originalGeneration==1) &&
                evidence.originalOwner!=evidence.heldOwner && evidence.holdFuse-evidence.lastHeldFuse>.4f &&
                Vector2.Distance(evidence.holdPosition,evidence.lastHeldPosition)>1 &&
                (Expire?evidence.releaseGeneration==0 && evidence.blastGeneration==evidence.holdGeneration:
                    evidence.releaseGeneration==evidence.holdGeneration+1 && evidence.blastGeneration==evidence.releaseGeneration &&
                    evidence.heldOwner==evidence.releaseOwner && evidence.releaseFuse<=evidence.lastHeldFuse+.001f &&
                    evidence.freeFlightDistance>.1f) &&
                evidence.blastCount==1 && blast.attack.attacker==Thrower && evidence.visuals==1 &&
                evidence.catcherReceipts==1 && evidence.throwerReceipts==1 &&
                evidence.throwerGrenades==4 && evidence.catcherGrenades==5 && world.grenades.Count==0 && WorldProps.Find(world,evidence.id)==null;
        }

        private void Finish(bool success,string detail)
        {
            if(finished) return;
            finished=true;evidence.success=success;evidence.detail=detail;evidence.expireWhileHeld=Expire;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(evidence,true));
            StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:1);}

        [Serializable] private sealed class Evidence
        {
            public bool success,expireWhileHeld,stalePoseSent,staleHoldReplaySent,detachedFlight,sharedWorld;
            public string detail,id,originalOwner,heldOwner,releaseOwner;
            public int originalGeneration,holdGeneration,releaseGeneration,blastGeneration,blastCount,visuals,
                catcherReceipts,throwerReceipts,catcherGrenades,throwerGrenades;
            public float airborneSpeed,holdFuse,lastHeldFuse,releaseFuse,freeFlightDistance;
            public Vector2 holdPosition,lastHeldPosition,releasePosition;
        }
    }
}
