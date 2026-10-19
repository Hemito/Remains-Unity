using Remains.Content;
using Remains.Domain;
using Remains.Networking;

namespace Remains.Gameplay
{
    // Only the equipped weapon advances, as Weapon.step in the original. Partial progress is local simulation
    // state; ownership/save reconstruction restarts an interval, never grants offline charges or frame-by-frame revisions.
    public sealed class WeaponRechargeController
    {
        private sealed class Clock {public WeaponDefinition definition;public float remaining;}
        private readonly CoopSession session;
        private readonly System.Collections.Generic.Dictionary<(string actor,int generation,int lifecycle,string item),Clock> clocks=new();
        private string world;
        public WeaponRechargeController(CoopSession session)=>this.session=session;
        public void Step(ActorState actor,float dt,float blockedSeconds)
        {
            if(world!=session.World?.worldId) {clocks.Clear();world=session.World?.worldId;}
            if(actor==null || actor.owner!=session.LocalOwner || actor.down || actor.health<=0 || dt<=0 || session.Pause.IsPaused(actor.level)) return;
            var item=WeaponInventory.Selected(actor);
            if(item?.weaponState==null || !WeaponInventory.Available(actor,item.definition)) return;
            var key=(actor.id,actor.generation,actor.lifecycleSequence,item.id);
            if(!clocks.TryGetValue(key,out var clock))
            {
                var definition=WeaponCatalog.Get(item.definition);
                clock=new Clock {definition=definition,remaining=definition.rechargeSeconds};clocks.Add(key,clock);
            }
            if(WeaponAmmunition.AdvanceRecharge(item.weaponState,clock.definition,ref clock.remaining,System.Math.Max(0,dt-blockedSeconds)))
            {actor.revision++;session.PublishActor(actor);}
        }
    }
    public sealed class WeaponReloadController
    {
        private readonly CoopSession session;
        private string actorId,weapon,weaponItem,world,level;
        private long selectionRevision;
        private int generation,lifecycle;
        private float remaining;
        public bool Active => actorId!=null;
        public float Remaining => remaining;
        public event System.Action<ActorState,string> Started;
        public event System.Action Finished;
        public WeaponReloadController(CoopSession session) => this.session=session;

        public bool Start(ActorState actor,string selected)
        {
            if(Active || string.IsNullOrEmpty(selected) || actor==null || actor.down || session.Pause.IsPaused(actor.level)) return false;
            var definition=WeaponCatalog.Get(selected);
            if(definition.rechargeSeconds>0) {session.ShowNotice("Оружие подзаряжается автоматически.");return false;}
            if(WeaponAmmunition.ReloadAmount(actor,definition)==0)
            { session.ShowNotice("Перезарядка не нужна или нет подходящих патронов в запасе.");return false; }
            actorId=actor.id;weapon=selected;generation=actor.generation;lifecycle=actor.lifecycleSequence;
            weaponItem=actor.equippedWeaponItem;
            world=session.World?.worldId;level=actor.level;selectionRevision=actor.weaponSelectionRevision;
            remaining=definition.reloadSeconds;
            session.ShowNotice("Перезарядка…");
            Started?.Invoke(actor,selected);
            return true;
        }

        public void Step(string selected,float dt)
        {
            if(!Active) return;
            var actor=session.World?.Find(actorId);
            if(actor==null || actor.down || actor.owner!=session.LocalOwner || selected!=weapon ||
                session.World.worldId!=world || actor.level!=level || actor.weaponSelectionRevision!=selectionRevision ||
                actor.generation!=generation || actor.lifecycleSequence!=lifecycle ||
                actor.weaponInventoryVersion!=0 && (actor.equippedWeaponItem!=weaponItem || !WeaponInventory.Available(actor,weapon)))
            {actorId=null;remaining=0;Finished?.Invoke();session.ShowNotice("Перезарядка прервана.");return;}
            if(session.Pause.IsPaused(actor.level)) return;
            remaining-=dt;
            if(remaining>0) return;
            actorId=null;remaining=0;
            Finished?.Invoke();
            if(WeaponAmmunition.CompleteReload(actor,WeaponCatalog.Get(weapon)))
            {session.PublishActor(actor);session.ShowNotice("Оружие перезаряжено.");}
            else session.ShowNotice("Перезарядка не выполнена: запас патронов изменился.");
        }
    }
}
