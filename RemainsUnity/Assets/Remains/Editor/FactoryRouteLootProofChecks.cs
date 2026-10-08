using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Remains.Domain;
using Remains.Runtime;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryRouteLootProofChecks
    {
        private const string Cup="factory:0:original:plant:27:BNhmeNonzjYCRmpA";
        private const string Ammo="factory:0:original:plant:40:bqLt39CNwjwI8aUX";
        private static int assertions;
        private sealed class Fixture
        {
            public WorldState world;
            public WarehouseLowerLootEvidence plan;
            public readonly List<WarehouseLowerClaim> claims=new();
            public ActorState Recipient=>world.Find("route-player:A");
            public ContainerState Source=>world.containers.Single(c=>c.id==plan.source);
        }
        public static void Run()
        {
            assertions=0;
            var cup=Create(Cup,("caps",4),("pin",1),("kombu_comp",1));
            Confirm(cup.world,cup.plan,cup.claims.ToArray(),"Исходный другой источник без выдач");
            foreach(var item in cup.plan.items) Commit(cup,item);
            Confirm(cup.world,cup.plan,cup.claims.ToArray(),"Три реальные целые выдачи из cup28");
            Require(InventoryStacks.Find(cup.Recipient,cup.plan.items[0].id).id=="prior:caps" &&
                InventoryJournal.Count(cup.Recipient,"caps")==11,"Объединение не создало настоящий alias исходной стопки");
            var copied=StateJson.Copy(cup.world);var copiedPlan=StateJson.Copy(cup.plan);
            Confirm(copied,copiedPlan,cup.claims.ToArray(),"Сериализация мира и полных расширений");
            CheckReservation(cup);
            CheckRejections(cup);
            CheckPartialGrant();
            CheckLegacy();
            Debug.Log($"FactoryRouteLootProofChecks PASS: {assertions} проверок; реальные SharedInventory whole/partial grants, alias/mod extensions, source/pending/recipient/epoch/journal и строгий legacy ammo25.");
        }
        private static Fixture Create(string sourceId,params (string definition,int count)[] items)
        {
            var world=new WorldState();
            var a=new ActorState {id="route-player:A",character="route-character:A",owner="A",generation=7,player=true,connected=true,x=168,y=7.2f};
            var b=new ActorState {id="route-player:B",character="route-character:B",owner="B",generation=12,player=true,connected=true,x=168,y=7.2f};
            world.actors.Add(a);world.actors.Add(b);
            var source=new ContainerState {id=sourceId,definition="cup",x=168,y=7.2f};world.containers.Add(source);
            for(int i=0;i<items.Length;i++) source.items.Add(new ItemStack {id="source:"+sourceId+":"+i,
                definition=items[i].definition,count=items[i].count,extensions=new Dictionary<string,JToken>
                {{"route-mod",new JObject { ["quality"]=3,["data"]=new JArray("kept",i) }}}});
            var prior=source.items[0].Copy();prior.id="prior:"+prior.definition;prior.count=7;InventoryStacks.Add(a,prior);
            var plan=new WarehouseLowerLootEvidence {source=sourceId,recipient=a.id,items=source.items.Select(i=>i.Copy()).ToArray()};
            plan.players=world.actors.Select(actor=>new WarehouseLowerBaseline {actor=actor.id,generation=actor.generation,
                counts=plan.items.Select(item=>InventoryJournal.Count(actor,item.definition)).ToArray()}).ToArray();
            return new Fixture {world=world,plan=plan};
        }
        private static void Commit(Fixture fixture,ItemStack expected)
        {
            var shared=new SharedInventory(fixture.world);string transaction="claim:"+expected.id;
            Require(shared.PrepareClaim(transaction,fixture.plan.source,expected.id,fixture.Recipient,out var reason),
                "Настоящий whole-stack prepare отклонён: "+reason);
            var pending=fixture.world.pendingGrants.Single();
            var claim=new WarehouseLowerClaim {transaction=transaction,item=expected.id,definition=expected.definition,
                count=expected.count,recipient=fixture.Recipient.id};
            Require(FactoryRouteLootProof.MatchesReservation(pending,fixture.plan.source,claim,expected),
                "Реальный резерв не связан с полным исходным предметом");
            Reject(fixture.world,fixture.plan,fixture.claims.ToArray(),"Pending ещё не является выдачей");
            shared.CommitGrant(pending);int count=InventoryJournal.Count(fixture.Recipient,expected.definition);
            shared.CommitGrant(pending);
            Require(InventoryJournal.Count(fixture.Recipient,expected.definition)==count,"Повтор CommitGrant добавил количество");
            fixture.claims.Add(claim);
            Confirm(fixture.world,fixture.plan,fixture.claims.ToArray(),"Реальный подтверждённый префикс целых стопок");
        }
        private static void CheckReservation(Fixture fixture)
        {
            var expected=fixture.plan.items[0];var claim=fixture.claims[0];
            var pending=new PendingGrant {containerId=fixture.plan.source,recipient=claim.recipient,transaction=claim.transaction,item=expected.Copy()};
            Require(FactoryRouteLootProof.MatchesReservation(pending,fixture.plan.source,claim),"Legacy structural reservation binding");
            Require(!FactoryRouteLootProof.MatchesReservation(pending,fixture.plan.source,claim,null),"Strong reservation proof принял отсутствующую исходную стопку");
            Require(!FactoryRouteLootProof.MatchesReservation(pending,"other-source",claim,expected),"Резерв другого источника принят");
            foreach(string change in new[]{"transaction","recipient","item","definition","partial","extensions"})
            {
                var bad=StateJson.Copy(pending);
                switch(change)
                {
                    case "transaction":bad.transaction="wrong";break;
                    case "recipient":bad.recipient="route-player:B";break;
                    case "item":bad.item.id="wrong";break;
                    case "definition":bad.item.definition="wrong";break;
                    case "partial":bad.item.count--;break;
                    case "extensions":((JObject)bad.item.extensions["route-mod"])["quality"]=4;break;
                }
                Require(!FactoryRouteLootProof.MatchesReservation(bad,fixture.plan.source,claim,expected),"Неверный резерв принят: "+change);
            }
            Require(FactoryRouteLootProof.SameItems(fixture.plan.items,fixture.plan.items.Reverse()),"Порядок не должен менять равенство стопок");
            var altered=fixture.plan.items.Select(i=>i.Copy()).ToArray();altered[0].extensions["new-mod-field"]=true;
            Require(!FactoryRouteLootProof.SameItems(fixture.plan.items,altered),"Неизвестное поле мода пропущено");
        }
        private static void CheckRejections(Fixture fixture)
        {
            var claims=fixture.claims.ToArray();
            Reject(fixture.world,fixture.plan,claims,"Неверный expected source","factory:6:other");
            var plan=StateJson.Copy(fixture.plan);plan.source="other-source";
            Reject(fixture.world,plan,claims,"Неверный source в плане",fixture.plan.source);
            foreach(string change in new[]{"missing-source","duplicate-source","refill","pending","missing-completed","partial-received",
                "duplicate-recipient","generation","missing-player","extensions","journal-debit","availability-reservation"})
            {
                var world=StateJson.Copy(fixture.world);var actor=world.Find(fixture.Recipient.id);
                var received=InventoryStacks.Find(actor,claims[0].item);
                switch(change)
                {
                    case "missing-source":world.containers.Clear();break;
                    case "duplicate-source":world.containers.Add(StateJson.Copy(fixture.Source));break;
                    case "refill":world.containers.Single().items.Add(fixture.plan.items[0].Copy());break;
                    case "pending":world.pendingGrants.Add(new PendingGrant {containerId=fixture.plan.source});break;
                    case "missing-completed":world.completedTransactions.Remove(claims[0].transaction);break;
                    case "partial-received":received.count=1;break;
                    case "duplicate-recipient":InventoryStacks.Add(world.Find("route-player:B"),fixture.plan.items[0].Copy());break;
                    case "generation":actor.generation++;break;
                    case "missing-player":world.actors.Remove(actor);break;
                    case "extensions":((JObject)received.extensions["route-mod"])["quality"]=9;break;
                    case "journal-debit":Require(InventoryJournal.Debit(actor,"later-use",received.id,1),"Настоящий debit для отрицательной проверки");break;
                    case "availability-reservation":actor.inventoryReservations[received.id]=1;break;
                }
                Reject(world,fixture.plan,claims,change);
            }
            foreach(string change in new[]{"partial-claim","wrong-recipient","wrong-definition","duplicate-item","duplicate-transaction"})
            {
                var bad=claims.Select(c=>StateJson.Copy(c)).ToArray();
                switch(change)
                {
                    case "partial-claim":bad[0].count--;break;
                    case "wrong-recipient":bad[0].recipient="route-player:B";break;
                    case "wrong-definition":bad[0].definition="heal";break;
                    case "duplicate-item":bad=bad.Concat(new[]{StateJson.Copy(bad[0])}).ToArray();break;
                    case "duplicate-transaction":bad[1].transaction=bad[0].transaction;break;
                }
                Reject(fixture.world,fixture.plan,bad,change);
            }
            plan=StateJson.Copy(fixture.plan);plan.players[0].counts[0]++;
            Reject(fixture.world,plan,claims,"Неверная baseline сумма");
            var unrelated=StateJson.Copy(fixture.world);unrelated.pendingGrants.Add(new PendingGrant {containerId="other-source"});
            Confirm(unrelated,fixture.plan,claims,"Другой source pending не блокирует выбранный источник");
        }
        private static void CheckPartialGrant()
        {
            var fixture=Create("route:another-source",("caps",4));var shared=new SharedInventory(fixture.world);
            Require(shared.PrepareClaim("actual-partial",fixture.plan.source,fixture.plan.items[0].id,fixture.Recipient,out var reason,2),
                "Настоящая partial операция для отрицательного whole proof отклонена: "+reason);
            var pending=fixture.world.pendingGrants.Single();var claim=new WarehouseLowerClaim {transaction=pending.transaction,
                recipient=pending.recipient,item=pending.item.id,definition=pending.item.definition,count=pending.item.count};
            Require(FactoryRouteLootProof.MatchesReservation(pending,fixture.plan.source,claim),"Partial receipt structural binding должен оставаться точным");
            Require(!FactoryRouteLootProof.MatchesReservation(pending,fixture.plan.source,claim,fixture.plan.items[0]),"Partial ошибочно связан с целой исходной стопкой");
            shared.CommitGrant(pending);
            Reject(fixture.world,fixture.plan,new[]{claim},"Реальная partial выдача не должна проходить whole-stack proof");
        }
        private static void CheckLegacy()
        {
            // Access the unchanged internal wrapper rather than duplicating its gate in this check.
            var method=typeof(FactoryRouteLootProof).Assembly.GetType("Remains.Runtime.WarehouseLowerLootProof",true)
                .GetMethod("Confirms",BindingFlags.Static|BindingFlags.NonPublic);
            Require(method!=null,"Не найден legacy wrapper");
            var ammo=Create(Ammo,("p10",12),("batt",18));foreach(var item in ammo.plan.items) Commit(ammo,item);
            bool Call(Fixture fixture)=> (bool)method.Invoke(null,new object[]{fixture.world,fixture.plan,fixture.claims.ToArray(),null});
            Require(Call(ammo),"Ammo25 p10/batt перестал подтверждаться legacy wrapper");
            var other=Create("other-ammo",("p10",12),("batt",18));foreach(var item in other.plan.items) Commit(other,item);
            Require(!Call(other),"Legacy wrapper принял другой источник с тем же лутом");
            var changed=Create(Ammo,("caps",4),("batt",18));foreach(var item in changed.plan.items) Commit(changed,item);
            Require(!Call(changed),"Legacy wrapper потерял строгий p10/batt контракт");
        }
        private static void Confirm(WorldState world,WarehouseLowerLootEvidence plan,WarehouseLowerClaim[] claims,string context)
        {
            string before=StateJson.ToJson(world);
            Require(FactoryRouteLootProof.Confirms(world,plan.source,plan,claims,out var reason),context+": "+reason);
            Require(StateJson.ToJson(world)==before,"Proof изменил реальный world: "+context);
        }
        private static void Reject(WorldState world,WarehouseLowerLootEvidence plan,WarehouseLowerClaim[] claims,string context,string expected=null)
        {
            string before=StateJson.ToJson(world);
            Require(!FactoryRouteLootProof.Confirms(world,expected??plan.source,plan,claims,out var reason) && !string.IsNullOrEmpty(reason),
                "Негативный случай не отклонён с причиной: "+context);
            Require(StateJson.ToJson(world)==before,"Отказ proof изменил world: "+context);
        }
        private static void Require(bool valid,string reason)
        {assertions++;if(!valid) throw new InvalidOperationException("FactoryRouteLootProofChecks: "+reason);}
    }
}
