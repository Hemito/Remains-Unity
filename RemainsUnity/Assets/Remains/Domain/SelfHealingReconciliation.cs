using System;
using System.Collections.Generic;
using UnityEngine;
using Remains.Serialization;

namespace Remains.Domain
{
    // Session-local intent/observations, not saved actor state. Only a recoverable exact
    // transaction grants the old-life -> old-life+1 exception for an active self-heal.
    public sealed class SelfHealingReconciliation
    {
        private sealed class Request
        {
            public string transaction,world,actor,owner,level;
            public int generation,lifecycle,damage,acceptedLifecycle;
            public readonly Dictionary<int,float> healthByDamage=new();
        }
        private readonly Action<string> diagnostic;
        public SelfHealingReconciliation(Action<string> diagnostic)
        {this.diagnostic=diagnostic??throw new ArgumentNullException(nameof(diagnostic));}
        private readonly Dictionary<string,Request> pending=new(StringComparer.Ordinal);
        private readonly Dictionary<string,Request> acknowledged=new(StringComparer.Ordinal);
        public bool RequiresCanonical(string actorId)
        {
            if(acknowledged.ContainsKey(actorId)) return true;
            foreach(var request in pending.Values) if(request.actor==actorId) return true;
            return false;
        }
        // A journal-only delta can complete a heal even when its actor text did not change. Materialize only
        // those exceptional projections before any normalization/reconciliation touches the incoming graph.
        public void MaterializeCanonical(WorldState incoming,HashSet<string> reused,Func<string,ActorState> copyCanonical)
        {
            if(reused==null || reused.Count==0 || pending.Count==0 && acknowledged.Count==0) return;
            for(int i=0;i<incoming.actors.Count;i++)
            {
                string id=incoming.actors[i].id;
                if(!reused.Contains(id) || !RequiresCanonical(id)) continue;
                var actor=copyCanonical?.Invoke(id);
                if(actor==null || actor.id!=id) throw new InvalidOperationException("Нет канонического актёра для согласования самолечения: actor="+id);
                incoming.actors[i]=actor;reused.Remove(id);
            }
        }
        public void Capture(string worldId,string transaction,ActorState actor)
        {
            if(string.IsNullOrWhiteSpace(worldId) || string.IsNullOrWhiteSpace(transaction) || actor==null ||
                string.IsNullOrWhiteSpace(actor.id) || string.IsNullOrWhiteSpace(actor.owner) || !actor.player || !actor.connected || actor.down)
                throw new InvalidOperationException($"Нельзя зарегистрировать самолечение: world={worldId}, transaction={transaction}, actor={actor?.id}, owner={actor?.owner}, down={actor?.down}.");
            if(pending.ContainsKey(transaction)) throw new InvalidOperationException("Запрос самолечения уже зарегистрирован: "+transaction);
            var request=new Request {transaction=transaction,world=worldId,actor=actor.id,owner=actor.owner,level=actor.level,
                generation=actor.generation,lifecycle=actor.lifecycleSequence,damage=actor.damageSequence};
            Record(request,actor);pending.Add(transaction,request);
        }
        public void Observe(string worldId,ActorState actor)
        {
            foreach(var request in pending.Values)
                if(request.world==worldId && Identity(request,actor) && actor.lifecycleSequence==request.lifecycle) Record(request,actor);
        }
        public void Reject(string transaction)=>pending.Remove(transaction);
        public void Reset() {pending.Clear();acknowledged.Clear();}
        public bool AwaitingCommit(string worldId,ICollection<string> completed,ActorState current,ActorState incoming,string localOwner)
        {
            foreach(var request in pending.Values)
                if(Transition(request,worldId,current,incoming,localOwner) &&
                    (current.lifecycleSequence==request.lifecycle && !current.down || current.down && current.damageSequence>request.damage &&
                        current.lifecycleSequence>=request.lifecycle && current.lifecycleSequence<=incoming.lifecycleSequence) &&
                    !completed.Contains(request.transaction)) return true;
            return false;
        }
        public bool DiscardPreHealingState(string worldId,ActorState current,ActorState incoming,string localOwner)
        {
            return current!=null && acknowledged.TryGetValue(current.id,out var request) && request.world==worldId &&
                Identity(request,current) && Identity(request,incoming) && current.owner==localOwner &&
                current.lifecycleSequence==request.acceptedLifecycle && incoming.lifecycleSequence>=request.lifecycle && incoming.lifecycleSequence<request.acceptedLifecycle;
        }
        public bool TryApply(string worldId,ICollection<string> completed,ActorState current,ref ActorState incoming,string localOwner)
        {
            foreach(var request in pending.Values)
            {
                if(!Transition(request,worldId,current,incoming,localOwner) || !completed.Contains(request.transaction)) continue;
                bool active=current.lifecycleSequence==request.lifecycle && !current.down;
                bool laterDown=current.down && current.damageSequence>request.damage &&
                    current.lifecycleSequence>=request.lifecycle;
                if(!active && !laterDown) continue;
                if(laterDown)
                {
                    // The latest owner-decided unconscious body is the source here, not
                    // the request-time alive pose. Ordinary PassiveActorMotion normalizes
                    // its locomotion flags; this acknowledgement performs no physics step.
                    if(!InventoryJournal.Merge(incoming,current,out var inventory,out var uses))
                        throw new InvalidOperationException($"Не удалось сохранить расход самолечения: transaction={request.transaction}, actor={current.id}.");
                    long revision=Math.Max(incoming.revision,current.revision);
                    if(revision==long.MaxValue) throw new InvalidOperationException("Исчерпана ревизия самолечения: actor="+current.id);
                    var authoritative=incoming;
                    incoming=current.Copy();incoming.inventory=inventory;incoming.inventoryUses=uses;
                    incoming.inventoryAliases=authoritative.inventoryAliases;incoming.inventoryReservations=authoritative.inventoryReservations;
                    incoming.holdEpoch=authoritative.holdEpoch;incoming.hold=StateJson.Copy(authoritative.hold);
                    incoming.lifecycleSequence=Math.Max(current.lifecycleSequence,authoritative.lifecycleSequence);incoming.revision=revision+1;
                    if(current.lifecycleSequence>authoritative.lifecycleSequence)
                        diagnostic($"Сохранено позднее падение после самолечения: world={worldId}, transaction={request.transaction}, actor={current.id}, lifecycle={current.lifecycleSequence}/{authoritative.lifecycleSequence}. Публикация сохранённого тела допустима только при совпадении текущей жизни с координатором.");
                    request.acceptedLifecycle=incoming.lifecycleSequence;
                    pending.Remove(request.transaction);acknowledged[incoming.id]=request;request.healthByDamage.Clear();return true;
                }
                Record(request,current);
                float authoritativeHealth=incoming.health;int authoritativeDamage=incoming.damageSequence;long authoritativeRevision=incoming.revision;
                if(authoritativeDamage<request.damage)
                    throw new InvalidOperationException($"Подтверждение самолечения откатило урон: transaction={request.transaction}, actor={incoming.id}, damage={authoritativeDamage}/{request.damage}.");
                float health=authoritativeHealth;
                if(current.damageSequence>=authoritativeDamage)
                {
                    if(!request.healthByDamage.TryGetValue(authoritativeDamage,out float observed))
                        throw new InvalidOperationException($"Нет локальной выборки урона для самолечения: transaction={request.transaction}, actor={current.id}, damage={authoritativeDamage}, local={current.damageSequence}, lifecycle={current.lifecycleSequence}.");
                    // Authoritative health already includes the heal and damage through its
                    // sequence. Add only owner changes after that exact health observation.
                    health=Mathf.Clamp(authoritativeHealth+current.health-observed,0,100);
                }
                if(current.revision>authoritativeRevision)
                {incoming.extensions=current.extensions;incoming.mana=current.mana;incoming.magazines=current.magazines;}
                incoming.health=health;
                if(current.damageSequence>authoritativeDamage)
                {
                    incoming.damageSequence=current.damageSequence;
                    incoming.resolvedAttacks=Union(incoming.resolvedAttacks,current.resolvedAttacks);
                    incoming.resolvedExplosions=Union(incoming.resolvedExplosions,current.resolvedExplosions);
                }
                // Publish the once-reconciled owner checkpoint after installation. Old-life
                // reports could not publish while the canonical heal was awaiting delivery.
                incoming.revision=Math.Max(authoritativeRevision,current.revision);
                if(incoming.revision==long.MaxValue) throw new InvalidOperationException("Исчерпана ревизия самолечения: actor="+incoming.id);
                incoming.revision++;
                ActorPose.CopyMotion(current,incoming);
                request.acceptedLifecycle=incoming.lifecycleSequence;
                pending.Remove(request.transaction);acknowledged[incoming.id]=request;
                request.healthByDamage.Clear();return true;
            }
            return false;
        }
        private static void Record(Request request,ActorState actor)
        {
            if(!float.IsFinite(actor.health) || actor.damageSequence<request.damage)
                throw new InvalidOperationException($"Некорректная выборка самолечения: transaction={request.transaction}, actor={actor.id}, health={actor.health}, damage={actor.damageSequence}/{request.damage}.");
            if(!request.healthByDamage.ContainsKey(actor.damageSequence)) request.healthByDamage.Add(actor.damageSequence,actor.health);
        }
        private static bool Identity(Request request,ActorState actor)=>actor!=null && actor.id==request.actor && actor.owner==request.owner &&
            actor.generation==request.generation && actor.level==request.level && actor.player && actor.connected;
        private static bool Transition(Request request,string worldId,ActorState current,ActorState incoming,string localOwner)=>
            request.world==worldId && Identity(request,current) && Identity(request,incoming) && current.owner==localOwner &&
            !incoming.down && incoming.health>0 && request.lifecycle<int.MaxValue && incoming.lifecycleSequence==request.lifecycle+1;
        private static List<string> Union(List<string> authoritative,List<string> owner)
        {
            var result=new List<string>(authoritative);
            foreach(string id in owner) if(!result.Contains(id)) result.Add(id);
            return result;
        }
    }
}
