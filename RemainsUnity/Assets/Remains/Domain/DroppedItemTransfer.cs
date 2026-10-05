using System;
using UnityEngine;

namespace Remains.Domain
{
    // A loose item uses the same recoverable inventory transfer as a container deposit.
    // The empty target and pending item are saved together before the item becomes claimable.
    public static class DroppedItemTransfer
    {
        public const float Width=.35f,Height=.3f;
        public static ContainerState Preview(ActorState actor,string transaction,string definition,Vector2 origin,Vector2 velocity,float angle=0,bool mirrored=false)
            =>new ContainerState {id="drop:"+transaction,definition="loose-item",presentationItem=definition,level=actor.level,
                x=origin.x,y=origin.y,vx=velocity.x,vy=velocity.y,width=Width,height=Height,anchorHeight=Height*.5f,presentationAngle=angle,presentationMirrored=mirrored,
                mass=.1f,owner=actor.owner,generation=1};
        public static bool Prepare(WorldState world,ActorState actor,string transaction,string itemId,int quantity,
            Vector2 origin,Vector2 velocity,IDepositRule rule,out string reason,bool paused=false,float angle=0,bool mirrored=false)
        {
            reason="Бросок предмета недоступен: состояние или точка запуска изменились.";
            if(actor==null || !actor.player || !actor.connected || actor.down ||
                !float.IsFinite(angle) || !float.IsFinite(origin.x) || !float.IsFinite(origin.y) || !float.IsFinite(velocity.sqrMagnitude) ||
                Vector2.Distance(origin,actor.Position)>2 || InventoryTransactions.Contains(world,transaction)) return false;
            var item=InventoryStacks.Find(actor,itemId);
            if(item==null || quantity<0) return false;
            int available=InventoryAvailability.Count(actor,item),amount=quantity==0?available:quantity;
            if(amount<=0 || amount>available) return false;
            string id="drop:"+transaction;
            if(WorldProps.Find(world,id)!=null) return false;
            var drop=Preview(actor,transaction,item.definition,origin,velocity,angle,mirrored);
            world.containers.Add(drop);
            if(new SharedDeposits(world,rule).Prepare(transaction,actor,id,item.id,amount,out reason,paused,InventoryTransactions.TossPurpose)) return true;
            world.containers.Remove(drop);return false;
        }
        public static bool PruneEmpty(WorldState world)
        {
            int count=world.containers.RemoveAll(container=>container.presentationItem!=null && container.items.Count==0 &&
                !world.pendingDeposits.Exists(value=>value.containerId==container.id) && !world.pendingGrants.Exists(value=>value.containerId==container.id));
            if(count>0) world.revision++;
            return count>0;
        }
    }
}
