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
        private readonly ActiveReloadClock clock=new();
        private ActiveReloadSettings settings;
        public bool Active => actorId!=null;
        public float Remaining => clock.Remaining;
        public ActiveReloadClock Clock=>clock;
        public event System.Action<ActorState,string> Started;
        public event System.Action Finished;
        public WeaponReloadController(CoopSession session) => this.session=session;

        public bool Start(ActorState actor,string selected)
        {
            if(Active || string.IsNullOrEmpty(selected) || actor==null || actor.down || session.Pause.IsPaused(actor.level)) return false;
            var definition=WeaponCatalog.Get(selected);
            if(definition.rechargeSeconds>0) {session.ShowNotice(Remains.Content.GameLocalization.UiText("source.WeaponReloadController.dd43af1b085f5c2a", "Оружие подзаряжается автоматически.", "RemainsUnity/Assets/Remains/Gameplay/WeaponReloadController.cs"));return false;}
            if(WeaponAmmunition.ReloadAmount(actor,definition)==0)
            { session.ShowNotice(Remains.Content.GameLocalization.UiText("source.WeaponReloadController.ee3a1557bea6de6a", "Перезарядка не нужна или нет подходящих патронов в запасе.", "RemainsUnity/Assets/Remains/Gameplay/WeaponReloadController.cs"));return false; }
            settings??=ActiveReloadSettings.Load();
            clock.Start(definition.reloadSeconds,settings.windowStart,settings.windowWidth);
            actorId=actor.id;weapon=selected;generation=actor.generation;lifecycle=actor.lifecycleSequence;
            weaponItem=actor.equippedWeaponItem;
            world=session.World?.worldId;level=actor.level;selectionRevision=actor.weaponSelectionRevision;
            session.ShowNotice(Remains.Content.GameLocalization.UiText("source.WeaponReloadController.0902bfd6c5c14765", "Перезарядка…", "RemainsUnity/Assets/Remains/Gameplay/WeaponReloadController.cs"));
            Started?.Invoke(actor,selected);
            return true;
        }

        // R starts a reload or spends its one optional timing attempt. Programmatic Start
        // retains its old meaning and never accidentally attempts an active reload.
        public bool Request(ActorState actor,string selected)
        {
            if(!Active) return Start(actor,selected);
            if(actor?.id!=actorId || !Matches(actor,selected) || session.Pause.IsPaused(actor.level)) return false;
            var attempt=clock.Press();
            if(attempt==ActiveReloadAttempt.Succeeded) Complete(actor);
            else if(attempt==ActiveReloadAttempt.Missed)
            {
                Finished?.Invoke();Started?.Invoke(actor,weapon);
                session.ShowNotice(Remains.Content.GameLocalization.UiText("source.WeaponReloadController.4baa954dc8252fef", "Промах: перезарядка начата заново.", "RemainsUnity/Assets/Remains/Gameplay/WeaponReloadController.cs"));
            }
            return attempt!=ActiveReloadAttempt.Ignored;
        }

        private bool Matches(ActorState actor,string selected)=>actor!=null && !actor.down && actor.owner==session.LocalOwner && selected==weapon &&
            session.World?.worldId==world && actor.level==level && actor.weaponSelectionRevision==selectionRevision &&
            actor.generation==generation && actor.lifecycleSequence==lifecycle &&
            (actor.weaponInventoryVersion==0 || actor.equippedWeaponItem==weaponItem && WeaponInventory.Available(actor,weapon));

        public void Step(string selected,float dt)
        {
            if(!Active) return;
            var actor=session.World?.Find(actorId);
            if(!Matches(actor,selected))
            {actorId=null;clock.Cancel();Finished?.Invoke();session.ShowNotice(Remains.Content.GameLocalization.UiText("source.WeaponReloadController.20d7e8398482d1d8", "Перезарядка прервана.", "RemainsUnity/Assets/Remains/Gameplay/WeaponReloadController.cs"));return;}
            if(session.Pause.IsPaused(actor.level)) return;
            clock.Advance(dt);
            if(clock.Running) return;
            Complete(actor);
        }
        private void Complete(ActorState actor)
        {
            actorId=null;
            Finished?.Invoke();
            if(WeaponAmmunition.CompleteReload(actor,WeaponCatalog.Get(weapon)))
            {session.PublishActor(actor);session.ShowNotice(Remains.Content.GameLocalization.UiText("source.WeaponReloadController.16b77d5305c86072", "Оружие перезаряжено.", "RemainsUnity/Assets/Remains/Gameplay/WeaponReloadController.cs"));}
            else session.ShowNotice(Remains.Content.GameLocalization.UiText("source.WeaponReloadController.fd3b5c6d17c20546", "Перезарядка не выполнена: запас патронов изменился.", "RemainsUnity/Assets/Remains/Gameplay/WeaponReloadController.cs"));
        }
    }
}
