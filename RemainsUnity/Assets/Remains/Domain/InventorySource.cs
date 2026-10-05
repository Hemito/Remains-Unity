using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Domain
{
    // Live view, never persisted: body items remain exclusively in ActorState.inventory.
    public sealed class InventorySource
    {
        public const string BodyPrefix="body:";
        private readonly ContainerState container;
        public ActorState Body {get;}
        private InventorySource(ContainerState container) {this.container=container;}
        private InventorySource(ActorState body) {Body=body;}
        public string id=>Body==null?container.id:BodyId(Body.id);
        public string level=>Body==null?container.level:Body.level;
        public float? Mass=>Body==null?container.mass:null;
        public bool IsLooseItem=>Body==null && container.presentationItem!=null;
        public Vector2 Position=>Body==null?container.Position:Body.Position;
        public List<ItemStack> items=>Body==null?container.items:Body.inventory;
        public Dictionary<string,string> Aliases=>Body==null?container.itemAliases:Body.inventoryAliases;
        public ItemStack Find(string item)=>InventoryStacks.Find(items,Aliases,item);
        public static string BodyId(string actor)=>BodyPrefix+actor;
        public static implicit operator InventorySource(ContainerState value)=>value==null?null:new(value);
        public static InventorySource Resolve(WorldState world,string id)
        {
            if(world==null || id==null) return null;
            var container=world.containers.Find(c=>c.id==id);
            if(id.StartsWith(BodyPrefix,StringComparison.Ordinal))
            {
                if(container!=null) throw new InvalidOperationException($"ID контейнера использует префикс инвентарей тел: {id}.");
                var body=world.Find(id.Substring(BodyPrefix.Length));return body==null || !Remains.Content.ActorTypes.Get(body.actorType).bodyInventory?null:new InventorySource(body);
            }
            return container;
        }
        public static IEnumerable<InventorySource> All(WorldState world)
        {
            foreach(var container in world.containers) yield return Resolve(world,container.id);
            foreach(var actor in world.actors) if(Remains.Content.ActorTypes.Get(actor.actorType).bodyInventory) yield return new InventorySource(actor);
        }
        public bool Reserve(string transaction,ItemStack item,int count,out ItemStack reserved)
        {
            reserved=null;
            if(item==null || !items.Contains(item) || count<=0 || count>item.count) return false;
            var copy=item.ReservedCopy(count);
            if(Body!=null)
            {
                if(!InventoryJournal.Debit(Body,transaction,item.id,count)) return false;
                if(Body.weaponInventoryVersion==1 && !string.IsNullOrEmpty(Body.equippedWeaponItem) && WeaponInventory.Selected(Body)==null)
                    WeaponInventory.Select(Body,null,out _);
                Body.revision++;
            }
            else {if(item.count==count) items.Remove(item);else item.count-=count;}
            reserved=copy;return true;
        }
        public void Add(ItemStack item) {InventoryStacks.Add(items,Aliases,item);if(Body!=null) Body.revision++;}
    }
}
