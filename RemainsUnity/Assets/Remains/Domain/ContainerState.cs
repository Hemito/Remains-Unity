using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Domain
{
    [Serializable]
    public sealed class ContainerState : PropState
    {
        public const string PrototypeId="container:prototype";
        public ContainerState() {id=PrototypeId;definition="mcrate1";x=-1.5f;mass=25;width=height=1.2f;anchorHeight=0;}
        public List<ItemStack> items=new();
        public string presentationItem; // Loose item: its actual icon, not a physical box definition.
        public Dictionary<string,string> itemAliases=new();
    }
    public static class ContainerAccess
    {
        private static readonly ILootRule defaults=new NearbyLootRule();
        public static string Rejection(ActorState actor,InventorySource container,ILootRule rule=null)
        {
            if(actor==null) return "Подбор недоступен: персонаж отсутствует.";
            if(container==null) return "Контейнер больше недоступен.";
            return (rule??defaults).AccessRejection(new LootAccessContext(actor,container));
        }
        public static string ClaimRejection(ActorState actor,InventorySource container,ItemStack item,ILootRule rule=null,int quantity=0)
        {
            string reason=Rejection(actor,container,rule);
            if(reason!=null) return reason;
            if(item==null || item.count<=0) return "Этот предмет уже забрали.";
            if(quantity<0 || quantity>item.count) return "Указанное количество недоступно. Обновите выбор.";
            return (rule??defaults).ClaimRejection(new LootClaimContext(new LootAccessContext(actor,container),item,quantity));
        }
        public static bool Accessible(ActorState actor,InventorySource source,ILootRule loot,IDepositRule deposit)
            =>actor!=null && source!=null && (Rejection(actor,source,loot)==null || (deposit!=null && deposit.AccessRejection(new LootAccessContext(actor,source))==null));
        public static InventorySource Nearest(WorldState world,ActorState actor,ILootRule rule=null,IDepositRule depositRule=null)
        {
            if(world==null || actor==null) return null;
            InventorySource nearest=null;float distance=float.PositiveInfinity;
            foreach(var container in InventorySource.All(world))
            {
                if(!Accessible(actor,container,rule,depositRule)) continue;
                float next=Vector2.Distance(actor.Position,container.Position);
                if(next>distance) continue;
                distance=next;nearest=container;
            }
            return nearest;
        }
    }
}
