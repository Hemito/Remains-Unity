using System;
using Remains.Domain;

namespace Remains.Runtime
{
    // Partial transactions exercise the protocol directly. The production grid
    // always claims the whole stack and has no hidden quantity controls.
    internal static class InventoryTransactionProbe
    {
        public static void Claim(PrototypeRuntime runtime,int index,int quantity)
        {
            var source=InventorySource.Resolve(runtime.Session.World,runtime.Hud.ShownInventorySourceId);
            if(source==null || index>=source.items.Count) throw new InvalidOperationException("Проверка подбора: нет исходного предмета.");
            runtime.Session.RequestClaim(source.id,source.items[index].id,quantity);
        }
        public static void Deposit(PrototypeRuntime runtime,string definition,int quantity)
        {
            var item=runtime.Session.LocalActor.inventory.Find(i=>i.definition==definition);
            if(item==null || !runtime.Session.Deposits.Request(runtime.Hud.ShownInventorySourceId,item.id,quantity))
                throw new InvalidOperationException("Проверка внесения: запрос не отправлен, definition="+definition);
        }
    }
}
