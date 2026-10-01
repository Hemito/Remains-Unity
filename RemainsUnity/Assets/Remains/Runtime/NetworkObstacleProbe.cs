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
    public struct ObstaclePhase : NetworkMessage {public int value;}
    public struct ObstacleFinish : NetworkMessage {public bool success;public string detail;}
    public sealed class NetworkObstacleProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private int phase=-1,fired=-1;
        private float since,deadline;
        private bool finished;
        private bool Host=>runtime.Options.Role=="host";
        private bool Victim=>runtime.Options.Character=="session-client-1";
        private ActorState Target=>runtime.Session.World.Find("player:session-client-1");
        private ActorState Shooter=>runtime.Session.World.Find("player:session-host");
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+65;value.CommandSource=Command;
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<ObstaclePhase>(m=>{phase=m.value;since=Time.unscaledTime;});
                NetworkClient.RegisterHandler<ObstacleFinish>(m=>Finish(m.success,m.detail));
            };
        }
        private static PlayerCommand Move(ActorState actor,float x)=>new PlayerCommand
        {movement=Vector2.right*(Mathf.Abs(actor.x-x)>.04f?Mathf.Sign(x-actor.x):0)};
        private PlayerCommand Command()
        {
            var actor=runtime.Session.LocalActor;if(actor==null) return default;
            if(phase==0) return Move(actor,Host?12:Victim?14.48f:18+int.Parse(actor.character.Split('-').Last()));
            if(phase==1 && Victim || phase==5 && Host)
                return new PlayerCommand {movement=actor.y<7.25f?Vector2.up:Vector2.zero};
            if(phase==2 && Victim) return Move(actor,12);
            if(phase==4 && Host) return Move(actor,14.48f);
            if(phase==6 && Host) return Move(actor,13);
            if((phase==3 || phase==7) && Host && fired!=phase)
            {fired=phase;return new PlayerCommand {weapon="energy",fire=true,hasAim=true,aim=Target.Position+Vector2.up*.55f};}
            return default;
        }
        private void Advance(int next) {phase=next;since=Time.unscaledTime;NetworkServer.SendToAll(new ObstaclePhase {value=next});}
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут препятствия: phase={phase}.");return;}
            if(!Host || runtime.Session.LocalActor==null) return;
            var players=runtime.Session.World.actors.Where(a=>a.player && a.connected).ToArray();
            if(players.Length!=runtime.Options.ProbePeers) return;
            if(phase<0) {Advance(0);return;}
            if(Time.unscaledTime-since<.5f) return;
            switch(phase)
            {
                case 0: if(Mathf.Abs(Shooter.x-12)<.1f && Mathf.Abs(Target.x-14.48f)<.1f) Advance(1);break;
                case 1: if(Target.y>=7.25f) Advance(2);break;
                case 2: if(Mathf.Abs(Target.x-12)<.1f && Mathf.Abs(Target.y-7.2f)<.05f) Advance(3);break;
                case 3:
                    if(Time.unscaledTime-since<2) return;
                    if(players.Any(a=>a.health!=100) || WeaponAmmunition.Magazine(Shooter,"energy").rounds!=14)
                    {Complete(false,"Выстрел через бетон нанёс урон или не потратил заряд.");return;}
                    Advance(4);break;
                case 4: if(Mathf.Abs(Shooter.x-14.48f)<.1f) Advance(5);break;
                case 5: if(Shooter.y>=7.25f) Advance(6);break;
                case 6: if(Mathf.Abs(Shooter.x-13)<.1f && Mathf.Abs(Shooter.y-7.2f)<.05f) Advance(7);break;
                case 7:
                    if(Time.unscaledTime-since<2) return;
                    Complete(Target.health==92 && Target.damageSequence==1 && players.All(a=>a.mana==100 && (a.id==Target.id || a.health==100)) &&
                        WeaponAmmunition.Magazine(Shooter,"energy").rounds==13,
                        "Обычные движение/лестница/выстрел: бетон блокирует урон, без преграды одно попадание; два заряда, мана и наблюдатели не затронуты.");break;
            }
        }
        private void Complete(bool success,string detail)=>NetworkServer.SendToAll(new ObstacleFinish {success=success,detail=detail});
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
