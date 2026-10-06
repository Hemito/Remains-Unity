using System;

namespace Remains.Domain
{
    public enum ContainerInspectChange {Rejected,Unchanged,Changed}
    [Serializable]
    public sealed class ContainerInspectionContext
    {
        public string world,source,definition,actor,owner,level;
        public int generation,lifecycle;
        public ContainerInspectionContext() {}
        public ContainerInspectionContext(WorldState world,ActorState actor,ContainerState container)
        {
            this.world=world.worldId;this.actor=actor.id;owner=actor.owner;level=actor.level;generation=actor.generation;lifecycle=actor.lifecycleSequence;
            source=container.id;definition=container.definition;
        }
        public bool Matches(WorldState current,ActorState player)=>current!=null && player!=null && world==current.worldId &&
            actor==player.id && owner==player.owner && level==player.level && player.connected &&
            generation==player.generation && lifecycle==player.lifecycleSequence;
    }
    public static class ContainerInspectionRules
    {
        public static ContainerInspectChange Apply(WorldState world,ActorState actor,ContainerInspectionContext request,
            bool paused,ILootRule loot,IDepositRule deposit,out string reason)
        {
            reason="Просмотр отменён: мир или состояние персонажа изменились.";
            if(world?.containers==null || actor==null || request==null || !request.Matches(world,actor) || world.Find(actor.id)!=actor ||
                !actor.player || !actor.connected || actor.down || ActorSessionRules.IsPassive(actor)) return ContainerInspectChange.Rejected;
            reason="Локация на паузе: просмотр контейнера не подтверждён.";
            if(paused) return ContainerInspectChange.Rejected;
            reason="Настоящий контейнер больше недоступен или изменился его тип.";
            if(string.IsNullOrWhiteSpace(request.source) || string.IsNullOrWhiteSpace(request.definition) ||
                request.source.StartsWith(InventorySource.BodyPrefix,StringComparison.Ordinal)) return ContainerInspectChange.Rejected;
            ContainerState container=null;
            foreach(var candidate in world.containers)
                if(candidate?.id==request.source) {if(container!=null) return ContainerInspectChange.Rejected;container=candidate;}
            if(container==null || container.definition!=request.definition || container.presentationItem!=null ||
                container.level!=actor.level || container.appearance==null) return ContainerInspectChange.Rejected;
            try {container.appearance.Validate(container.id);}
            catch(InvalidOperationException error) {reason=error.Message;return ContainerInspectChange.Rejected;}
            reason="Вид контейнера требует явной миграции сохранения: "+container.id;
            if(container.appearance.version!=1) return ContainerInspectChange.Rejected;
            if(!ContainerAccess.Accessible(actor,container,loot,deposit))
            {reason=ContainerAccess.Rejection(actor,container,loot)??"Просмотр контейнера недоступен.";return ContainerInspectChange.Rejected;}
            if(container.appearance.opened) {reason=null;return ContainerInspectChange.Unchanged;}
            reason="Исчерпан/невалиден счётчик вида контейнера или мира.";
            if(world.revision<0 || world.revision==long.MaxValue || container.appearance.revision==long.MaxValue) return ContainerInspectChange.Rejected;
            container.appearance.opened=true;container.appearance.revision++;world.revision++;
            reason=null;return ContainerInspectChange.Changed;
        }
    }
}
