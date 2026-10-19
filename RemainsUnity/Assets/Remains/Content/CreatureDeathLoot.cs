using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Content
{
    // Host accepted-state path only. The caller checkpoints the actor, loose items and receipt together.
    // Client replicas and cosmetic DamageFeedback must never call this service.
    public static class CreatureDeathLoot
    {
        public static string Receipt(ActorState actor)=>"npc-death-loot:"+actor.id+":"+actor.lifecycleSequence;
        private static bool Transition(WorldState world,ActorState before,ActorState after)=>world!=null && before!=null && after!=null &&
            ReferenceEquals(world.Find(before.id),before) && !before.player && !after.player && !before.down && before.health>0 &&
            after.down && after.health==0 && before.id==after.id && before.actorType==after.actorType && before.level==after.level &&
            !string.IsNullOrEmpty(before.owner) && before.owner==after.owner &&
            before.lifecycleSequence>=0 && ActorVersion.CanPublishOwnerState(after,before);
        public static Plan Prepare(WorldState world,ActorState before,ActorState after,Func<string,ContainerLootTable> tableForType=null)
        {
            if(!Transition(world,before,after))return null;
            var table=(tableForType??SourceTable)(before.actorType);if(table==null)return null;
            if(CreatureDeathLootIds.Completed(world,Receipt(before)))return null;
            table.Validate();
            return Plan.Create(world,before,after,table);
        }
        private static ContainerLootTable SourceTable(string type)
        {
            var entry=CreatureCatalog.Load().Creatures.FirstOrDefault(c=>c.actorType==type && c.family==NpcCreatureBehaviours.Turret);
            if(entry==null)return null;
            if(entry.deathLoot==null)throw new InvalidOperationException("Не подготовлена исходная добыча турели: actorType="+type);
            return entry.deathLoot.Read();
        }
        public sealed class Plan
        {
            private readonly string worldId,receipt;
            private readonly ActorState before,after;
            private readonly string beforeJson,afterJson;
            private readonly long worldRevision;
            private readonly ContainerState[] drops;
            private Plan(WorldState world,ActorState before,ActorState after,ContainerState[] drops)
            {worldId=world.worldId;receipt=Receipt(before);this.before=before;this.after=after;beforeJson=StateJson.ToJson(before);afterJson=StateJson.ToJson(after);worldRevision=world.revision;this.drops=drops;}
            internal static Plan Create(WorldState world,ActorState before,ActorState after,ContainerLootTable table)
            {
                var items=table.Generate(world.worldId,Receipt(before));
                var drops=new List<ContainerState>();
                if(!float.IsFinite(after.x) || !float.IsFinite(after.y))throw new InvalidOperationException("Нечисловая позиция гибели NPC: "+Receipt(before));
                // UnitTurret inherits Unit.dropLoot; Interact.loot uses owner.Y-owner.scY/2.
                // scY is phis@sY (Unit.setXml); turrets have trup=0, so Unit.die does not replace scY with sitY.
                var source=CreatureCatalog.Load().Require(after.actorType);
                if(!float.IsFinite(source.deathLootHeight)||source.deathLootHeight<=0)throw new InvalidOperationException("Нет исходного scY для добычи NPC: "+after.actorType);
                Vector2 origin=after.Position+Vector2.up*(source.deathLootHeight*.5f);
                foreach(var item in items)
                {
                    // Preview requires the shared baked opaque shape before any world mutation.
                    item.id=CreatureDeathLootIds.ItemPrefix+item.id;
                    var drop=DroppedItemTransfer.Preview(after,"npc:"+item.id,item.definition,origin,JumpVelocity(Receipt(before),item.id));
                    drop.id=CreatureDeathLootIds.DropPrefix+item.id;
                    drop.items.Add(item);drops.Add(drop);
                }
                var result=new Plan(world,before,after,drops.ToArray());result.Validate(world);CreatureDeathLootIds.Validate(world,result.Ids());return result;
            }
            private void Validate(WorldState world)
            {
                if(world==null || world.worldId!=worldId || world.revision!=worldRevision || !Transition(world,before,after) || StateJson.ToJson(before)!=beforeJson || StateJson.ToJson(after)!=afterJson)
                    throw new InvalidOperationException("Изменилось принятое состояние гибели NPC: "+receipt);
                if(world.revision==long.MaxValue)throw new InvalidOperationException("Исчерпана ревизия мира при гибели NPC: "+receipt);
            }
            private IEnumerable<string> Ids()=>drops.Select(d=>d.id).Concat(drops.SelectMany(d=>d.items).Select(i=>i.id));
            // Replaces the still-canonical living record and publishes all loot atomically after preflight.
            // CoopSession.UpdateActor advances world.revision and broadcasts its existing death checkpoint next.
            public bool ApplyAccepted(WorldState world)
            {
                if(world==null || world.worldId!=worldId)throw new InvalidOperationException("Мир квитанции гибели NPC изменился: "+receipt);
                if(CreatureDeathLootIds.Completed(world,receipt))return false;
                Validate(world);
                CreatureDeathLootIds.Validate(world,Ids());CreatureDeathLootIds.Reserve(world,Ids());
                world.actors[world.actors.IndexOf(before)]=after;
                world.containers.AddRange(drops);world.completedTransactions.Add(receipt);CreatureDeathLootIds.Complete(world,receipt);
                WorldChanges.Mark(world,after);foreach(var drop in drops)WorldChanges.Mark(world,drop);
                return true;
            }
        }
        // Original Loot(jump=true): dx=random*10-5; dy=random*5-10, source y points down.
        // px/frame -> m/s is explicitly 30/50. Motion uses an independent deterministic stream.
        private static Vector2 JumpVelocity(string receipt,string item)
        {
            byte[] hash;using(var sha=SHA256.Create())hash=sha.ComputeHash(Encoding.UTF8.GetBytes(receipt+"|"+item+"|loot-jump-v1"));
            double U(int i)=>((uint)hash[i]|(uint)hash[i+1]<<8|(uint)hash[i+2]<<16|(uint)hash[i+3]<<24)/4294967296d;
            const float conversion=30f/50;
            return new Vector2((float)(U(0)*10-5)*conversion,(float)(10-U(4)*5)*conversion);
        }
    }
}
