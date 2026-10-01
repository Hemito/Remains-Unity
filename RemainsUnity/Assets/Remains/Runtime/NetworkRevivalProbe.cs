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
    public sealed class NetworkRevivalProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private int phase,startDamage;
        private float since,deadline;
        private bool grenade,finished,advancing,damageCancelled,healingBeforeDamage,releaseProgress,movementProgress;
        private bool Resume=>runtime.Options.Smoke.Contains("-resume-");
        private bool Host=>runtime.Options.Role=="host";
        private ActorState Victim=>runtime.Session.World?.Find("player:session-client-1");
        private ActorState Healer=>runtime.Session.World?.Find("player:session-host");
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+60;runtime.CommandSource=Command;
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
                float destination=Host?3:self.id=="player:session-client-1"?1.8f:6+int.Parse(self.character.Split('-').Last());
                command.movement=Vector2.right*(Mathf.Abs(destination-self.x)>.05f?Mathf.Sign(destination-self.x):0);return command;
            }
            if(!Host) return command;
            float age=Time.unscaledTime-since;
            if(phase==1) {command.weapon="pistol";command.hasAim=true;command.aim=Victim.Position+Vector2.up*.55f;command.fire=!Victim.down;}
            if(phase==2) command.revive=age<.55f;
            if(phase==3) {command.movement=self.x<5.4f?Vector2.right:Vector2.zero;command.revive=true;}
            if(phase==4) command.movement=Mathf.Abs(self.x-3)>.05f?Vector2.right*Mathf.Sign(3-self.x):Vector2.zero;
            if(phase==5)
            {
                command.grenade=!grenade;grenade=true;command.hasAim=true;command.aim=self.Position+Vector2.down*3;
                command.revive=age>1.2f && !damageCancelled;
            }
            if(phase==6) command.revive=true;
            return command;
        }
        private void Next()
        {if(advancing) return;advancing=true;NetworkServer.SendToAll(new RevivalProbePhase {phase=phase+1});}
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут поднятия: phase={phase}, hp={Victim?.health}, healer={Healer?.health}, x={Healer?.x}/{Victim?.x}, progress={runtime.Revival.Progress}.");return;}
            if(!Host || advancing || runtime.Session.World?.actors.Count(a=>a.player && a.connected)!=runtime.Options.ProbePeers) return;
            if(Resume && phase==0)
            {
                if(Victim.health!=35 || Healer.health!=30 || InventoryJournal.Count(Healer,"heal")!=4 || Healer.damageSequence<1 || Victim.damageSequence<1)
                {Finish(false,"Перезапуск потерял здоровье, расход предмета или историю урона.");return;}
                advancing=true;NetworkServer.SendToAll(new RevivalProbePhase {phase=7});return;
            }
            float age=Time.unscaledTime-since;
            if(phase>=2 && phase<=5 && (!Victim.down || InventoryJournal.Count(Healer,"heal")!=5))
            {Finish(false,"Прерванное поднятие изменило цель или потратило предмет.");return;}
            if(phase==2 && runtime.Revival.Progress>0) releaseProgress=true;
            if(phase==3 && runtime.Revival.Progress>0) movementProgress=true;
            if(phase==5 && Healer.damageSequence==startDamage && runtime.Revival.Progress>.2f) healingBeforeDamage=true;
            if(phase==5 && Healer.damageSequence>startDamage && runtime.Revival.Progress==0) damageCancelled=true;
            switch(phase)
            {
                case 0:
                    if(Mathf.Abs(Healer.x-3)<.1f && Mathf.Abs(Victim.x-1.8f)<.1f &&
                        runtime.Session.World.actors.Where(a=>a.player && a.id!=Healer.id && a.id!=Victim.id).All(a=>a.x>7)) Next();break;
                case 1: if(Victim.down) Next();break;
                case 2: if(age>2 && releaseProgress && runtime.Revival.Progress==0) Next();break;
                case 3: if(age>2 && movementProgress && Vector2.Distance(Healer.Position,Victim.Position)>2 && runtime.Revival.Progress==0) Next();break;
                case 4: if(Mathf.Abs(Healer.x-3)<.1f) Next();break;
                case 5: if(age>2.4f && damageCancelled && healingBeforeDamage && Healer.damageSequence>startDamage && !Healer.down && runtime.Revival.Progress==0) Next();break;
                case 6: if(!Victim.down && Victim.health==35 && InventoryJournal.Count(Healer,"heal")==4) Next();break;
                case 7: if(age>4) {advancing=true;NetworkServer.SendToAll(new RevivalProbeFinish());}break;
            }
        }
        private bool Validate()
        {
            var saved=runtime.Session.Saves.LoadCharacter();var self=runtime.Session.LocalActor;
            return Victim!=null && !Victim.down && Victim.health==35 && InventoryJournal.Count(Healer,"heal")==4 &&
                saved!=null && saved.id==self.id && saved.health==self.health && InventoryJournal.Count(saved,"heal")==InventoryJournal.Count(self,"heal");
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail,phase=phase},true));
            StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:1);}
        [Serializable] private sealed class Report {public bool success;public string detail;public int phase;}
    }
}
