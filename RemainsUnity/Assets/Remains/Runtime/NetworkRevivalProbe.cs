using System;
using System.Collections;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Runtime
{
    public struct RevivalProbePhase : NetworkMessage {public int phase;}
    public struct RevivalProbeFinish : NetworkMessage { }
    // Actor state is read-only here. Movement, attacks and healing use ordinary commands.
    [DefaultExecutionOrder(10000)]
    public sealed class NetworkRevivalProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private int phase,startDamage;
        private float since,deadline;
        private bool grenade,finished,advancing,damageCancelled,healingBeforeDamage,releaseProgress,movementProgress;
        private bool Resume=>runtime.Options.Smoke.Contains("-resume-");
        private bool PauseScenario=>runtime.Options.Smoke.Contains("-pause-");
        private NetworkPauseWindow pauseWindow;
        private bool pauseStarted,pauseRunning;
        private bool Host=>runtime.Options.Role=="host";
        private bool CaptureFrames=>runtime.Options.Smoke.Contains("-frames-");
        private bool capturedDown,capturedRevived;
        private string grenadeId;
        private bool failureCaptureRequested;
        private ActorState Victim=>runtime.Session.World?.Find("player:session-client-1");
        private ActorState Healer=>runtime.Session.World?.Find("player:session-host");
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+60;runtime.CommandSource=Command;
            runtime.Grenades.Created+=flight=>
            {if(phase==5 && flight.state.attacker==Healer?.id) grenadeId=flight.state.id;};
            if(PauseScenario) pauseWindow=new NetworkPauseWindow(this,value.Session,value.Options.ProbePeers,PauseSnapshot,
                ()=>Victim!=null && Victim.down && InventoryJournal.Count(Healer,"heal")==5 &&
                    (!Host || runtime.Revival.Progress>0 && runtime.Revival.Progress<1 && !runtime.Revival.Pending),
                ()=>finished,reason=>Finish(false,reason));
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<RevivalProbePhase>(message=>
                {phase=message.phase;since=Time.unscaledTime;advancing=false;if(phase==5) startDamage=runtime.Session.LocalActor.damageSequence;});
                NetworkClient.RegisterHandler<RevivalProbeFinish>(_=>Finish(Validate(),Resume?"Перезапуск: здоровье 30/35, расход одной хилки и счётчики урона сохранены.":"Обычные команды: нокдаун, отмена E/расстоянием/уроном, поднятие и расход одной хилки; сохранение проверено."));
            };
        }
        private PlayerCommand Command()
        {
            var self=runtime.Session.LocalActor;if(self==null) return default;
            var command=new PlayerCommand();
            if(Resume) return command;
            if(phase==0)
            {
                float destination=Host?25:self.id=="player:session-client-1"?23.8f:28+int.Parse(self.character.Split('-').Last());
                command.movement=Vector2.right*(Mathf.Abs(destination-self.x)>.05f?Mathf.Sign(destination-self.x):0);return command;
            }
            if(!Host) return command;
            float age=Time.unscaledTime-since;
            if(phase==1) {command.weapon="pistol";command.hasAim=true;command.aim=Victim.Position+Vector2.up*.55f;command.fire=!Victim.down;}
            if(phase==2) command.revive=age<.55f;
            if(phase==3) {command.movement=self.x<27.4f?Vector2.right:Vector2.zero;command.revive=true;}
            if(phase==4) command.movement=Mathf.Abs(self.x-25)>.05f?Vector2.right*Mathf.Sign(25-self.x):Vector2.zero;
            if(phase==5)
            {
                // A downward hold starts below this one-way shelf; throw up so the real grenade returns to its top.
                command.grenade=!grenade;grenade=true;command.hasAim=true;command.aim=self.Position+Vector2.up*3;
                command.revive=age>1.2f && !damageCancelled;
            }
            if(phase==6) command.revive=true;
            if(command.revive) {command.hasAim=true;command.aim=InventorySource.BodyBounds(Victim).center;}
            return command;
        }
        private void Next()
        {if(advancing) return;advancing=true;NetworkServer.SendToAll(new RevivalProbePhase {phase=phase+1});}
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут поднятия: phase={phase}, hp={Victim?.health}, healer={Healer?.health}, x={Healer?.x}/{Victim?.x}, progress={runtime.Revival.Progress}.");return;}
            if(!Host || advancing || runtime.Session.World?.actors.Count(a=>a.player && a.connected)!=runtime.Options.ProbePeers) return;
            if(pauseRunning) return;
            if(PauseScenario && phase==6 && !pauseStarted && runtime.Revival.Progress>=.3f)
            {pauseStarted=true;pauseRunning=true;StartCoroutine(PauseRevival());return;}
            if(Resume && phase==0)
            {
                if(Victim.health!=35 || Healer.health!=30 || InventoryJournal.Count(Healer,"heal")!=4 || Healer.damageSequence<1 || Victim.damageSequence<1)
                {Finish(false,"Перезапуск потерял здоровье, расход предмета или историю урона.");return;}
                advancing=true;NetworkServer.SendToAll(new RevivalProbePhase {phase=7});return;
            }
            float age=Time.unscaledTime-since;
            if(phase>=2 && phase<=5 && (!Victim.down || InventoryJournal.Count(Healer,"heal")!=5))
            {Finish(false,$"Прерванное поднятие изменило цель или потратило предмет: phase={phase}, victimHP={Victim.health}, victimDown={Victim.down}, healerHP={Healer.health}, healerDamageSequence={Healer.damageSequence}, startDamage={startDamage}, healCount={InventoryJournal.Count(Healer,"heal")}, progress={runtime.Revival.Progress}, grenadeId={grenadeId ?? "не наблюдалась"}.");return;}
            if(phase==2 && runtime.Revival.Progress>0) releaseProgress=true;
            if(phase==3 && runtime.Revival.Progress>0) movementProgress=true;
            if(phase==5 && Healer.damageSequence==startDamage && runtime.Revival.Progress>.2f) healingBeforeDamage=true;
            if(phase==5 && Healer.damageSequence>startDamage && runtime.Revival.Progress==0) damageCancelled=true;
            switch(phase)
            {
                case 0:
                    if(Mathf.Abs(Healer.x-25)<.1f && Mathf.Abs(Victim.x-23.8f)<.1f &&
                        runtime.Session.World.actors.Where(a=>a.player && a.id!=Healer.id && a.id!=Victim.id).All(a=>a.x>29)) Next();break;
                case 1: if(Victim.down) Next();break;
                case 2: if(age>2 && releaseProgress && runtime.Revival.Progress==0) Next();break;
                case 3: if(age>2 && movementProgress && Vector2.Distance(Healer.Position,Victim.Position)>2 && runtime.Revival.Progress==0) Next();break;
                case 4: if(Mathf.Abs(Healer.x-25)<.1f) Next();break;
                case 5: if(age>2.4f && damageCancelled && healingBeforeDamage && Healer.damageSequence>startDamage && !Healer.down && runtime.Revival.Progress==0) Next();break;
                case 6: if(!Victim.down && Victim.health==35 && InventoryJournal.Count(Healer,"heal")==4) Next();break;
                case 7: if(age>4) {advancing=true;NetworkServer.SendToAll(new RevivalProbeFinish());}break;
            }
        }
        private bool Validate()
        {
            var saved=runtime.Session.Saves.LoadCharacter();var self=runtime.Session.LocalActor;
            return (!CaptureFrames || capturedDown && capturedRevived) && Victim!=null && !Victim.down && Victim.health==35 && InventoryJournal.Count(Healer,"heal")==4 &&
                saved!=null && saved.id==self.id && saved.health==self.health && InventoryJournal.Count(saved,"heal")==InventoryJournal.Count(self,"heal");
        }
        private void LateUpdate()
        {
            if(failureCaptureRequested)
            {
                failureCaptureRequested=false;
                try {NetworkSceneRecord.Save(runtime,100+phase);}
                catch(Exception error) {Debug.LogWarning($"Не удалось сохранить кадр сбоя поднятия: phase={phase}, grenadeId={grenadeId ?? "не наблюдалась"}, reason={error.Message}.",this);}
            }
            if(!CaptureFrames || finished || runtime.Session.LocalActor==null || Time.unscaledTime-since<.25f) return;
            if(phase==2 && !capturedDown && Victim.down)
            {if(!CheckWeaponVisible(false)) return;NetworkSceneRecord.Save(runtime,2);capturedDown=true;}
            if(phase==7 && !capturedRevived && !Victim.down && Victim.health==35)
            {if(!CheckWeaponVisible(true)) return;NetworkSceneRecord.Save(runtime,7);capturedRevived=true;}
        }
        private bool CheckWeaponVisible(bool visible)
        {
            var views=FindObjectsByType<Remains.Presentation.WeaponView>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            int matches=0;
            foreach(var view in views)
            {
                var group=view.GetComponentInParent<Remains.Presentation.ActorVisualGroup>(true);
                if(group==null || group.ActorId!=Victim.id) continue;
                matches++;
                if(view.gameObject.activeInHierarchy!=visible)
                {Finish(false,$"Неверная видимость целого оружия: actor={Victim.id}, down={Victim.down}, expected={visible}.");return false;}
            }
            if(matches!=1) {Finish(false,$"Ожидалось одно оружие пострадавшего: actor={Victim.id}, count={matches}.");return false;}
            return true;
        }
        private float[] PauseSnapshot()=>new[]{Victim.health,Healer.health,(float)InventoryJournal.Count(Healer,"heal"),
            Victim.x,Victim.y,Healer.x,Healer.y,runtime.Session.LocalActor.poseTime,
            Host?runtime.Revival.Progress:0,Host && runtime.Revival.Pending?1:0};
        private IEnumerator PauseRevival()
        {yield return pauseWindow.Run("поднятие союзника");pauseRunning=false;}
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            if(PauseScenario) {success &= pauseWindow.Verified==1;detail+=$" Проверенные паузы поднятия: {pauseWindow.Verified}.";}
            failureCaptureRequested=!success && CaptureFrames && runtime.Session.LocalActor!=null;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail,phase=phase},true));
            StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:1);}
        [Serializable] private sealed class Report {public bool success;public string detail;public int phase;}
    }
}
