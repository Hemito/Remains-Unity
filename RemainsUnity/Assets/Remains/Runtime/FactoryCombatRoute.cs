using System;
using System.Linq;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Runtime
{
    // Test input for the authored room; never changes actors, doors or health directly.
    internal sealed class FactoryCombatRoute
    {
        private readonly PrototypeRuntime runtime;
        private bool jumped,requestedDoor;
        public bool Complete {get;private set;}
        public string NpcId {get;private set;}
        public FactoryCombatRoute(PrototypeRuntime runtime)=>this.runtime=runtime;
        private ActorState Enemy=>runtime.Session.World.actors.Single(a=>!a.player && a.level=="factory:0");
        private DoorState Door=>runtime.Session.World.doors.Single(d=>d.level=="factory:0" && d.definition=="door1");
        public PlayerCommand Command()
        {
            var actor=runtime.Session.LocalActor;
            if(actor==null || Complete) return default;
            if(actor.x<21.7f)
            {
                bool jump=!jumped && actor.x>=17.2f;
                if(jump) jumped=true;
                return new PlayerCommand {movement=Vector2.right,jump=jump,jumpHeld=actor.x<20};
            }
            if(!Door.open)
            {
                if(runtime.Options.Role=="host" && !requestedDoor)
                {
                    var target=InteractionSelection.Select(runtime.Session.World,actor,runtime.Session.Rules);
                    if(target.kind!=InteractionKind.Door || target.id!=Door.id)
                        throw new InvalidOperationException("Боевой маршрут: у двери выбран другой объект.");
                    requestedDoor=true;return new PlayerCommand {interact=true};
                }
                return default;
            }
            var enemy=Enemy;NpcId=enemy.id;
            if(enemy.down) {Complete=true;return default;}
            float targetX=runtime.Options.Role=="host"?26:24.5f;
            return new PlayerCommand {movement=actor.x<targetX?Vector2.right:Vector2.zero,
                weapon="pistol",hasAim=true,aim=enemy.Position+Vector2.up*.65f,
                fire=runtime.Options.Role=="host" && actor.x>=25,reload=ProbeWeaponSelection.Magazine(actor,"pistol").rounds==0,
                heal=actor.health<60 && InventoryJournal.Count(actor,"heal")>0};
        }
        public bool Saved(WorldState world)=>NpcId!=null && world.Find(NpcId) is {down:true,health:<=0} actor && actor.resolvedAttacks.Count>0;
    }
}
