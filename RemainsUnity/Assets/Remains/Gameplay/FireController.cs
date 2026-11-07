using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using Remains.Networking;

namespace Remains.Gameplay
{
    // Fire (FireRules) for the actors this peer simulates: a pulse of a fire hurts an actor touching any of its patches
    // once and sets it burning; burning hurts every second until it ends or the actor leaves the level. The host removes
    // burnt-out fires. Each fire's patches are flown once per peer and kept. Cost: nothing without fires; with fires, a
    // pass over the owned actors of their levels.
    public sealed class FireController
    {
        private readonly CoopSession session;
        private readonly SimulationGeometry geometry;
        private readonly CombatController combat;
        private readonly Dictionary<string,List<FirePatch>> patches=new(StringComparer.Ordinal);
        private readonly HashSet<string> live=new(StringComparer.Ordinal);
        private readonly List<ActorState> burningActors=new();
        public int Hits {get;private set;}
        public FireController(CoopSession session,SimulationGeometry geometry,CombatController combat)
        {this.session=session;this.geometry=geometry;this.combat=combat;}

        // The patches of a fire, flown once (null while its level's geometry is not here).
        public IReadOnlyList<FirePatch> Patches(FireState fire)
        {
            if(patches.TryGetValue(fire.id,out var known)) return known;
            var room=geometry.Find(fire.level);if(room==null && fire.dropletPaths==null) return null;
            return patches[fire.id]=FireRules.Patches(fire,room);
        }

        public void Step(ActorFrameIndex actors=null)
        {
            var world=session.World;
            if(world==null) return;
            if(session.ServerWorld!=null && session.ServerWorld.fires.Count>0 &&
                FireRules.Expire(session.ServerWorld,level=>session.LevelTime.ReadServer(level))) session.CommitWorld();
            live.Clear();foreach(var fire in world.fires) live.Add(fire.id);
            if(patches.Count>live.Count) {var gone=new List<string>();foreach(var id in patches.Keys) if(!live.Contains(id)) gone.Add(id);foreach(var id in gone) patches.Remove(id);}
            var candidates=world.actors;
            if(actors!=null && world.fires.Count==0)
            {
                actors.CollectBurning(burningActors);
                candidates=burningActors;
            }
            foreach(var actor in candidates)
            {
                if(actor.owner!=session.LocalOwner || !ActorPresence.IsPresent(actor) || actor.health<=0 || session.Pause.IsPaused(actor.level) ||
                    !session.LevelTime.HasLocal(actor.level)) continue;
                double now=session.LevelTime.ReadLocal(actor.level);
                bool changed=Burn(actor,now);
                foreach(var fire in world.fires)
                {
                    if(fire.level!=actor.level) continue;
                    int pulse=FireRules.Pulse(fire,now);if(pulse<0) continue;
                    string contact="fire:"+fire.id+":"+pulse;
                    if(actor.resolvedAttacks.Contains(contact) || Patches(fire) is not { } burning) continue;
                    var profile=FireRules.Profile(fire);
                    double pulseAge=pulse*(double)profile.pulseSeconds;
                    if(!Touching(burning,actor,now-fire.start,pulseAge,fire.torch?fire.fuel:profile.PatchSeconds)) continue;
                    if(!combat.ApplyContact(actor,contact,DamageTypes.Fire,profile.pulseDamage)) continue;
                    Hits++;changed=true;
                    if(profile.igniteDamage>0 && profile.igniteSeconds>0) Ignite(actor,now,profile);
                }
                if(changed) session.PublishActor(actor);
            }
        }
        private static bool Touching(IReadOnlyList<FirePatch> burning,ActorState actor,double age,double pulseAge,double seconds)
        {
            foreach(var patch in burning)
                if(pulseAge>=patch.landedAt && patch.IsBurning(age,seconds) && FireRules.Touches(patch,actor)) return true;
            return false;
        }
        // Unit.damage → addEffect("burning", dopDamage): the longer and the stronger of the old and the new burning.
        private static void Ignite(ActorState actor,double now,FireSplash profile)
        {
            if(actor.burnLevel!=actor.level || actor.burnUntil<=now) {actor.burnLevel=actor.level;actor.burnNext=now+1;actor.burnDamage=0;}
            actor.burnUntil=Math.Max(actor.burnUntil,now+profile.igniteSeconds);actor.burnDamage=Math.Max(actor.burnDamage,profile.igniteDamage);
            actor.revision++;
        }
        // Effect.secEffect "burning": its damage once a second while it lasts; gone on another level or when over.
        private bool Burn(ActorState actor,double now)
        {
            if(string.IsNullOrEmpty(actor.burnLevel)) return false;
            if(actor.burnLevel!=actor.level || now>=actor.burnUntil) {actor.burnLevel=null;actor.burnUntil=actor.burnNext=0;actor.burnDamage=0;actor.revision++;return true;}
            if(now<actor.burnNext) return false;
            string contact="burn:"+actor.id+":"+actor.burnNext.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
            actor.burnNext+=1;actor.revision++;
            combat.ApplyContact(actor,contact,DamageTypes.Fire,actor.burnDamage);
            return true;
        }
    }
}
