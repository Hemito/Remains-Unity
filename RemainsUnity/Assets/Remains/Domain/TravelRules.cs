namespace Remains.Domain
{
    public static class TravelRules
    {
        public static string Rejection(ActorState actor, string source, string destination, int generation, int lifecycle, bool destinationPaused=false)
        {
            if (actor == null || !actor.connected) return "Персонаж не подключён к сессии.";
            if (actor.generation != generation || actor.lifecycleSequence != lifecycle || actor.level != source)
                return "Состояние персонажа изменилось. Повторите переход.";
            if (actor.down) return "Персонаж без сознания и не может перейти самостоятельно.";
            if (!(source == "hub" && destination == "factory:0") &&
                !(source == "factory:0" && destination == "hub")) return "Этот маршрут недоступен.";
            if(destinationPaused) return "Уровень остановлен. Дождитесь возобновления перед входом.";
            return null;
        }

        public static void Apply(ActorState actor, string level, RoomLayout destination)
        {
            ActorHoldRules.Release(actor);
            actor.level = level;
            actor.x = destination.spawnX;
            actor.y = destination.spawnY;
            actor.vx = actor.vy = 0;
            actor.attackBusy = false;
            actor.attackRemaining = 0;
            ActorPose.Select(actor, "idle");
            actor.lifecycleSequence++;
            actor.revision++;
        }
    }
}
