using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Remains.Domain
{
    // Live view, never persisted: body items remain exclusively in ActorState.inventory.
    public sealed class InventorySource
    {
        public const string BodyPrefix="body:";
        // Ephemeral views follow object identity, never ID/revision. A new snapshot
        // gets its own view; an unchanged/moving source does not allocate every frame.
        private static readonly ConditionalWeakTable<ContainerState,InventorySource> containers=new();
        private static readonly ConditionalWeakTable<ActorState,InventorySource> bodies=new();
        private static readonly ConditionalWeakTable<ContainerState,InventorySource>.CreateValueCallback createContainer=CreateContainer;
        private static readonly ConditionalWeakTable<ActorState,InventorySource>.CreateValueCallback createBody=CreateBody;
        private readonly ContainerState container;
        private string bodyActorId,bodySourceId;
        public ActorState Body {get;}
        private InventorySource(ContainerState container) {this.container=container;}
        private InventorySource(ActorState body) {Body=body;bodyActorId=body.id;bodySourceId=BodyId(body.id);}
        public string id
        {
            get
            {
                if(Body==null) return container.id;
                if(bodyActorId!=Body.id) {bodyActorId=Body.id;bodySourceId=BodyId(Body.id);}
                return bodySourceId;
            }
        }
        public string level=>Body==null?container.level:Body.level;
        public float? Mass=>Body==null && container.hasPhysicalBody?container.mass:null;
        public bool IsLooseItem=>Body==null && container.presentationItem!=null;
        public Vector2 Position=>Body==null?container.Position:Body.Position;
        public Rect Bounds
        {
            get
            {
                if(Body==null) return new Rect(container.x-container.width*.5f,container.y-container.anchorHeight,container.width,container.height);
                return BodyBounds(Body);
            }
        }
        public static Rect BodyBounds(ActorState actor)
        {
            var hull=Anatomy.Movement(actor);
            return new Rect(actor.x-hull.width*.5f,actor.y,hull.width,hull.height);
        }
        public List<ItemStack> items=>Body==null?container.items:Body.inventory;
        public Dictionary<string,string> Aliases=>Body==null?container.itemAliases:Body.inventoryAliases;
        public ItemStack Find(string item)=>InventoryStacks.Find(items,Aliases,item);
        public static string BodyId(string actor)=>BodyPrefix+actor;
        public static implicit operator InventorySource(ContainerState value)=>value==null?null:containers.GetValue(value,createContainer);
        private static InventorySource CreateContainer(ContainerState value)=>new(value);
        private static InventorySource CreateBody(ActorState value)=>new(value);
        public static InventorySource FromBody(ActorState body)=>body==null || !Remains.Content.ActorTypes.Get(body.actorType).bodyInventory?
            null:bodies.GetValue(body,createBody);
        public static InventorySource Resolve(WorldState world,string id)
        {
            if(world==null || id==null) return null;
            ContainerState container=null;
            foreach(var candidate in world.containers) if(candidate.id==id) {container=candidate;break;}
            if(id.StartsWith(BodyPrefix,StringComparison.Ordinal))
            {
                if(container!=null) throw new InvalidOperationException($"ID контейнера использует префикс инвентарей тел: {id}.");
                foreach(var body in world.actors)
                    if(body.id!=null && id.Length==BodyPrefix.Length+body.id.Length && string.CompareOrdinal(id,BodyPrefix.Length,body.id,0,body.id.Length)==0) return FromBody(body);
                return null;
            }
            return container;
        }
        public static IEnumerable<InventorySource> All(WorldState world)
        {
            foreach(var container in world.containers)
            {
                if(container.id.StartsWith(BodyPrefix,StringComparison.Ordinal))
                    throw new InvalidOperationException($"ID контейнера использует префикс инвентарей тел: {container.id}.");
                yield return container;
            }
            foreach(var actor in world.actors) {var source=FromBody(actor);if(source!=null) yield return source;}
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
