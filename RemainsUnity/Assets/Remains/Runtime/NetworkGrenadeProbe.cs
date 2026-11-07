using System;
using System.Collections;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using Remains.Persistence;
using Remains.Presentation;
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
        private bool threw,disconnected,returned,finished,handoff,startRequested,checkpointSaved;
        private string grenadeId;
        private float heldAt;
        private Vector2 plannedOrigin;
        private bool heldLaunchVerified;
        private HeldLaunchEvidence heldLaunch;
        private InteractionVisuals interactionVisuals;
        private Func<string,int,Vector2?> resolveBlastPosition;
        private BlastEvidence blastEvidence;
        private int immediateContacts;
        private readonly System.Collections.Generic.Dictionary<string,int> contactFrames=new();
        private bool Held=>runtime.Options.Smoke.Contains("-held-");
        private readonly System.Collections.Generic.HashSet<string> explosionIds=new();
        private bool Resume=>runtime.Options.Smoke.Contains("-resume-");
        private bool Host=>runtime.Options.Role=="host";
        private bool Source=>runtime.Options.Character=="session-client-1";
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+45;value.CommandSource=Command;
            resolveBlastPosition=value.Combat.LocalExplosionPosition;
            value.Combat.LocalExplosionPosition=ObserveBlast;
            value.Combat.DamageApplied+=ObserveDamage;
            interactionVisuals=FindFirstObjectByType<InteractionVisuals>();
            File.WriteAllText(value.Options.Report+".hub-layout.json",Remains.Serialization.StateJson.ToJson(Remains.Domain.RoomLayout.Load("hub")));
            value.GrenadeAudio.Observed+=message=>
            {contactFrames[message.grenade]=Time.frameCount;if(Host) NetworkServer.SendToAll(message);}; // Repeat the cosmetic event to verify deduplication.
            value.Grenades.Impact+=(state,speed)=>
            {
                if(speed<value.GrenadeAudio.MinimumSpeed || !NetworkClient.isConnected) return;
                if(!contactFrames.TryGetValue(state.id,out int frame) || frame!=Time.frameCount)
                {Finish(false,"Локальный звук столкновения ожидает сетевой ответ.");return;}
                immediateContacts++;
            };
            value.Session.AttackReceived+=attack=>
            {
                if(attack.weapon!="grenade" || !explosionIds.Add(attack.id)) return;
                explosions++;
                // Replay only the notification. The persisted detonation is not repeated.
                if(Host) NetworkServer.SendToAll(Remains.Networking.AttackMessage.From(attack));
            };
            value.Explosions.Created+=effect=>
            {if(Host && value.Options.Smoke.Contains("-frames-")) StartCoroutine(CaptureExplosion(effect));};
            value.Grenades.Created+=flight=>
            {
                if(Held && Source && !Resume && !finished)
                {
                    if(heldLaunch!=null)
                    {
                        heldLaunch.createdCount++;
                        heldLaunch.createdFrame=Time.frameCount;heldLaunch.createdOrigin=flight.position;
                        heldLaunch.createdTransaction=flight.state.id;heldLaunch.rotationVersion=flight.state.rotationVersion;
                        heldLaunch.angularVelocity=flight.state.angularVelocity;heldLaunch.createdVelocity=new Vector2(flight.state.vx,flight.state.vy);
                    }
                    heldLaunchVerified=heldLaunch?.requestMatched==true && heldLaunch.createdCount==1 && flight.state.id==heldLaunch.transaction &&
                        Vector2.Distance(flight.position,plannedOrigin)<.0001f &&
                        flight.state.rotationVersion==1 && Mathf.Abs(flight.state.angularVelocity)>1;
                    if(!heldLaunchVerified) Finish(false,$"Граната выпущена не из удерживаемой позиции: request={heldLaunch?.transaction}, created={flight.state.id}, planned={plannedOrigin}, createdOrigin={flight.position}, body={heldLaunch?.body}, anchor={heldLaunch?.anchor}, pose={heldLaunch?.pose}/{heldLaunch?.poseFrame}, rotation={flight.state.rotationVersion}/{flight.state.angularVelocity:R}.");
                }
                if(Host && value.Options.Smoke.Contains("-frames-")) StartCoroutine(CaptureFlight());
            };
            value.Session.ClientReady+=()=>
            {
                // Mirror resets diagnostic events after scene Awake. Register only
                // at the actual client lifecycle boundary, including reconnects.
                NetworkDiagnostics.OutMessageEvent-=ObserveLaunchRequest;
                if(Source && Held && !Resume && !finished) NetworkDiagnostics.OutMessageEvent+=ObserveLaunchRequest;
                NetworkClient.RegisterHandler<GrenadeProbePhase>(message=>phase=message.phase);
                NetworkClient.RegisterHandler<GrenadeProbeFinish>(_=>Finish(Validate(),Resume?"Новый запуск из снимка активной гранаты: запал продолжен, одна детонация, расход предмета не повторён.":"Граната пережила отключение автора, один владелец завершил взрыв; повторные снимки и возвращение автора не повторили урон."));
            };
        }
        private PlayerCommand Command()
        {
            if(Resume || runtime.Session.LocalActor is not {} actor) return default;
            if(Host && phase==0 && Thrower is {} thrower)
            {
                // Stand in the outer half of the blast so the sole connected player survives the handoff.
                float target=thrower.x+WeaponCatalog.Get("grenade").range*5/6;
                return new PlayerCommand {movement=new Vector2(Mathf.Clamp((target-actor.x)*2,-1,1),0)};
            }
            if(phase!=1 || !Source || threw) return default;
            // Keep the handoff/damage scenario above its support. A downward release
            // can legitimately pass through the one-way floor and miss these players.
            var aim=actor.Position+Vector2.up*3;
            if(Held)
            {
                if(actor.weapon!="grenade") return new PlayerCommand {weaponItem=actor.inventory.First(item=>item.definition=="grenade").id,hasAim=true,aim=aim};
                if(heldAt==0) {heldAt=Time.unscaledTime;if(runtime.Options.Smoke.Contains("-frames-")) StartCoroutine(CaptureHeld());}
                if(Time.unscaledTime-heldAt<.5f) return new PlayerCommand {hasAim=true,aim=actor.Position+new Vector2(-3,1)};
            }
            threw=true;return new PlayerCommand {grenade=!Held,fire=Held,hasAim=true,aim=aim};
        }
        private ActorState Thrower=>runtime.Session.World?.Find("player:session-client-1");
        private bool ReadyToThrow()
        {
            if(Resume) return true;
            if(runtime.Session.LocalActor is not {} host || Thrower is not {} thrower) return false;
            float target=thrower.x+WeaponCatalog.Get("grenade").range*5/6;
            return Mathf.Abs(host.x-target)<.05f && Mathf.Abs(host.vx)<.15f && Mathf.Abs(host.y-thrower.y)<.05f;
        }
        private Vector2? ObserveBlast(string id,int generation)
        {
            var position=resolveBlastPosition?.Invoke(id,generation);
            var actor=runtime.Session.LocalActor;
            var explosion=runtime.Session.World?.explosions.Find(value=>value.attack.id==id && value.attack.weapon=="grenade");
            if(actor==null || explosion==null || blastEvidence!=null) return position;
            var attack=explosion.attack;
            var weapon=WeaponCatalog.Get(attack.weapon);
            var blast=position??attack.End;
            float distance=Anatomy.BlastDistance(actor,blast);
            float damage=ExplosionRules.Damage(weapon.damage,weapon.range,distance,id,actor.id);
            float protectedDamage=new TypeHitProtectionRule().Resolve(actor,attack,damage);
            blastEvidence=new BlastEvidence {attack=id,actor=actor.id,position=blast,distance=distance,
                initialHealth=actor.health,expectedHealth=Mathf.Max(0,actor.health-protectedDamage)};
            return position;
        }
        private void ObserveDamage(DamageResult hit)
        {
            if(blastEvidence==null || blastEvidence.attack!=hit.attack || blastEvidence.actor!=hit.actor) return;
            blastEvidence.applications++;blastEvidence.damage+=hit.amount;
        }
        private void ObserveLaunchRequest(NetworkDiagnostics.MessageInfo info)
        {
            if(finished || !Held || !Source || Resume || info.message is not ThrowRequest request) return;
            try
            {
                if(heldLaunch!=null) throw new InvalidOperationException("Зафиксирован второй запрос одного удерживаемого броска: "+request.transaction);
                // Send observes the real command after Movement.Step and Aim. A detached
                // actor has no bound body support and cannot predict this launch root.
                var actor=runtime.Session.LocalActor??throw new InvalidOperationException("Нет текущего игрока на границе запроса броска.");
                var support=ActorBodySupport.Sample(actor);plannedOrigin=HeldItemPose.Mount(actor);
                var renderer=interactionVisuals?.FindWeapon(actor.id)?.Artwork;
                var sprite=renderer?.sprite;var expected=Remains.Content.WeaponRigs.Prefab("grenade").Artwork.sprite;
                heldLaunch=new HeldLaunchEvidence {transaction=request.transaction,actor=actor.id,item=request.item,
                    world=runtime.Session.World.worldId,owner=actor.owner,connection=runtime.Session.LocalOwner,
                    generation=actor.generation,lifecycle=actor.lifecycleSequence,requestFrame=Time.frameCount,
                    level=actor.level,anchor=actor.Position,body=support.BodyPosition,bodyBound=support.IsBound,
                    bodyOutcome=support.Outcome.ToString(),pose=support.Pose,poseFrame=support.Frame,poseTime=actor.poseTime,
                    heldOffset=new Vector2(actor.heldItemPoseX,actor.heldItemPoseY),plannedOrigin=plannedOrigin,
                    requestOrigin=new Vector2(request.originX,request.originY),requestAngle=request.angle,
                    requestAngularVelocity=request.angularVelocity,aim=new Vector2(request.aimX,request.aimY),
                    requestVelocity=new Vector2(request.vx,request.vy)};
                if(sprite!=null)
                {
                    heldLaunch.sprite=sprite.name;heldLaunch.texture=sprite.texture.name;
                    heldLaunch.spriteInstance=sprite.GetInstanceID();heldLaunch.textureInstance=sprite.texture.GetInstanceID();
                    heldLaunch.rect=new Vector4(sprite.rect.x,sprite.rect.y,sprite.rect.width,sprite.rect.height);
                    heldLaunch.pivot=sprite.pivot;heldLaunch.pixelsPerUnit=sprite.pixelsPerUnit;
                    heldLaunch.textureWidth=sprite.texture.width;heldLaunch.textureHeight=sprite.texture.height;
                    heldLaunch.rendererEnabled=renderer.enabled && renderer.gameObject.activeInHierarchy && !renderer.forceRenderingOff && renderer.color.a>0;
                    heldLaunch.spriteMatchesPrefab=sprite==expected;
                }
                heldLaunch.requestMatched=info.count==1 && support.IsBound && actor.owner==runtime.Session.LocalOwner &&
                    actor.weapon=="grenade" && actor.equippedWeaponItem==request.item &&
                    actor.generation==request.generation && actor.lifecycleSequence==request.lifecycle && actor.level==request.level &&
                    heldLaunch.rendererEnabled && heldLaunch.spriteMatchesPrefab && Vector2.Distance(heldLaunch.requestOrigin,plannedOrigin)<.0001f;
                if(!heldLaunch.requestMatched)
                    throw new InvalidOperationException($"Запрос гранаты не соответствует фактической готовой позе: actor={actor.id}, item={request.item}/{actor.equippedWeaponItem}, origin={heldLaunch.requestOrigin}, mount={plannedOrigin}, body={support.BodyPosition}, anchor={actor.Position}, bound={support.IsBound}, pose={support.Pose}/{support.Frame}, sprite={heldLaunch.sprite}, rect={heldLaunch.rect}, ppu={heldLaunch.pixelsPerUnit:R}.");
            }
            catch(Exception error) {Finish(false,error.Message);}
        }
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут гранаты: phase={phase}, handoff={handoff}, explosions={explosions}, active={runtime.Session.World?.grenades.Count}, lobbyWaiting={runtime.Session.Lobby.ClientWaiting}, lobbyPlayers={runtime.Session.Lobby.ClientPlayers}, lobbyBusy={runtime.Session.Lobby.ClientBusy}, lobbyError={runtime.Session.Lobby.ClientError}, contentDifferences={runtime.Session.Lobby.ContentDifferences?.Count}.");return;}
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
            if(phase==0 && session.World.actors.Count(a=>a.player && a.connected)==runtime.Options.ProbePeers && ReadyToThrow())
            {phase=1;NetworkServer.SendToAll(new GrenadeProbePhase {phase=1});}
            if(phase!=1) return;
            var active=session.ServerWorld.grenades.FirstOrDefault();
            if(active!=null)
            {
                grenadeId=active.id;
                if(active.generation>1 && active.owner==session.LocalOwner && !ActorPresence.IsPresent(session.ServerWorld.Find("player:session-client-1")))
                {
                    handoff=true;
                    if(!checkpointSaved) CaptureActiveCheckpoint(session.ServerWorld);
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
        private void CaptureActiveCheckpoint(WorldState world)
        {
            // The runner can resume this directory. Using final character profiles with an
            // earlier world would import already applied damage into the restored hub.
            string directory=Path.Combine(Path.GetDirectoryName(runtime.Options.Report),"active-checkpoint");
            var captured=Remains.Serialization.StateJson.Copy(world);
            var host=captured.Find(runtime.Session.LocalActorId);
            var thrower=captured.Find("player:session-client-1");
            if(host==null || thrower==null || Mathf.Abs(host.x-runtime.Session.LocalActor.x)>.1f) return;
            var hostStore=new SaveStore(Path.Combine(directory,host.character+"-save"));
            var snapshot=hostStore.SaveWorld(captured);
            hostStore.SaveCharacter(host);
            new SaveStore(Path.Combine(directory,thrower.character+"-save")).SaveCharacter(thrower);
            File.WriteAllText(Path.Combine(directory,"session-host-report.json.active-world.json"),snapshot.Json);
            checkpointSaved=true;
        }
        private bool Validate()
        {
            var world=runtime.Session.World;
            if(world==null || world.grenades.Count!=0 || world.explosions.Count!=1 || Held && Source && !heldLaunchVerified ||
                Host && !Resume && !checkpointSaved) return false;
            var explosion=world.explosions[0];var id=explosion.attack.id;
            var players=world.actors.Where(a=>a.player && a.connected).ToArray();
            if(players.Length!=runtime.Options.ProbePeers) return false;
            foreach(var player in players)
            {
                bool thrower=player.character=="session-client-1",hit=Resume || !thrower;
                if(explosion.players.Contains(player.id)!=hit || player.resolvedExplosions.Count(x=>x==id)!=(hit?1:0) ||
                    player.resolvedAttacks.Count(x=>x==id)!=(hit?1:0) || player.inventory.Where(i=>i.definition=="grenade").Sum(i=>i.count)!=(thrower?4:5) ||
                    !hit && (player.health!=100 || player.down)) return false;
            }
            if(!Resume && Source) return blastEvidence==null;
            var actor=runtime.Session.LocalActor;
            return actor!=null && blastEvidence!=null && blastEvidence.attack==id && blastEvidence.actor==actor.id &&
                blastEvidence.initialHealth==100 && blastEvidence.expectedHealth<100 && (!Host || blastEvidence.expectedHealth>0) &&
                blastEvidence.applications==1 && Mathf.Abs(blastEvidence.damage-(100-blastEvidence.expectedHealth))<.001f &&
                Mathf.Abs(actor.health-blastEvidence.expectedHealth)<.001f && actor.down==(blastEvidence.expectedHealth==0);
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            NetworkDiagnostics.OutMessageEvent-=ObserveLaunchRequest;
            if(Held) detail+=" Проверен выбор стопки и бросок основной кнопкой из удерживаемой позиции.";
            int expectedSounds=!Resume && Source?0:1;
            if(Host && !Resume && runtime.GrenadeAudio.Played==0) success=false;
            detail+=$" Звуков столкновения гранаты: {runtime.GrenadeAudio.Played}; немедленных локальных контактов: {immediateContacts}; события повторены для проверки дедупликации.";
            if(runtime.Audio.ExplosionSounds!=expectedSounds) success=false;
            if(runtime.Explosions.Shown!=expectedSounds) success=false;
            detail+=$" Эффектов взрыва: {runtime.Explosions.Shown}, ожидалось {expectedSounds}, уведомление повторено для проверки дедупликации.";
            detail+=$" Звуков взрыва: {runtime.Audio.ExplosionSounds}, ожидалось {expectedSounds}; старое событие при возвращении не озвучивается.";
            var world=runtime.Session.World;string attack=world?.explosions.FirstOrDefault()?.attack.id;
            var evidence=new Report {success=success,detail=detail,held=Held,heldLaunch=heldLaunch,handoff=handoff,phase=phase,explosions=explosions,
                activeGrenades=world?.grenades.Count??-1,attack=attack,explosionPosition=world?.explosions.FirstOrDefault()?.attack.Origin??Vector2.zero,
                players=world?.actors.Where(a=>a.player).Select(a=>new PlayerEvidence {id=a.id,connected=a.connected,health=a.health,position=a.Position,
                    grenades=a.inventory.Where(i=>i.definition=="grenade").Sum(i=>i.count),receipts=a.resolvedExplosions.Count(id=>id==attack)}).ToArray(),blast=blastEvidence};
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(evidence,true));StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:1);}
        private void OnDestroy()
        {
            NetworkDiagnostics.OutMessageEvent-=ObserveLaunchRequest;
            if(runtime?.Combat==null) return;
            runtime.Combat.LocalExplosionPosition=resolveBlastPosition;
            runtime.Combat.DamageApplied-=ObserveDamage;
        }
        private IEnumerator CaptureExplosion(Remains.Presentation.ExplosionVisual effect)
        {
            yield return new WaitForSeconds(.12f);
            if(effect==null) {Finish(false,"Эффект взрыва исчез до снимка.");yield break;}
            NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+".explosion.png",runtime.Hud.GetComponent<Canvas>());
        }
        private IEnumerator CaptureFlight()
        {
            yield return new WaitForSeconds(.25f);
            if(!runtime.Grenades.Flights.Any()) {Finish(false,"Граната исчезла до снимка полёта.");yield break;}
            NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+".flight.png",runtime.Hud.GetComponent<Canvas>());
        }
        private IEnumerator CaptureHeld()
        {
            yield return new WaitForSeconds(.2f);
            var actor=runtime.Session.LocalActor;
            if(actor?.weapon!="grenade") {Finish(false,"Перед броском не выбрана граната.");yield break;}
            NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+".held.png",runtime.Hud.GetComponent<Canvas>());
        }
        [Serializable] private sealed class PlayerEvidence {public string id;public bool connected;public float health;public Vector2 position;public int grenades,receipts;}
        [Serializable] private sealed class BlastEvidence
        {public string attack,actor;public Vector2 position;public float distance,initialHealth,expectedHealth,damage;public int applications;}
        [Serializable] private sealed class HeldLaunchEvidence
        {
            public string transaction,createdTransaction,world,owner,connection,actor,item,level,bodyOutcome,pose,sprite,texture;
            public int generation,lifecycle,requestFrame,createdFrame,createdCount,poseFrame,rotationVersion,spriteInstance,textureInstance,textureWidth,textureHeight;
            public bool bodyBound,requestMatched,rendererEnabled,spriteMatchesPrefab;
            public float poseTime,pixelsPerUnit,requestAngle,requestAngularVelocity,angularVelocity;
            public Vector2 anchor,body,heldOffset,plannedOrigin,requestOrigin,createdOrigin,requestVelocity,createdVelocity,aim,pivot;
            public Vector4 rect;
        }
        [Serializable] private sealed class Report {public bool success,held,handoff;public string detail,attack;public int phase,explosions,activeGrenades;public Vector2 explosionPosition;public PlayerEvidence[] players;public HeldLaunchEvidence heldLaunch;public BlastEvidence blast;}
    }
}
