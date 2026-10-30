using System;
using System.Linq;
using Mirror;
using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Networking
{
    // Host: a creature whose accepted death is an explosion (ActorTypeDefinition.deathExplosion; UnitMsp.dropLoot →
    // Unit.explosion: a blast at X, Y − 3 px) records it as any explosion — the present players decide their own contact,
    // the host lists the non-player actors it hit — and the blast reaches the hazards around it. Killed or self-destroyed
    // alike: the death the owner publishes is the trigger, once per life (the stored actor is down afterwards).
    public sealed class SessionDeathBlasts
    {
        private readonly CoopSession session;
        public int Explosions {get;private set;}
        public SessionDeathBlasts(CoopSession session)=>this.session=session;

        // The explosion of a death transition (before alive, after dead), or null when the type has none.
        public static AttackEvent Blast(ActorState before,ActorState after)
        {
            if(before==null || after==null || before.player || before.down || before.health<=0 || !after.down || after.health>0) return null;
            string weapon=ActorTypes.Get(after.actorType).deathExplosion;
            if(string.IsNullOrWhiteSpace(weapon)) return null;
            if(!WeaponCatalog.IsExplosive(weapon)) throw new InvalidOperationException($"Взрыв гибели {after.actorType}: {weapon} не взрывчатое оружие.");
            var centre=after.Position+Vector2.up*(3/50f);
            return new AttackEvent {id="death-blast:"+after.id+":"+before.lifecycleSequence,attacker=after.id,level=after.level,sourcePlayer=false,weapon=weapon,
                factoryEnemy=FactoryEnemyScaling.Capture(after.factoryEnemy),originX=centre.x,originY=centre.y,endX=centre.x,endY=centre.y};
        }

        // After the death is stored: records the explosion, commits the world with both and sends the blast.
        public void Detonate(AttackEvent blast)
        {
            var world=session.ServerWorld;
            if(world==null || blast==null) throw new InvalidOperationException("Взрыв гибели вне мира хозяина: "+blast?.id);
            var hits=world.actors.Where(actor=>!actor.player && AttackScope.Includes(blast,actor) && HitGeometry.Contacts(actor,blast)).Select(actor=>actor.id).ToArray();
            ExplosionRules.Record(world,blast,hits,true);Explosions++;
            session.CommitWorld();
            NetworkServer.SendToAll(AttackMessage.From(blast));
            session.Hazards.Blast(blast);
        }
    }
}
