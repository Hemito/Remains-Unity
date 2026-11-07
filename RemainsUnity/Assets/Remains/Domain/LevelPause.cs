using System;
using System.Collections.Generic;

namespace Remains.Domain
{
    public readonly struct LevelPauseContext
    {
        public readonly bool manual,menu,inventory,pauseOnMenu,pauseOnInventory;
        public LevelPauseContext(bool manual,bool menu,bool inventory,bool pauseOnMenu,bool pauseOnInventory)
        {this.manual=manual;this.menu=menu;this.inventory=inventory;this.pauseOnMenu=pauseOnMenu;this.pauseOnInventory=pauseOnInventory;}
    }
    public interface ILevelPauseRule {bool IsPaused(LevelPauseContext context);}
    public sealed class CooperativePauseRule : ILevelPauseRule
    {
        public bool IsPaused(LevelPauseContext context)=>context.manual ||
            context.pauseOnMenu && context.menu || context.pauseOnInventory && context.inventory;
    }

    // Session-only intent state. Never restore open menus or manual pauses from a world save.
    // The network coordinator resolves sender -> actor; callers cannot supply an arbitrary level.
    public sealed class LevelPauseCoordinator
    {
        private readonly struct Intent
        {
            public readonly string level;
            public readonly int generation,lifecycle;
            public readonly bool menu,inventory;
            public Intent(ActorState actor,bool menu,bool inventory)
            {level=actor.level;generation=actor.generation;lifecycle=actor.lifecycleSequence;this.menu=menu;this.inventory=inventory;}
            public bool Matches(ActorState actor)=>actor!=null && actor.player && actor.connected &&
                actor.level==level && actor.generation==generation && actor.lifecycleSequence==lifecycle;
        }
        private readonly Dictionary<string,Intent> intents=new(StringComparer.Ordinal);
        private readonly HashSet<string> manualLevels=new(StringComparer.Ordinal);
        private readonly ILevelPauseRule rule;
        public bool PauseOnMenu {get;set;}
        public bool PauseOnInventory {get;set;}
        public LevelPauseCoordinator(ILevelPauseRule rule)
        {this.rule=rule??throw new ArgumentNullException(nameof(rule));}

        public string SetIntent(ActorState actor,int generation,int lifecycle,bool menu,bool inventory)
        {
            string rejection=Rejection(actor,generation,lifecycle);
            if(rejection!=null) return rejection;
            if(menu || inventory) intents[actor.id]=new Intent(actor,menu,inventory);
            else intents.Remove(actor.id);
            return null;
        }
        public string SetManual(ActorState actor,int generation,int lifecycle,bool paused)
        {
            string rejection=Rejection(actor,generation,lifecycle);
            if(rejection!=null) return rejection;
            if(paused) manualLevels.Add(actor.level);else manualLevels.Remove(actor.level);
            return null;
        }
        public bool IsPaused(WorldState world,string level)=>IsPausedForPlayers(world?.actors,level);
        public bool IsPausedForPlayers(IReadOnlyList<ActorState> players,string level)
        {
            if(players==null || level==null) return false;
            bool occupied=false,menu=false,inventory=false;
            for(int i=0;i<players.Count;i++)
            {
                var actor=players[i];
                if(!actor.player || !actor.connected || actor.level!=level) continue;
                occupied=true;
                if(intents.TryGetValue(actor.id,out var intent) && intent.Matches(actor))
                {menu|=intent.menu;inventory|=intent.inventory;}
            }
            return occupied && rule.IsPaused(new LevelPauseContext(manualLevels.Contains(level),menu,inventory,PauseOnMenu,PauseOnInventory));
        }
        public void Reconcile(WorldState world)
        {
            var expired=new List<string>();
            foreach(var pair in intents) if(!pair.Value.Matches(world?.Find(pair.Key))) expired.Add(pair.Key);
            foreach(string id in expired) intents.Remove(id);
            manualLevels.RemoveWhere(level=>!LevelActivity.HasConnectedPlayer(world,level));
        }
        public void Reset()
        {intents.Clear();manualLevels.Clear();PauseOnMenu=false;PauseOnInventory=false;}
        private static string Rejection(ActorState actor,int generation,int lifecycle)
        {
            if(actor==null || !actor.player || !actor.connected || string.IsNullOrWhiteSpace(actor.id) || string.IsNullOrWhiteSpace(actor.level))
                return "Причину паузы может менять только подключённый игрок своего уровня.";
            if(actor.generation!=generation || actor.lifecycleSequence!=lifecycle)
                return "Состояние персонажа изменилось. Повторите запрос паузы.";
            return null;
        }
    }
}
