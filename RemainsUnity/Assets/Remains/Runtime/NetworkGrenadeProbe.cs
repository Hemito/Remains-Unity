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
        private float heldAt;
        private Vector2 plannedOrigin;
        private bool heldLaunchVerified;
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
                if(Host) NetworkServer.SendToAll(new Remains.Networking.AttackMessage {json=JsonUtility.ToJson(attack)});
            };
            value.Explosions.Created+=effect=>
            {if(Host && value.Options.Smoke.Contains("-frames-")) StartCoroutine(CaptureExplosion(effect));};
            value.Grenades.Created+=flight=>
            {
                if(Held && Source && !heldLaunchVerified)
                {
                    heldLaunchVerified=Vector2.Distance(flight.position,plannedOrigin)<.0001f &&
                        flight.state.rotationVersion==1 && Mathf.Abs(flight.state.angularVelocity)>1;
                    if(!heldLaunchVerified) Finish(false,"Граната выпущена не из удерживаемой позиции.");
                }
                if(Host && value.Options.Smoke.Contains("-frames-")) StartCoroutine(CaptureFlight());
            };
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<GrenadeProbePhase>(message=>phase=message.phase);
                NetworkClient.RegisterHandler<GrenadeProbeFinish>(_=>Finish(Validate(),Resume?"Новый запуск из снимка активной гранаты: запал продолжен, одна детонация, расход предмета не повторён.":"Граната пережила отключение автора, один владелец завершил взрыв; повторные снимки и возвращение автора не повторили урон."));
            };
        }
        private PlayerCommand Command()
        {
            if(Resume || phase!=1 || !Source || threw || runtime.Session.LocalActor==null) return default;
            // Keep the handoff/damage scenario above its support. A downward release
            // can legitimately pass through the one-way floor and miss these players.
            var actor=runtime.Session.LocalActor;var aim=actor.Position+Vector2.up*3;
            if(Held)
            {
                if(actor.weapon!="grenade") return new PlayerCommand {weaponItem=actor.inventory.First(item=>item.definition=="grenade").id,hasAim=true,aim=aim};
                if(heldAt==0) {heldAt=Time.unscaledTime;if(runtime.Options.Smoke.Contains("-frames-")) StartCoroutine(CaptureHeld());}
                if(Time.unscaledTime-heldAt<.5f) return new PlayerCommand {hasAim=true,aim=actor.Position+new Vector2(-3,1)};
                var launchPose=actor.Copy();WeaponPose.Aim(launchPose,aim);
                plannedOrigin=WeaponPose.Mount(launchPose);
            }
            threw=true;return new PlayerCommand {grenade=!Held,fire=Held,hasAim=true,aim=aim};
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
            if(phase==0 && session.World.actors.Count(a=>a.player && a.connected)==runtime.Options.ProbePeers)
            {phase=1;NetworkServer.SendToAll(new GrenadeProbePhase {phase=1});}
            if(phase!=1) return;
            var active=session.ServerWorld.grenades.FirstOrDefault();
            if(active!=null)
            {
                grenadeId=active.id;
                if(active.generation>1 && !Remains.Domain.ActorPresence.IsPresent(session.ServerWorld.Find("player:session-client-1")))
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
            if(world==null || world.grenades.Count!=0 || world.explosions.Count!=1 || Held && Source && !heldLaunchVerified) return false;
            var id=world.explosions[0].attack.id;
            return world.actors.Where(a=>a.player && a.connected).All(a=>!Resume && a.character=="session-client-1"?
                a.health==100 && !a.resolvedExplosions.Contains(id) && a.inventory.Where(i=>i.definition=="grenade").Sum(i=>i.count)==4:
                a.health==30 && a.resolvedExplosions.Count(x=>x==id)==1 && (a.character!="session-client-1" || a.inventory.Where(i=>i.definition=="grenade").Sum(i=>i.count)==4));
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            if(Held) detail+=" Проверен выбор стопки и бросок основной кнопкой из удерживаемой позиции.";
            int expectedSounds=!Resume && Source?0:1;
            if(Host && !Resume && runtime.GrenadeAudio.Played==0) success=false;
            detail+=$" Звуков столкновения гранаты: {runtime.GrenadeAudio.Played}; немедленных локальных контактов: {immediateContacts}; события повторены для проверки дедупликации.";
            if(runtime.Audio.ExplosionSounds!=expectedSounds) success=false;
            if(runtime.Explosions.Shown!=expectedSounds) success=false;
            detail+=$" Эффектов взрыва: {runtime.Explosions.Shown}, ожидалось {expectedSounds}, уведомление повторено для проверки дедупликации.";
            detail+=$" Звуков взрыва: {runtime.Audio.ExplosionSounds}, ожидалось {expectedSounds}; старое событие при возвращении не озвучивается.";
            var world=runtime.Session.World;string attack=world?.explosions.FirstOrDefault()?.attack.id;
            var evidence=new Report {success=success,detail=detail,held=Held,handoff=handoff,phase=phase,explosions=explosions,
                activeGrenades=world?.grenades.Count??-1,attack=attack,explosionPosition=world?.explosions.FirstOrDefault()?.attack.Origin??Vector2.zero,
                players=world?.actors.Where(a=>a.player).Select(a=>new PlayerEvidence {id=a.id,connected=a.connected,health=a.health,position=a.Position,
                    grenades=a.inventory.Where(i=>i.definition=="grenade").Sum(i=>i.count),receipts=a.resolvedExplosions.Count(id=>id==attack)}).ToArray()};
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(evidence,true));StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:1);}
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
        [Serializable] private sealed class Report {public bool success,held,handoff;public string detail,attack;public int phase,explosions,activeGrenades;public Vector2 explosionPosition;public PlayerEvidence[] players;}
    }
}
