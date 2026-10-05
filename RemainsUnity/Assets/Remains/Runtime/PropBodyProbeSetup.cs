using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Runtime
{
    // Read-only fixture selection. Only the caller's own player may be placed once.
    public sealed class PropBodyProbeSetup
    {
        public ContainerState Cargo {get;private set;}
        public Vector2 PlayerPosition {get;private set;}
        public Vector2 Lift {get;private set;}
        public float Floor {get;private set;}
        public string Contents {get;private set;}
        public int InitialGeneration {get;private set;}
        public static string Inventory(ContainerState cargo)=>StateJson.ToJson(cargo.items)+"|"+StateJson.ToJson(cargo.itemAliases);
        public static bool Supported(ContainerState cargo,RoomLayout room,WorldState world)
        {
            var feet=cargo.Position-Vector2.up*cargo.anchorHeight;
            var hull=new MovementHull {width=cargo.width,height=cargo.height};
            return ActorTerrainMotion.Supported(room,hull,feet) || PropSupport.Supported(cargo,feet,WorldProps.All(world));
        }
        public static PropBodyProbeSetup Select(WorldState world,ActorState actor,RoomLayout room,ITelekinesisRule rules)
        {
            var settings=TelekinesisSettings.Load();var playerHull=Anatomy.Movement(actor);
            foreach(var cargo in world.containers.Where(c=>c.level==actor.level && c.id.Contains(":original:plant:") &&
                c.movable && !c.held && c.presentationItem==null && c.items.Count>0 &&
                float.IsFinite(c.mass) && c.mass<=settings.maximumMass).OrderBy(c=>c.id,StringComparer.Ordinal))
            {
                float floor=cargo.y-cargo.anchorHeight;
                var cargoHull=new MovementHull {width=cargo.width,height=cargo.height};
                if(!ActorTerrainMotion.Supported(room,cargoHull,new Vector2(cargo.x,floor))) continue;
                if(world.actors.Any(a=>!a.player && !a.down && a.level==actor.level && Vector2.Distance(a.Position,cargo.Position)<10)) continue;
                var lift=cargo.Position+Vector2.up*1.2f;
                if(!ActorTerrainMotion.IsClear(room,cargoHull,lift-Vector2.up*cargo.anchorHeight)) continue;
                foreach(int direction in new[]{1,-1})
                {
                    var position=new Vector2(cargo.x+direction*(cargo.width*.5f+playerHull.width*.5f+.3f),floor);
                    if(!ActorTerrainMotion.IsClear(room,playerHull,position) || !ActorTerrainMotion.Supported(room,playerHull,position)) continue;
                    var placed=actor.Copy();placed.Position=position;
                    if(rules.Rejection(placed,cargo,false)!=null || rules is ITelekinesisSightRule sight && sight.SightRejection(placed,cargo,room)!=null) continue;
                    return new PropBodyProbeSetup {Cargo=cargo,PlayerPosition=position,Lift=lift,Floor=floor,
                        Contents=Inventory(cargo),InitialGeneration=cargo.generation};
                }
            }
            throw new InvalidOperationException("Нет авторского контейнера с содержимым, доступной массой, свободным подъёмом и поддержанной позицией игрока вдали от NPC. Объекты не изменены.");
        }
    }

    // A focused normal route to the existing map. No debug travel or desktop input.
    public sealed class PropBodyHubRoute
    {
        public TravelPoint Map {get;}
        private readonly float ladderX;
        private int phase;
        public PropBodyHubRoute(PrototypeRuntime runtime)
        {
            Map=runtime.Session.Travel.Points("hub").Single(p=>p.kind==TravelPointKind.Map);
            var room=runtime.Geometry.Get("hub");
            var ladder=room.ladders.Where(l=>l.bottom<room.spawnY+.5f && l.top>Map.arrivalY)
                .OrderBy(l=>Mathf.Abs(l.x+l.movementOffset-Map.arrivalX)).First();
            ladderX=ladder.x+ladder.movementOffset;
        }
        public PlayerCommand Command(PrototypeRuntime runtime)
        {
            var actor=runtime.Session.LocalActor;
            if(phase==0)
            {
                if(Mathf.Abs(actor.x-ladderX)>.06f) return new PlayerCommand {movement=Vector2.right*Mathf.Sign(ladderX-actor.x)};
                phase=1;
            }
            if(phase==1)
            {
                if(actor.y<Map.arrivalY+.02f) return new PlayerCommand {movement=Vector2.up};
                phase=2;return new PlayerCommand {movement=Vector2.right*Mathf.Sign(Map.arrivalX-ladderX),jump=true};
            }
            if(Mathf.Abs(actor.x-Map.arrivalX)>.08f) return new PlayerCommand {movement=Vector2.right*Mathf.Sign(Map.arrivalX-actor.x)};
            if(phase==2 && Map.Rejection(actor)==null && ActorTerrainMotion.Supported(runtime.Geometry.Get("hub"),Anatomy.Movement(actor),actor.Position))
            {phase=3;return new PlayerCommand {interact=true,hasAim=true,aim=Map.Bounds.center};}
            return default;
        }
    }
}
