using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Runtime
{
    public sealed class JourneyInteractionStage
    {
        private readonly PrototypeRuntime runtime;
        private bool threw;
        private readonly Dictionary<string,float> health=new();
        private int damageSequence;
        public bool TelekinesisVerified {get;private set;}
        public bool GrenadeVerified {get;private set;}
        private bool Host=>runtime.Options.Role=="host";
        public JourneyInteractionStage(PrototypeRuntime value) {runtime=value;}
        public PlayerCommand Command(int phase)
        {
            var self=runtime.Session.LocalActor;
            var prop=runtime.Session.World.props.Single();
            if(Host && phase==15) return new PlayerCommand {telekinesis=true,hasAim=true,aim=prop.Position};
            if(Host && phase==16) return new PlayerCommand {telekinesis=true,hasAim=true,aim=new Vector2(-1,2)};
            if(phase==20)
            {
                if(Host && self.health<=70 && InventoryJournal.Count(self,"heal")>0) return new PlayerCommand {heal=true};
                float target=Host?5:11+int.Parse(self.character.Split('-').Last());
                return new PlayerCommand {movement=Vector2.right*(Mathf.Abs(self.x-target)>.05f?Mathf.Sign(target-self.x):0)};
            }
            if(phase==21 && Host && !threw)
            {threw=true;return new PlayerCommand {grenade=true,hasAim=true,aim=self.Position+Vector2.down*3};}
            return default;
        }
        public int Next(int phase)
        {
            var world=runtime.Session.World;var prop=world.props.Single();
            var host=world.Find("player:session-host");
            if(phase==15 && prop.held && prop.owner==host.owner) return 16;
            if(phase==16 && Vector2.Distance(prop.Position,new Vector2(-1,2))<.15f) return 17;
            if(phase==17 && !prop.held && prop.y<.4f)
            {
                if(host.mana>=100 || world.actors.Any(a=>a.player && a.id!=host.id && a.mana!=100))
                    throw new InvalidOperationException("Journey: неверный расход маны телекинеза.");
                TelekinesisVerified=true;return 4;
            }
            if(phase==20 && host.health>70 && Mathf.Abs(host.x-5)<.1f &&
                world.actors.Where(a=>a.player && a.connected).All(a=>Mathf.Abs(a.vx)<.1f && (a.id==host.id || a.x>11)))
            {
                foreach(var actor in world.actors.Where(a=>a.player)) health.Add(actor.id,actor.health);
                damageSequence=host.damageSequence;return 21;
            }
            if(phase==21 && world.explosions.Count==1 && world.grenades.Count==0)
            {
                string id=world.explosions[0].attack.id;
                if(world.actors.Any(a=>a.player && !a.resolvedExplosions.Contains(id))) return phase;
                foreach(var actor in world.actors.Where(a=>a.player))
                    if(Mathf.Abs(actor.health-(health[actor.id]-(actor.id==host.id?70:0)))>.001f ||
                        InventoryJournal.Count(actor,"grenade")!=(actor.id==host.id?4:5) ||
                        actor.resolvedExplosions.Count(x=>x==id)!=1)
                        throw new InvalidOperationException($"Journey: неверный результат гранаты у {actor.id}, hp={actor.health}.");
                if(host.damageSequence!=damageSequence+1) throw new InvalidOperationException("Journey: повторный урон гранаты.");
                GrenadeVerified=true;return 9;
            }
            return phase;
        }
    }
}
