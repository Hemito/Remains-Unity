using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Runtime
{
    public struct LadderBodyPhase : NetworkMessage {public int phase;}
    public struct LadderBodyDone : NetworkMessage { }
    public struct LadderBodyFinish : NetworkMessage { }
    // Read-only observations of actors. Every gameplay action uses PlayerCommand.
    [DefaultExecutionOrder(10000)]
    public sealed class NetworkLadderBodyProbe : MonoBehaviour
    {
        [Serializable] private sealed class Sample
        {public float time,x,y,vy;public bool down,climbing;public string pose,owner;public int generation;}
        [Serializable] private sealed class Report
        {public bool success,climbed,fell,landed,revived,movedAfterRevival;public string detail;public float firstDownY;}
        [Serializable] private sealed class Trace {public List<Sample> samples;}
        private PrototypeRuntime runtime;
        private RoomLadder ladder;
        private int phase;
        private float deadline,since,firstDownY,downX,downAt;
        private bool advancing,finished,sent,climbed,downSeen,fallVelocitySeen,fell,landed,revived,movedAfterRevival;
        private string owner;private int generation;
        private readonly HashSet<int> done=new(),captures=new();
        private readonly List<Sample> samples=new();
        private bool Host=>runtime.Options.Role=="host";
        private bool Frames=>runtime.Options.Smoke.Contains("-frames-");
        private ActorState Victim=>runtime.Session.World?.Find("player:session-client-1");
        private ActorState Healer=>runtime.Session.World?.Find("player:session-host");
        private float Axis=>ladder.x+ladder.movementOffset;
        private float Height=>ladder.bottom+2.5f;
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+65;value.CommandSource=Command;
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<LadderBodyDone>((connection,_)=>
            {done.Add(connection.connectionId);if(done.Count==2) NetworkServer.SendToAll(new LadderBodyFinish());});
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<LadderBodyPhase>(message=>
                {phase=message.phase;since=Time.unscaledTime;advancing=false;});
                NetworkClient.RegisterHandler<LadderBodyFinish>(_=>Finish(true,"Лестница: нокдаун от выстрелов, падение без управления, опора, поднятие одной аптечкой и восстановление движения."));
            };
        }
        private PlayerCommand Command()
        {
            var self=runtime.Session.LocalActor;
            if(finished || self==null || Victim==null || Healer==null) return default;
            if(ladder==null) ladder=runtime.Geometry.Get("hub").ladders.OrderBy(value=>Mathf.Abs(value.x+value.movementOffset-14.98f)).First();
            if(phase==0)
            {
                if(Mathf.Abs(self.x-Axis)>.08f) return Move(Axis,self);
                return self.y>ladder.bottom+.1f?new PlayerCommand {movement=Vector2.down}:default;
            }
            if(!Host)
            {
                if(self.down) return new PlayerCommand {movement=Vector2.one,jump=true,sprint=true};
                if(phase==1 && self.y<Height-.08f) return new PlayerCommand {movement=Vector2.up};
                if(phase==5 && self.x<Axis+1) return Move(Axis+1.1f,self);
                return default;
            }
            if(phase==1) return Move(Axis+2.5f,self);
            if(phase==2) return new PlayerCommand {weapon="pistol",hasAim=true,aim=Victim.Position+Vector2.up*.65f,fire=!Victim.down};
            if(phase==3) return Move(Axis+1.1f,self);
            if(phase==4) return new PlayerCommand {revive=true};
            return default;
        }
        private static PlayerCommand Move(float x,ActorState actor)=>new()
        {movement=Vector2.right*(Mathf.Abs(x-actor.x)>.08f?Mathf.Sign(x-actor.x):0)};
        private void Next()
        {if(advancing) return;advancing=true;NetworkServer.SendToAll(new LadderBodyPhase {phase=phase+1});}
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут падения: phase={phase}, victim={Victim?.Position}, healer={Healer?.Position}, down={Victim?.down}.");return;}
            var victim=Victim;
            if(ladder==null || victim==null || Healer==null) return;
            if(owner==null) {owner=victim.owner;generation=victim.generation;}
            if(victim.owner!=owner || victim.generation!=generation || victim.level!="hub")
            {Finish(false,"Нокдаун изменил владельца, поколение или локацию тела.");return;}
            if(samples.Count<3000 && phase>=1 && phase<=5)
                samples.Add(new Sample {time=Time.unscaledTime,x=victim.x,y=victim.y,vy=victim.vy,down=victim.down,
                    climbing=victim.climbing,pose=victim.pose,owner=victim.owner,generation=victim.generation});
            if(victim.climbing && victim.y>Height-.2f) climbed=true;
            if(victim.down)
            {
                if(!downSeen) {downSeen=true;firstDownY=victim.y;downX=victim.x;downAt=Time.unscaledTime;}
                // Damage can arrive after this frame's movement; the remote pose also needs delivery time.
                if(victim.climbing && Time.unscaledTime-downAt>(Host?.5f:.1f) || Mathf.Abs(victim.x-downX)>.12f)
                {Finish(false,"Тело удерживает лестницу или принимает горизонтальное управление.");return;}
                if(victim.vy<-.1f) fallVelocitySeen=true;
                // Rendered position can lag the latest authoritative landing velocity.
                if(fallVelocitySeen && victim.y<firstDownY-.3f) fell=true;
                if(fell && Mathf.Abs(victim.y-ladder.bottom)<.08f && Mathf.Abs(victim.vy)<.001f) landed=true;
            }
            else if(downSeen && landed && victim.health==35) revived=true;
            if(revived && victim.x>Axis+.6f) movedAfterRevival=true;
            if(phase==6 && Time.unscaledTime-since>4 && !sent)
            {
                if(!climbed || !fell || !landed || !revived || !movedAfterRevival || firstDownY<ladder.bottom+1 ||
                    InventoryJournal.Count(Healer,"heal")!=4 || Frames && captures.Count!=4)
                {Finish(false,$"Неполная проверка тела: climb={climbed}, fall={fell}, ground={landed}, revive={revived}, move={movedAfterRevival}, frames={captures.Count}.");return;}
                var saved=runtime.Session.Saves.LoadWorld()?.Find(victim.id);
                if(saved==null || saved.down || saved.health!=35 || saved.x<Axis+.5f)
                {Finish(false,"Сохранение не содержит поднятое и переместившееся тело.");return;}
                sent=true;NetworkClient.Send(new LadderBodyDone());
            }
            if(!Host || advancing || runtime.Session.World.actors.Count(a=>a.player && a.connected)!=2) return;
            switch(phase)
            {
                case 0: if(new[]{victim,Healer}.All(a=>Mathf.Abs(a.x-Axis)<.12f && Mathf.Abs(a.y-ladder.bottom)<.12f)) Next();break;
                case 1: if(climbed && Mathf.Abs(Healer.x-(Axis+2.5f))<.12f) Next();break;
                case 2: if(victim.down) Next();break;
                case 3: if(landed && Mathf.Abs(Healer.x-(Axis+1.1f))<.12f) Next();break;
                case 4: if(revived) Next();break;
                case 5: if(movedAfterRevival) Next();break;
            }
        }
        private void LateUpdate()
        {
            if(!Frames || finished || ladder==null || Victim==null) return;
            int capture=Victim.climbing && Victim.y>Height-.2f?1:
                Victim.down && Victim.y>ladder.bottom+.3f && Victim.y<Height-.35f?2:
                landed && Victim.down?3:revived?4:0;
            if(capture!=0 && captures.Add(capture)) NetworkSceneRecord.Save(runtime,capture);
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail,
                climbed=climbed,fell=fell,landed=landed,revived=revived,movedAfterRevival=movedAfterRevival,firstDownY=firstDownY},true));
            File.WriteAllText(runtime.Options.Report+".body.json",JsonUtility.ToJson(new Trace {samples=samples},true));
            Debug.Log(detail);StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:2);}
    }
}
