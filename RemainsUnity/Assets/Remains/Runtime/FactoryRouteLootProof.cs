using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Domain;

namespace Remains.Runtime
{
    // Read-only route evidence. SharedInventory owns reservation, grant and persistence.
    public static class FactoryRouteLootProof
    {
        // The legacy DTO has no extension fields. This overload binds its structural receipt.
        public static bool MatchesReservation(PendingGrant grant,string expectedSourceId,WarehouseLowerClaim claim)
            =>MatchesReservationFields(grant,expectedSourceId,claim,null,false);

        // Callers with the captured original stack also bind its complete mod extensions.
        public static bool MatchesReservation(PendingGrant grant,string expectedSourceId,WarehouseLowerClaim claim,ItemStack expectedItem)
            =>MatchesReservationFields(grant,expectedSourceId,claim,expectedItem,true);

        private static bool MatchesReservationFields(PendingGrant grant,string expectedSourceId,WarehouseLowerClaim claim,
            ItemStack expectedItem,bool requireExpected)
            =>!string.IsNullOrEmpty(expectedSourceId) && grant?.item!=null && claim!=null &&
                !string.IsNullOrEmpty(claim.transaction) && !string.IsNullOrEmpty(claim.recipient) &&
                !string.IsNullOrEmpty(claim.item) && !string.IsNullOrEmpty(claim.definition) && claim.count>0 &&
                grant.transaction==claim.transaction && grant.containerId==expectedSourceId && grant.recipient==claim.recipient &&
                grant.item.id==claim.item && grant.item.definition==claim.definition && grant.item.count==claim.count &&
                (!requireExpected || expectedItem!=null && expectedItem.id==grant.item.id && expectedItem.count==grant.item.count &&
                    InventoryStacks.Compatible(expectedItem,grant.item));

        public static bool SameItems(IEnumerable<ItemStack> first,IEnumerable<ItemStack> second)
        {
            if(first==null || second==null) return false;
            var a=first.ToArray();var b=second.ToArray();
            if(a.Any(i=>!ValidItem(i)) || b.Any(i=>!ValidItem(i)) ||
                a.Select(i=>i.id).Distinct(StringComparer.Ordinal).Count()!=a.Length ||
                b.Select(i=>i.id).Distinct(StringComparer.Ordinal).Count()!=b.Length) return false;
            a=a.OrderBy(i=>i.id,StringComparer.Ordinal).ToArray();b=b.OrderBy(i=>i.id,StringComparer.Ordinal).ToArray();
            return a.Length==b.Length && a.Select((i,n)=>i.id==b[n].id && i.count==b[n].count && InventoryStacks.Compatible(i,b[n])).All(v=>v);
        }

        public static bool Confirms(WorldState world,string expectedSourceId,WarehouseLowerLootEvidence plan,
            WarehouseLowerClaim[] claims,out string reason)
        {
            reason="Нет полного наблюдения мира/выдачи: source="+expectedSourceId;
            if(world?.containers==null || world.actors==null || world.pendingGrants==null || world.completedTransactions==null ||
                string.IsNullOrEmpty(expectedSourceId) || plan?.source!=expectedSourceId || plan.items==null ||
                plan.players==null || plan.players.Length==0 || claims==null || plan.items.Any(i=>!ValidItem(i)) ||
                plan.players.Any(p=>p==null || string.IsNullOrEmpty(p.actor)) || claims.Any(c=>c==null) ||
                plan.items.Select(i=>i.id).Distinct(StringComparer.Ordinal).Count()!=plan.items.Length ||
                plan.players.Select(p=>p.actor).Distinct(StringComparer.Ordinal).Count()!=plan.players.Length) return false;
            var sources=world.containers.Where(c=>c!=null && c.id==expectedSourceId).ToArray();
            if(sources.Length!=1 || world.pendingGrants.Any(g=>g?.containerId==expectedSourceId))
            {reason="Источник отсутствует/повторён или выдача ещё зарезервирована: source="+expectedSourceId;return false;}
            var source=sources[0];
            if(claims.Select(c=>c.item).Distinct(StringComparer.Ordinal).Count()!=claims.Length ||
                claims.Select(c=>c.transaction).Distinct(StringComparer.Ordinal).Count()!=claims.Length)
            {reason="Повторный ID стопки или транзакции: source="+expectedSourceId;return false;}
            foreach(var claim in claims)
            {
                var item=plan.items.SingleOrDefault(i=>i.id==claim.item);
                if(item==null || claim.definition!=item.definition || claim.count!=item.count || string.IsNullOrEmpty(claim.transaction) ||
                    !world.completedTransactions.Contains(claim.transaction) || !plan.players.Any(p=>p.actor==claim.recipient))
                {reason="Нет точного подтверждения целой стопки: source="+expectedSourceId+", item="+claim.item;return false;}
                var recipients=plan.players.Where(p=>InventoryStacks.Find(world.Find(p.actor),claim.item)!=null).ToArray();
                if(recipients.Length!=1 || recipients[0].actor!=claim.recipient)
                {reason="Стопка не имеет единственного явного получателя: source="+expectedSourceId+", item="+claim.item;return false;}
                var received=InventoryStacks.Find(world.Find(claim.recipient),claim.item);
                if(received.count<claim.count || !InventoryStacks.Compatible(item,received))
                {reason="Полученная стопка изменила количество/определение/расширения: source="+expectedSourceId+", item="+claim.item;return false;}
            }
            if(!SameItems(source.items,plan.items.Where(i=>!claims.Any(c=>c.item==i.id))))
            {reason="Содержимое источника не соответствует удалённым целым стопкам: source="+expectedSourceId;return false;}
            foreach(var player in plan.players)
            {
                var actor=world.Find(player.actor);
                if(actor==null || actor.generation!=player.generation || player.counts?.Length!=plan.items.Length || player.counts.Any(c=>c<0))
                {reason="Изменилась личность/поколение участника: source="+expectedSourceId+", actor="+player.actor;return false;}
                for(int i=0;i<plan.items.Length;i++)
                {
                    var definition=plan.items[i].definition;
                    long expected=player.counts[i]+claims.Where(c=>c.recipient==player.actor && c.definition==definition).Sum(c=>(long)c.count);
                    if(expected>int.MaxValue || InventoryJournal.Count(actor,definition)!=expected)
                    {reason=$"Не совпала подтверждённая сумма: source={expectedSourceId}, actor={player.actor}, definition={definition}, expected={expected}.";return false;}
                }
            }
            reason=null;return true;
        }
        private static bool ValidItem(ItemStack item)=>item!=null && !string.IsNullOrEmpty(item.id) &&
            !string.IsNullOrEmpty(item.definition) && item.count>0;
    }
}
