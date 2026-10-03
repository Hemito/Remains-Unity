using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using UnityEngine;

namespace Remains.Runtime
{
    public struct PauseProbePhase : NetworkMessage {public int phase;}
    public struct PauseProbeAck : NetworkMessage {public int phase;}
    public struct PauseProbeFinish : NetworkMessage { }
    // Uses ordinary player commands and session requests, with per-client simulation evidence.
    public sealed class NetworkPauseProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private readonly HashSet<int> acknowledgements=new();
        private int phase=-1,hostPhase=-1,acknowledged=-1;
        private float deadline;
        private bool finished;
        private bool fired,reloaded,sampling;
        private int frozenSamples,activeSamples;
        private string failure;
        private bool menuCommand,inventoryCommand,uiBusy;
        private bool DeferredScenario=>runtime.Options.Smoke.Contains("-deferred-");
        private bool ReconnectScenario=>runtime.Options.Smoke.Contains("-reconnect-");
        private bool observedDisconnected,reconnected;
        private float healthBeforeLate;
        private int damageBeforeLate;
        private bool observedDeferred;
        private bool First=>runtime.Session.CharacterId=="session-client-1";
        private bool Host=>runtime.Options.Role=="host";
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+55;
            value.CommandSource=Command;
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<PauseProbeAck>((connection,message)=>
                {
                    if(message.phase==hostPhase) acknowledgements.Add(connection.connectionId);
                    Debug.Log($"Проверка паузы: ack connection={connection.connectionId}, phase={message.phase}, expected={hostPhase}, count={acknowledgements.Count}.");
                });
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<PauseProbePhase>(Receive);
                NetworkClient.RegisterHandler<PauseProbeFinish>(_=>Finish(true));
            };
        }
        private PlayerCommand Command()
        {
            var actor=runtime.Session.LocalActor;
            if(actor==null) return default;
            if(menuCommand) {menuCommand=false;return new PlayerCommand {menu=true};}
            if(inventoryCommand) {inventoryCommand=false;return new PlayerCommand {inventory=true};}
            var result=new PlayerCommand {movement=Vector2.right*.15f,hasAim=true,aim=actor.Position+Vector2.right*20};
            if(First && phase==1 && actor.level=="factory:0" && !runtime.Session.Travel.Pending)
            {
                if(!fired) {result.fire=true;fired=true;}
            }
            if(First && phase==2 && !reloaded) {result.reload=true;result.grenade=true;reloaded=true;}
            return result;
        }
        private IEnumerator Start()
        {
            if(!Host) yield break;
            while(runtime.Session.World==null || runtime.Session.World.actors.Count(a=>a.player && a.connected)!=runtime.Options.ProbePeers) yield return null;
            for(int step=0;step<=(ReconnectScenario?14:11) && !finished;step++)
            {
                hostPhase=step;acknowledgements.Clear();
                Debug.Log($"Проверка паузы: начало phase={step}, peers={runtime.Options.ProbePeers}.");
                if(step==6) yield return ConfigureSettings(true,false);
                if(step==8) yield return ConfigureSettings(false,true);
                if(step==11) yield return ConfigureSettings(false,false);
                if(step==12) yield return ConfigureSettings(true,false);
                if(step==14) yield return ConfigureSettings(false,false);
                NetworkServer.SendToAll(new PauseProbePhase {phase=step});
                while(acknowledgements.Count!=runtime.Options.ProbePeers && !finished) yield return null;
                if(step==2 && DeferredScenario) InjectLateAttack();
            }
            if(!finished) NetworkServer.SendToAll(new PauseProbeFinish());
        }
        private void InjectLateAttack()
        {
            // Fault fixture: deliver the same already-created shot twice while the level is paused.
            // Uses the real relay and receiver, but intentionally bypasses new-shot input/ammo spending.
            var session=runtime.Session;var target=session.ServerWorld.Find("player:session-client-1");
            var attack=new AttackEvent {id="pause-late-shot",attacker=session.LocalActorId,weapon="pistol",level="factory:0",
                originX=target.x-1,originY=target.y+.7f,endX=target.x+1,endY=target.y+.7f};
            var definition=Remains.Content.WeaponCatalog.Get(attack.weapon);
            ProjectileLaunch.Prepare(attack,definition,definition.projectileSpeed);
            var message=new AttackMessage {json=Remains.Serialization.StateJson.ToJson(attack)};
            // Inject delivery, not a new firing command: normal Fire correctly rejects paused input.
            NetworkClient.Send(message);NetworkClient.Send(message);
        }
        private void Receive(PauseProbePhase message)
        {
            phase=message.phase;
            var session=runtime.Session;
            if(phase==2 && First) {healthBeforeLate=session.LocalActor.health;damageBeforeLate=session.LocalActor.damageSequence;}
            if(phase==1 && (Host || First)) session.Travel.RequestFactory();
            if(phase==2 && First) StartCoroutine(ManualPause(true));
            if(phase==3 && Host) session.Travel.RequestHub();
            if(phase==4 && Host)
            {
                // Bypass only the optimistic local gate; host must still reject an actual travel request.
                var actor=session.LocalActor;
                NetworkClient.Send(new TravelRequest {id="pause-probe-blocked",source=actor.level,destination="factory:0",
                    generation=actor.generation,lifecycle=actor.lifecycleSequence});
                StartCoroutine(AcknowledgeBlockedEntry());
            }
            if(phase==5 && First) StartCoroutine(ManualPause(false));
            if(phase==6 && First) StartCoroutine(OpenAutomaticMenu());
            if(phase==7 && First) StartCoroutine(SwitchToInventory());
            if(phase==9 && First) StartCoroutine(ExitPausedLevel());
            if(phase==10 && First) session.Travel.RequestFactory();
            if(phase==12 && First) StartCoroutine(OpenAutomaticMenu());
            if(phase==13 && First) StartCoroutine(DisconnectAndReturn());
        }
        private IEnumerator DisconnectAndReturn()
        {
            uiBusy=true;
            var session=runtime.Session;var original=session.LocalActor.Copy();
            session.StopClient();
            yield return new WaitForSecondsRealtime(1.5f);
            session.StartClient();
            while(session.LocalActor==null && !finished) yield return null;
            if(finished) yield break;
            var resumed=session.LocalActor;
            if(resumed.id!=original.id || resumed.level!=original.level || resumed.generation<=original.generation ||
                !resumed.down || resumed.Position!=original.Position || resumed.health>original.health ||
                session.World.actors.Count(actor=>actor.character==original.character)!=1 ||
                runtime.Hud.InventoryOpen || runtime.Hud.PauseMenu.IsOpen)
            {failure="Возвращение после автопаузы изменило тело/позицию либо сохранило старый интерфейс";Finish(false);yield break;}
            reconnected=true;uiBusy=false;
        }
        private IEnumerator OpenAutomaticMenu()
        {uiBusy=true;yield return OpenMenu();uiBusy=false;}
        private IEnumerator SwitchToInventory()
        {
            uiBusy=true;yield return CloseMenu();inventoryCommand=true;
            while(!runtime.Hud.InventoryOpen && !finished) yield return null;
            uiBusy=false;
        }
        private IEnumerator ExitPausedLevel()
        {
            uiBusy=true;yield return OpenMenu();
            yield return UiPointerProbe.Click(runtime.Hud.PauseMenu.ExitToHubRect);
            while((runtime.Session.LocalActor?.level!="hub" || runtime.Session.Travel.Pending) && !finished) yield return null;
            if(runtime.Hud.InventoryOpen || runtime.Hud.PauseMenu.IsOpen)
            {failure="Переход сохранил старый открытый интерфейс";Finish(false);}
            uiBusy=false;
        }
        private IEnumerator OpenMenu()
        {
            if(runtime.Hud.PauseMenu.IsOpen) yield break;
            menuCommand=true;
            while(!runtime.Hud.PauseMenu.IsOpen && !finished) yield return null;
        }
        private IEnumerator CloseMenu()
        {
            if(!runtime.Hud.PauseMenu.IsOpen) yield break;
            menuCommand=true;
            while(runtime.Hud.PauseMenu.IsOpen && !finished) yield return null;
        }
        private IEnumerator ManualPause(bool paused)
        {
            uiBusy=true;
            if(paused) {while(!reloaded && !finished) yield return null;yield return null;}
            yield return OpenMenu();
            yield return UiPointerProbe.Click(paused?runtime.Hud.PauseMenu.ManualPauseRect:runtime.Hud.PauseMenu.ResumeRect);
            while(runtime.Session.Pause.IsPaused("factory:0")!=paused && !finished) yield return null;
            yield return CloseMenu();uiBusy=false;
        }
        private IEnumerator ConfigureSettings(bool menu,bool inventory)
        {
            yield return OpenMenu();
            if(runtime.Session.Pause.PauseOnMenu!=menu)
            {
                yield return UiPointerProbe.Click(runtime.Hud.PauseMenu.MenuSettingRect);
                while(runtime.Session.Pause.PauseOnMenu!=menu && !finished) yield return null;
            }
            if(runtime.Session.Pause.PauseOnInventory!=inventory)
            {
                yield return UiPointerProbe.Click(runtime.Hud.PauseMenu.InventorySettingRect);
                while(runtime.Session.Pause.PauseOnInventory!=inventory && !finished) yield return null;
            }
            yield return CloseMenu();
        }
        private IEnumerator AcknowledgeBlockedEntry()
        {
            yield return new WaitForSecondsRealtime(1.5f);
            if(runtime.Session.ServerWorld.Find(runtime.Session.LocalActorId).level!="hub") {Finish(false);yield break;}
            Acknowledge();
        }
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false);return;}
            var session=runtime.Session;
            if(Host && phase==13 && session.ServerWorld?.Find("player:session-client-1") is {connected:false,down:true,level:"factory:0"} && !session.Pause.IsServerPaused("factory:0"))
                observedDisconnected=true;
            if(uiBusy || phase<0 || acknowledged==phase || session.LocalActor==null || session.Travel.Pending || session.Pause.Revision<0) return;
            if(phase==4 && Host) return;
            bool expectedPause=phase is 2 or 3 or 4 or 6 or 8 or 12;
            bool expectedMenu=phase is 6 or 7 or 12 or 13;
            bool expectedInventory=phase is 8 or 9 or 10;
            string expectedLevel=First && phase>=1 && phase!=9 || Host && phase is 1 or 2?"factory:0":"hub";
            if(session.LocalActor.level!=expectedLevel || session.Pause.IsPaused("factory:0")!=expectedPause || session.Pause.IsPaused("hub") ||
                session.Pause.PauseOnMenu!=expectedMenu || session.Pause.PauseOnInventory!=expectedInventory) return;
            if(DeferredScenario && First && phase is 3 or 4)
            {
                var flights=session.World.projectiles.Where(p=>p.launch.id=="pause-late-shot").ToArray();
                if(flights.Length==0) return;
                if(flights.Length!=1 || flights[0].finished || flights[0].traveled!=0)
                {failure="Повтор запуска создал второй полёт либо пауза продвинула снаряд";Finish(false);return;}
                // Host left this level at phase 3: the remaining client must continue the flight.
                if(flights[0].owner!=session.LocalOwner) return;
                if(session.LocalActor.health!=healthBeforeLate || session.LocalActor.damageSequence!=damageBeforeLate)
                {failure="Отложенное попадание изменило здоровье во время паузы";Finish(false);return;}
                observedDeferred=true;
            }
            if(DeferredScenario && First && phase==5)
            {
                if(session.LocalActor.damageSequence==damageBeforeLate) return;
                if(!observedDeferred || session.LocalActor.damageSequence!=damageBeforeLate+1 || session.LocalActor.health!=healthBeforeLate-11 || runtime.Combat.PendingHitCount!=0)
                {failure="Возобновление не применило ровно один отложенный урон";Finish(false);return;}
            }
            if(phase==1 && First && !fired) return;
            if(First && phase==6 && !runtime.Hud.PauseMenu.IsOpen) return;
            if(First && phase==12 && !runtime.Hud.PauseMenu.IsOpen) return;
            if(phase==13 && (First && !reconnected || Host && !observedDisconnected)) return;
            if(First && phase is 7 or 8 && (!runtime.Hud.InventoryOpen || runtime.Hud.PauseMenu.IsOpen)) return;
            if(phase is 2 or 3 or 6 or 7 or 8 or 12)
            {if(!sampling) {sampling=true;StartCoroutine(SampleSimulation());}return;}
            Acknowledge();
        }
        private float[] Snapshot()
        {
            var session=runtime.Session;var actor=session.LocalActor;
            var values=new List<float> {actor.x,actor.y,actor.vx,actor.vy,actor.poseTime,actor.weaponAngle,
                runtime.Combat.ShotCooldownRemaining,runtime.Combat.Reload.Remaining,runtime.Revival.Progress,actor.mana};
            foreach(var npc in session.World.actors.Where(a=>!a.player && a.owner==session.LocalOwner && a.level==actor.level).OrderBy(a=>a.id))
                values.AddRange(new[]{npc.x,npc.y,npc.vx,npc.vy,npc.poseTime,npc.attackRemaining,npc.attackCooldown});
            foreach(var flight in runtime.Grenades.Flights.Where(f=>f.state.level==actor.level).OrderBy(f=>f.state.id))
                values.AddRange(new[]{flight.state.x,flight.state.y,flight.state.vx,flight.state.vy,flight.state.remaining});
            foreach(var flight in runtime.Combat.Projectiles.Flights.Where(f=>f.state.launch.level==actor.level).OrderBy(f=>f.state.launch.id))
                values.AddRange(new[]{flight.path.Traveled,flight.finishedAge});
            foreach(var prop in session.World.props.Where(p=>p.level==actor.level && p.owner==session.LocalOwner).OrderBy(p=>p.id))
                values.AddRange(new[]{prop.x,prop.y,prop.vx,prop.vy});
            return values.ToArray();
        }
        private IEnumerator SampleSimulation()
        {
            yield return new WaitForSecondsRealtime(.8f); // Allow already sent pose packets to settle.
            bool paused=runtime.Session.Pause.IsPaused(runtime.Session.LocalActor.level);
            var before=Snapshot();
            if(First && phase is 2 or 3 && (!runtime.Combat.Reload.Active || !runtime.Grenades.Flights.Any(f=>f.state.remaining>0)))
            {failure="Не создана незавершённая перезарядка/граната перед паузой";Finish(false);yield break;}
            yield return new WaitForSecondsRealtime(.8f);
            var after=Snapshot();
            if(paused && !before.SequenceEqual(after)) {failure="Симуляция изменилась во время паузы";Finish(false);yield break;}
            if(!paused && First && phase==7 && after[4]<=before[4]+.1f)
            {failure="Инвентарь остановил время при выключенной настройке";Finish(false);yield break;}
            if(!paused && !(First && phase==7) && after[0]<=before[0]+.1f) {failure="Активный хаб не продолжает движение";Finish(false);yield break;}
            if(paused) frozenSamples++;else activeSamples++;
            Acknowledge();sampling=false;
        }
        private void Acknowledge()
        {
            if(phase==2 && First) {healthBeforeLate=runtime.Session.LocalActor.health;damageBeforeLate=runtime.Session.LocalActor.damageSequence;}
            acknowledged=phase;NetworkClient.Send(new PauseProbeAck {phase=phase});
        }
        private void Finish(bool success)
        {
            if(finished) return;finished=true;exitCode=success?0:1;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,
                detail=$"Пауза: phase={phase}, hostPhase={hostPhase}, ack={acknowledged}, ackCount={acknowledgements.Count}, frozenSamples={frozenSamples}, activeSamples={activeSamples}, deferred={observedDeferred}, disconnected={observedDisconnected}, reconnected={reconnected}, attackerHits={runtime.Combat.AttackerDecidedHits}, victimHits={runtime.Combat.VictimDecidedHits}, revision={runtime.Session.Pause.Revision}, level={runtime.Session.LocalActor?.level}, failure={failure}."},true));
            Invoke(nameof(Exit),1);
        }
        private int exitCode;
        private void Exit()=>Application.Quit(exitCode);
        [Serializable] private sealed class Report {public bool success;public string detail;}
    }
}
