using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Runtime
{
    public struct OfficeSeamFixture : NetworkMessage {public int hostGeneration,clientGeneration;}
    public struct OfficeSeamObserved : NetworkMessage {}
    public struct OfficeSeamReady : NetworkMessage {}
    // Shared diagnostic for the two reviewed seams. Initial placement is explicit; crossing and returning use normal movement.
    internal sealed class FactoryOfficeSeamProbe
    {
        private readonly PrototypeRuntime runtime;
        private readonly HashSet<int> observed=new();
        private OfficeSeamFixture fixture;
        private bool placed,readySent;
        private int phase;
        private float readyAt=-1;
        private string actorId,owner,capture;
        private RoomLayout geometry;
        private readonly bool warehouse;
        private float fixtureAt=-1;
        private float Ground=>warehouse?7.2f:10.4f;
        private float InX=>warehouse?117.8f:78.65f;
        private float OutX=>warehouse?114:75.55f;
        private string Label=>warehouse?"Стык офис→склад":"Стык цех→офисы";
        public bool ContainerReady {get;private set;}
        public bool Verified=>phase==3;
        public string Failure {get;private set;}
        private ActorState Actor=>runtime.Session.LocalActor;
        private bool Host=>runtime.Options.Role=="host";
        private int ExpectedGeneration=>Host?fixture.hostGeneration:fixture.clientGeneration;
        public FactoryOfficeSeamProbe(PrototypeRuntime value,bool warehouse=false)
        {
            runtime=value;this.warehouse=warehouse;
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<OfficeSeamFixture>(message=>fixture=message);
                NetworkClient.RegisterHandler<OfficeSeamReady>(_=>ContainerReady=true);
            };
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<OfficeSeamObserved>((connection,_)=>
            {
                observed.Add(connection.connectionId);
                if(observed.Count==runtime.Options.ProbePeers && readyAt<0) readyAt=Time.unscaledTime+.5f;
            });
        }
        public void Tick()
        {
            if(Failure!=null || ContainerReady) return;
            var session=runtime.Session;
            if(Host && !placed && !session.Travel.Pending && session.ServerWorld.actors.Where(a=>a.player && a.connected).All(a=>a.level=="factory:0") &&
                session.World.actors.Where(a=>a.player && a.connected).All(a=>a.level=="factory:0"))
            {
                geometry=runtime.Geometry.Get("factory:0");
                foreach(var player in session.ServerWorld.actors.Where(a=>a.player && a.connected))
                {
                    var position=new Vector2(warehouse?(player.character=="session-host"?113.8f:114):(player.character=="session-host"?75.4f:75.6f),Ground);
                    if(!ActorTerrainMotion.IsClear(geometry,Anatomy.Movement(player),position) || !ActorTerrainMotion.Supported(geometry,Anatomy.Movement(player),position))
                    {Failure="Начальное положение у нового стыка не имеет свободной опоры: "+position;return;}
                    player.Position=position;player.Velocity=Vector2.zero;player.climbing=false;player.pose="idle";player.poseTime=0;
                    player.generation++;player.revision++;
                }
                fixture=new OfficeSeamFixture {hostGeneration=session.ServerWorld.actors.Single(a=>a.character=="session-host").generation,
                    clientGeneration=session.ServerWorld.actors.SingleOrDefault(a=>a.character=="session-client-1")?.generation??0};
                placed=true;session.CommitWorld();NetworkServer.SendToAll(fixture);
                Debug.Log(Label+": изменены только исходные положения игроков; геометрия/контент обычные, далее проход и возврат управлением.");
            }
            if(Host && readyAt>=0 && Time.unscaledTime>=readyAt && !readySent)
            {readySent=true;NetworkServer.SendToAll(new OfficeSeamReady());}
            if(Actor==null || ExpectedGeneration==0 || Actor.generation!=ExpectedGeneration) return;
            if(phase==0)
            {geometry=runtime.Geometry.Get("factory:0");actorId=Actor.id;owner=Actor.owner;phase=1;fixtureAt=Time.unscaledTime;capture="start";}
            if(Actor.id!=actorId || Actor.owner!=owner || Actor.level!="factory:0" || session.Travel.Pending ||
                !ReferenceEquals(geometry,runtime.Geometry.Get("factory:0")) || Actor.down || Mathf.Abs(Actor.y-Ground)>.03f)
            {Failure="Проход офисного стыка изменил актёра/уровень/владельца/опору или запустил переход.";return;}
            if(phase==1 && Actor.x>=InX) {phase=2;capture="in";}
            else if(phase==2 && Actor.x<=OutX)
            {phase=3;capture="out";NetworkClient.Send(new OfficeSeamObserved());}
        }
        public PlayerCommand Command()
        {
            if(Failure!=null || Actor==null || Actor.generation!=ExpectedGeneration || fixtureAt<0 || Time.unscaledTime-fixtureAt<3) return default;
            return new PlayerCommand {movement=phase==1?Vector2.right:phase==2?Vector2.left:Vector2.zero};
        }
        public void Capture()
        {
            if(capture==null || !runtime.Options.Smoke.Contains("-frames-") || capture=="start" && Time.unscaledTime-fixtureAt<2.8f) return;
            NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+(warehouse?".warehouse-seam-":".office-seam-")+capture+".png",runtime.Hud.GetComponent<Canvas>());capture=null;
        }
    }
}
