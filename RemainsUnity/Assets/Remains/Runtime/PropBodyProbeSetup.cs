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
        private const float LiftHeight=1.2f;
        public ContainerState Cargo {get;private set;}
        public Vector2 PlayerPosition {get;private set;}
        public Vector2 Lift {get;private set;}
        public float Floor {get;private set;}
        public string Contents {get;private set;}
        public int InitialGeneration {get;private set;}
        public static string Inventory(ContainerState cargo)=>StateJson.ToJson(cargo.items)+"|"+StateJson.ToJson(cargo.itemAliases);
        public static string ManifestRejection(WorldState world,ActorState sender,string hostActorId,bool remote,PropBodyPrepared message,
            float maximumMass,float grenadeDamage)
        {
            if(world==null || sender==null || !sender.player || !sender.connected || sender.down || sender.id!=message.source ||
                sender.level!="factory:0" || string.IsNullOrEmpty(sender.owner) || sender.owner!=message.owner ||
                string.IsNullOrEmpty(hostActorId) || (remote?sender.id==hostActorId:sender.id!=hostActorId))
                return $"Неверный подключённый источник: sender={sender?.id}, requested={message.source}, owner={message.owner}, remote={remote}.";
            if(WorldRooms.Definition(world,sender.level)!=NewGameWorld.FactoryDefinitionId ||
                world.actors.Count(a=>a.player && a.connected)!=2 ||
                world.actors.Any(a=>a.player && a.connected && a.id!=sender.id && (a.level!="hub" || a.down)))
                return "Подготовка не подтверждает авторский завод и активного союзника в хабе.";
            var cargo=world.containers.Find(c=>c.id==message.cargo);
            if(cargo==null || !cargo.id.Contains(":original:plant:") || cargo.level!=sender.level || !cargo.movable ||
                cargo.presentationItem!=null || !cargo.held || cargo.owner!=sender.owner || cargo.generation!=message.heldGeneration ||
                cargo.definition!=message.definition || !float.IsFinite(cargo.mass) || cargo.mass<0 || cargo.mass>maximumMass ||
                cargo.mass!=message.mass || cargo.items.Count==0 || Inventory(cargo)!=message.contents)
                return $"Серверный контейнер не соответствует подготовке: cargo={message.cargo}, owner={cargo?.owner}, held={cargo?.held}, generation={cargo?.generation}/{message.heldGeneration}.";
            if(message.previousAttacks==null || !sender.resolvedAttacks.SequenceEqual(message.previousAttacks) ||
                sender.resolvedAttacks.Count!=0 || sender.health!=100 || sender.health!=message.initialHealth ||
                message.initialGrenades<2 || sender.inventory.Where(i=>i.definition=="grenade").Sum(i=>i.count)!=message.initialGrenades ||
                grenadeDamage!=message.damage)
                return "Начальные HP, гранаты, урон или решения об атаках не совпадают с серверным источником.";
            if(new[]{message.floor,message.liftX,message.liftY,message.playerX,message.playerY}.Any(v=>!float.IsFinite(v)) ||
                Mathf.Abs(message.liftY-message.floor-cargo.anchorHeight-LiftHeight)>.001f)
                return "Некорректная геометрия подготовленной позиции и подъёма.";
            return null;
        }
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
                var lift=cargo.Position+Vector2.up*LiftHeight;
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
