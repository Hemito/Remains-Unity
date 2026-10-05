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
    public struct PauseGroupPhase : NetworkMessage {public int phase;}
    public struct PauseGroupAck : NetworkMessage {public int phase;public bool valid;public string detail;}
    public struct PauseGroupFinish : NetworkMessage {public bool success;public string detail;}
    public sealed class NetworkPauseGroupProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private readonly HashSet<int> replies=new();
        private int phase=-1,checkedPhases;
        private bool finished,menuCommand,inventoryCommand,sawOffline;
        private float deadline;
        private bool Host=>runtime.Options.Role=="host";
        private bool First=>runtime.Options.Character=="session-client-1";
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+50;
            value.CommandSource=()=>
            {
                var command=new PlayerCommand {menu=menuCommand,inventory=inventoryCommand};
                menuCommand=inventoryCommand=false;return command;
            };
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<PauseGroupAck>((connection,message)=>
            {
                if(message.phase!=phase) return;
                if(!message.valid) {Complete(false,message.detail);return;}
                replies.Add(connection.connectionId);
            });
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<PauseGroupPhase>(message=>{phase=message.phase;StartCoroutine(ApplyAndCheck());});
                NetworkClient.RegisterHandler<PauseGroupFinish>(message=>Finish(message.success,message.detail));
            };
        }
        private IEnumerator Start()
        {
            if(!Host) yield break;
            while(runtime.Session.World==null || runtime.Session.World.actors.Count(a=>a.player && a.connected)!=runtime.Options.ProbePeers) yield return null;
            runtime.Session.Pause.SetSettings(true,true);
            for(int step=0;step<10 && !finished;step++)
            {
                phase=step;replies.Clear();NetworkServer.SendToAll(new PauseGroupPhase {phase=step});
                while(replies.Count!=runtime.Options.ProbePeers && !finished) yield return null;
            }
            if(!finished) Complete(true,"Меню, инвентарь и ручная пауза независимы; отключение одного инициатора сохраняет причину другого.");
        }
        private IEnumerator Menu(bool open)
        {
            if(runtime.Hud.PauseMenu.IsOpen==open) yield break;
            menuCommand=true;
            while(runtime.Hud.PauseMenu.IsOpen!=open && !finished) yield return null;
        }
        private IEnumerator Inventory(bool open)
        {
            if(runtime.Hud.InventoryOpen==open) yield break;
            inventoryCommand=true;
            while(runtime.Hud.InventoryOpen!=open && !finished) yield return null;
        }
        private IEnumerator ApplyAndCheck()
        {
            int step=phase;
            if(step==1) {if(Host) yield return Menu(true);if(First) yield return Inventory(true);}
            if(step==2 && Host) yield return Menu(false);
            if(step==3 && First) yield return Inventory(false);
            if(step==4)
            {
                if(Host)
                {
                    yield return Menu(true);yield return UiPointerProbe.Click(runtime.Hud.PauseMenu.ManualPauseRect);yield return Menu(false);
                }
                if(First) yield return Inventory(true);
            }
            if(step==5 && Host)
            {
                yield return Menu(true);yield return UiPointerProbe.Click(runtime.Hud.PauseMenu.ResumeRect);yield return Menu(false);
            }
            if(step==6 && First) yield return Inventory(false);
            if(step==7 && (Host || First)) yield return Menu(true);
            if(step==8 && First)
            {
                runtime.Session.StopClient();yield return new WaitForSecondsRealtime(1.5f);runtime.Session.StartClient();
                while(runtime.Session.LocalActor==null && !finished) yield return null;
            }
            if(step==9 && Host) yield return Menu(false);
            if(finished) yield break;
            yield return new WaitForSecondsRealtime(.6f);
            bool paused=step is 1 or 2 or 4 or 5 or 7 or 8;
            bool expectedMenu=Host && step is 1 or 7 or 8 || First && step==7;
            bool expectedInventory=First && step is 1 or 2 or 4 or 5;
            var actor=runtime.Session.LocalActor;
            bool valid=actor!=null && actor.level=="hub" && runtime.Session.Pause.IsPaused("hub")==paused &&
                runtime.Hud.PauseMenu.IsOpen==expectedMenu && runtime.Hud.InventoryOpen==expectedInventory &&
                runtime.Session.Pause.PauseOnMenu && runtime.Session.Pause.PauseOnInventory;
            float time=actor?.poseTime ?? 0;
            double canonicalTime=Host?runtime.Session.LevelTime.ReadServer("hub"):0;
            yield return new WaitForSecondsRealtime(.6f);
            actor=runtime.Session.LocalActor;
            valid &= actor!=null && runtime.Session.Pause.IsPaused("hub")==paused &&
                (paused?actor.poseTime==time:actor.poseTime>time+.1f) && (!Host || step!=8 || sawOffline);
            if(Host)
            {
                double after=runtime.Session.LevelTime.ReadServer("hub");
                bool clocksValid=paused?after==canonicalTime:after>canonicalTime+.1;
                valid &= clocksValid;if(clocksValid) clockPhases++;
            }
            if(valid) checkedPhases++;
            NetworkClient.Send(new PauseGroupAck {phase=step,valid=valid,
                detail=$"Композиция паузы не совпала: phase={step}, actor={actor?.id}, paused={runtime.Session.Pause.IsPaused("hub")}, menu={runtime.Hud.PauseMenu.IsOpen}, inventory={runtime.Hud.InventoryOpen}, offline={sawOffline}."});
        }
        private int clockPhases;
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут композиции: phase={phase}, ack={replies.Count}.");return;}
            if(Host && phase==8 && runtime.Session.ServerWorld?.Find("player:session-client-1") is {connected:false} && runtime.Session.Pause.IsServerPaused("hub")) sawOffline=true;
        }
        private void Complete(bool success,string detail)=>NetworkServer.SendToAll(new PauseGroupFinish {success=success,detail=detail});
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;success &= checkedPhases==10 && (!Host || clockPhases==10);
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,
                canonicalClockPhases=clockPhases,
                detail=detail+$" Проверено фаз: {checkedPhases}; отключение при оставшейся причине: {sawOffline}."},true));
            exitCode=success?0:1;Invoke(nameof(Exit),1);
        }
        private int exitCode;
        private void Exit()=>Application.Quit(exitCode);
        [Serializable] private sealed class Report {public bool success;public string detail;public int canonicalClockPhases;}
    }
}
