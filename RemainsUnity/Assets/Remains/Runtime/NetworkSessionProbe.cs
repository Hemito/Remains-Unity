using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Gameplay;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Runtime
{
    public struct SessionProbePhase : NetworkMessage { public int phase; }
    public struct SessionProbeAck : NetworkMessage { public int phase; }

    // No ActorState writes: commands enter the production loop, NPCs keep running.
    public sealed class NetworkSessionProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private int phase=-1;
        private float phaseTime,deadline;
        private bool acked,travelRequested,finished;
        private bool grenadeRequested,blastAfterTravel;
        private string completedActor;
        private string retainedContents,retainedActors;
        private int visualChangedFrame;
        private readonly HashSet<int> acknowledgements=new();
        private bool Host=>runtime.Options.Role=="host";

        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+60;
            value.Session.Changed+=()=>visualChangedFrame=Time.frameCount;
            runtime.CommandSource=Command;
            value.Session.AttackReceived+=attack=>
            {
                if(attack.weapon=="grenade" && attack.attacker=="player:session-host")
                    blastAfterTravel=attack.level=="hub" && value.Session.World.Find(attack.attacker)?.level=="factory:0";
            };
            value.Session.ClientReady+=()=>NetworkClient.RegisterHandler<SessionProbePhase>(message=>
            {phase=message.phase;phaseTime=Time.unscaledTime;acked=false;travelRequested=false;if(phase==7) completedActor=value.Session.LocalActorId;});
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<SessionProbeAck>((connection,message)=>
            {
                if(message.phase==phase && value.Session.ConnectedActor(connection)!=null) acknowledgements.Add(connection.connectionId);
            });
        }
        private PlayerCommand Command()
        {
            var self=runtime.Session.LocalActor;
            if(self==null) return default;
            if(phase==1 && Host && !grenadeRequested)
            {
                grenadeRequested=true;
                return new PlayerCommand {grenade=true,hasAim=true,aim=self.Position+Vector2.down*3};
            }
            if(phase!=3) return default;
            float destination=Host?-2:2;
            float delta=destination-self.x;
            return new PlayerCommand { movement=new Vector2(Mathf.Abs(delta)>.05f?Mathf.Sign(delta):0,0),
                hasAim=true,aim=self.Position+new Vector2(Host?-5:5,.8f),weapon="pistol" };
        }
        private void Advance(int next)
        {
            acknowledgements.Clear();
            NetworkServer.SendToAll(new SessionProbePhase {phase=next});
        }
        private void Update()
        {
            if(finished) return;
            // Completion was acknowledged by every peer before phase 7. A host exit
            // must not make a slower client's report depend on the live player count.
            if(phase==7) {if(Time.unscaledTime-phaseTime>1) Finish(true,"Обычная симуляция: UI-переходы в обе стороны, разделение группы, граната в исходной локации, независимое движение и видимость; выход всей группы и повторный вход без пересоздания NPC/контейнеров.");return;}
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут: phase={phase}, level={runtime.Session.LocalActor?.level}, pending={runtime.Session.Travel.Pending}");return;}
            var session=runtime.Session;var self=session.LocalActor;
            if(self==null) return;
            if(runtime.Options.NetworkStress && Transport.active is not LatencySimulation)
            {Finish(false,"Тестовый транспорт задержек не активирован.");return;}
            var players=session.World.actors.Where(a=>a.player && a.connected).ToArray();
            if(players.Length!=runtime.Options.ProbePeers) return;
            if(phase<0) {if(Host) Advance(0);return;}
            if((phase==1 && Host && runtime.Grenades.Flights.Count>0 || phase==2 && !Host || phase==4 && Host || phase==5 && !Host || phase==6) && !travelRequested)
            {travelRequested=true;StartCoroutine(UiPointerProbe.Click(runtime.Hud.FactoryButtonRect));}
            if(Time.unscaledTime-phaseTime<1) return;
            bool ready=phase switch
            {
                0=>players.All(a=>a.level=="hub"),
                1=>blastAfterTravel && self.level==(Host?"factory:0":"hub") && players.Count(a=>a.level=="factory:0")==1 &&
                    players.Where(a=>a.character!="session-host").All(a=>a.health==30),
                2=>players.All(a=>a.level=="factory:0") && !session.Travel.Pending,
                3=>players.All(a=>a.level=="factory:0" && (a.character=="session-host"?a.x<-1.8f && a.facing==-1:a.x>1.8f && a.facing==1)),
                4=>self.level==(Host?"hub":"factory:0") && players.Count(a=>a.level=="hub")==1 && !session.Travel.Pending,
                5=>players.All(a=>a.level=="hub") && !session.Travel.Pending,
                6=>players.All(a=>a.level=="factory:0") && !session.Travel.Pending,
                _=>false
            };
            if(!ready) return;
            // Unity destroys objects and their owned materials at frame boundaries.
            if(Time.frameCount<=visualChangedFrame+2 || runtime.ReleasingUnusedAssets) return;
            if(!runtime.Geometry.LoadedLevels.OrderBy(l=>l).SequenceEqual(players.Select(a=>a.level).Distinct().OrderBy(l=>l)))
            {Finish(false,"Геометрия симуляции не соответствует занятым локациям.");return;}
            string contents=string.Join("|",session.World.containers.OrderBy(c=>c.id).Select(c=>c.id+":"+string.Join(",",c.items.OrderBy(i=>i.id).Select(i=>i.id+":"+i.count))));
            string actors=string.Join("|",session.World.actors.Where(a=>!a.player).OrderBy(a=>a.id).Select(a=>a.id+":"+a.health));
            if(phase==3) {retainedContents=contents;retainedActors=actors;}
            if(phase>=4 && (contents!=retainedContents || actors!=retainedActors))
            {Finish(false,"Выход/возвращение изменили NPC или содержимое контейнеров.");return;}
            if(players.Select(a=>a.id).Distinct().Count()!=players.Length) {Finish(false,"Повтор ID персонажа");return;}
            var expected=session.World.actors.Where(a=>a.level==self.level).Select(a=>a.id).OrderBy(id=>id).ToArray();
            var visible=FindObjectsByType<PonyVisual>(FindObjectsInactive.Include,FindObjectsSortMode.None).Select(v=>v.ActorId).OrderBy(id=>id).ToArray();
            if(!expected.SequenceEqual(visible)) {Finish(false,$"Неверная видимость: expected={string.Join(",",expected)}, actual={string.Join(",",visible)}");return;}
            int expectedSprites=expected.Length+session.World.props.Count(p=>p.level==self.level)+session.World.containers.Count(c=>c.level==self.level)+runtime.Grenades.Flights.Count(f=>f.state.level==self.level);
            int spriteCount=FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length;
            var rooms=FindObjectsByType<RoomMeshLifetime>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            int roomMaterials=Resources.FindObjectsOfTypeAll<Material>().Count(m=>m.name.StartsWith("RoomMaterial:"));
            if(spriteCount!=expectedSprites || rooms.Length!=1 || roomMaterials!=rooms[0].Materials.Count)
            {Finish(false,$"Ресурсы локации не освобождены: sprites={spriteCount}/{expectedSprites}, rooms={rooms.Length}, materials={roomMaterials}.");return;}
            if(!acked) {acked=true;NetworkClient.Send(new SessionProbeAck {phase=phase});}
            if(Host && acknowledgements.Count==runtime.Options.ProbePeers) Advance(phase+1);
        }
        private void Finish(bool success,string detail)
        {
            finished=true;
            string json=JsonUtility.ToJson(new Report {success=success,detail=detail,phase=phase,peers=runtime.Options.ProbePeers,actor=completedActor??runtime.Session.LocalActorId,stress=runtime.Options.NetworkStress},true);
            if(runtime.Options.Report!=null) File.WriteAllText(runtime.Options.Report,json);
            Debug.Log(json);Application.Quit(success?0:2);
        }
        [Serializable] private sealed class Report {public bool success,stress;public string detail,actor;public int phase,peers;}
    }
}
