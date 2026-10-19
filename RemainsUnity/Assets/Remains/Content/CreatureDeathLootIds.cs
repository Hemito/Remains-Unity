using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Remains.Domain;

namespace Remains.Content
{
    // Reserved namespace: only CreatureDeathLoot creates these IDs. Ordinary inventory transfers may move them.
    // Explicitly rebuilt on world loading/replacement; publication appends reservations that outlive pickup/pruning.
    public static class CreatureDeathLootIds
    {
        public const string ItemPrefix="npc-death-item:",DropPrefix="npc-death-drop:";
        private sealed class Index {public readonly HashSet<string> ids=new(StringComparer.Ordinal);public readonly HashSet<string> receipts=new(StringComparer.Ordinal);}
        private static readonly ConditionalWeakTable<WorldState,Index> indexes=new();
        public static bool Reserved(string id)=>id!=null && (id.StartsWith(ItemPrefix,StringComparison.Ordinal)||id.StartsWith(DropPrefix,StringComparison.Ordinal));
        public static void PrepareWorld(WorldState world)
        {
            if(world==null)throw new ArgumentNullException(nameof(world));
            var index=new Index();
            void Add(string id) {if(Reserved(id))index.ids.Add(id);}
            var actual=new Dictionary<string,string>(StringComparer.Ordinal);
            void Actual(string id,string context)
            {
                if(!Reserved(id))return;
                if(actual.TryGetValue(id,out string previous))throw new InvalidOperationException($"Повтор реального ID добычи NPC: world={world.worldId}, id={id}, first={previous}, second={context}.");
                actual.Add(id,context);Add(id);
            }
            foreach(var actor in world.actors) {Actual(actor.id,"actor:"+actor.id);foreach(var item in actor.inventory)Actual(item.id,"actor-item:"+actor.id);foreach(var pair in actor.inventoryAliases){Add(pair.Key);Add(pair.Value);}}
            foreach(var body in world.containers) {Actual(body.id,"container:"+body.id);foreach(var item in body.items)Actual(item.id,"container-item:"+body.id);foreach(var pair in body.itemAliases){Add(pair.Key);Add(pair.Value);}}
            foreach(var prop in world.props)Actual(prop.id,"prop");
            foreach(var door in world.doors)Actual(door.id,"door");
            foreach(var hazard in world.hazards)Actual(hazard.id,"hazard");
            foreach(var mine in world.mines)Actual(mine.id,"mine");
            foreach(var platform in world.movingPlatforms)Actual(platform.id,"platform");
            foreach(var button in world.mechanismButtons)Actual(button.id,"button");
            foreach(var grant in world.pendingGrants)if(grant.item!=null)Add(grant.item.id);
            foreach(var deposit in world.pendingDeposits)if(deposit.item!=null)Add(deposit.item.id);
            foreach(var receipt in world.completedTransactions)if(receipt.StartsWith("npc-death-loot:",StringComparison.Ordinal))index.receipts.Add(receipt);
            indexes.Remove(world);indexes.Add(world,index);
        }
        private static Index Require(WorldState world)=>world!=null && indexes.TryGetValue(world,out var index)?index:
            throw new InvalidOperationException("Индекс ID добычи NPC не подготовлен при загрузке мира: "+world?.worldId);
        internal static void Validate(WorldState world,IEnumerable<string> candidates)
        {
            var index=Require(world);var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(string id in candidates)
                if(!Reserved(id)||!seen.Add(id)||index.ids.Contains(id))throw new InvalidOperationException("Конфликт зарезервированного ID добычи NPC: "+id);
        }
        internal static void Reserve(WorldState world,IEnumerable<string> candidates)
        {var index=Require(world);foreach(var id in candidates)index.ids.Add(id);}
        internal static bool Completed(WorldState world,string receipt)=>Require(world).receipts.Contains(receipt);
        internal static void Complete(WorldState world,string receipt)=>Require(world).receipts.Add(receipt);
        public static void RejectForeignCreation(IEnumerable<string> ids,string context)
        {foreach(string id in ids)if(Reserved(id))throw new InvalidOperationException("Сторонний writer использует namespace добычи NPC: id="+id+", context="+context);}
    }
}
