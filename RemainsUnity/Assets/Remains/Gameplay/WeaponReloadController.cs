using Remains.Content;
using Remains.Domain;
using Remains.Networking;

namespace Remains.Gameplay
{
    public sealed class WeaponReloadController
    {
        private readonly CoopSession session;
        private string actorId,weapon;
        private int generation,lifecycle;
        private float remaining;
        public bool Active => actorId!=null;
        public float Remaining => remaining;
        public WeaponReloadController(CoopSession session) => this.session=session;

        public bool Start(ActorState actor,string selected)
        {
            if(Active || actor==null || actor.down || session.Pause.IsPaused(actor.level)) return false;
            var definition=WeaponCatalog.Get(selected);
            if(WeaponAmmunition.ReloadAmount(actor,definition)==0)
            { session.ShowNotice("Перезарядка не нужна или нет подходящих патронов в запасе.");return false; }
            actorId=actor.id;weapon=selected;generation=actor.generation;lifecycle=actor.lifecycleSequence;
            remaining=definition.reloadSeconds;
            session.ShowNotice("Перезарядка…");
            return true;
        }

        public void Step(string selected,float dt)
        {
            if(!Active) return;
            var actor=session.World?.Find(actorId);
            if(actor==null || actor.down || actor.owner!=session.LocalOwner || selected!=weapon ||
                actor.generation!=generation || actor.lifecycleSequence!=lifecycle)
            {actorId=null;remaining=0;session.ShowNotice("Перезарядка прервана.");return;}
            if(session.Pause.IsPaused(actor.level)) return;
            remaining-=dt;
            if(remaining>0) return;
            actorId=null;remaining=0;
            if(WeaponAmmunition.CompleteReload(actor,WeaponCatalog.Get(weapon)))
            {session.PublishActor(actor);session.ShowNotice("Оружие перезаряжено.");}
            else session.ShowNotice("Перезарядка не выполнена: запас патронов изменился.");
        }
    }
}
