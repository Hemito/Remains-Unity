using System;
using Remains.Domain;
using Remains.Gameplay;

namespace Remains.Networking
{
    // Session lifecycle adapter. The curve, deduplication and owner state live in the motion service.
    public sealed class SessionWeaponRecoil
    {
        private readonly CoopSession session;
        private WeaponRecoilMotion motion;
        private string reloadingActor;
        public SessionWeaponRecoil(CoopSession session)
        {
            this.session=session;Configure();
            session.Changed+=Reconcile;
            session.Pause.Changed+=Reconcile;
            session.Projectiles.Rejected+=Reject;
        }
        public void Configure()
        {
            motion=new WeaponRecoilMotion(curveRule:session.Rules.WeaponRecoil);
            reloadingActor=null;
        }
        public void Clear() {motion.Clear();reloadingActor=null;}
        public void BeginReload(ActorState actor) {reloadingActor=actor.id;Reconcile();}
        public void EndReload() {reloadingActor=null;Reconcile();}
        private bool Blocked(ActorState actor)=>actor.id==reloadingActor || actor.npcWeaponAction?.kind==NpcWeaponActions.Reload || ActorHoldRules.Active(actor);
        public void Reconcile()=>Step(0);
        public void Step(float dt)
        {
            if(session.GameplayFailurePending) return;
            try
            {
                motion.Step(session.World,session.LocalOwner,dt,session.Pause.IsPaused,session.Pause.Revision,Blocked);
                if(session.World==null) return;
                foreach(var actor in session.World.actors)
                    if(actor.owner!=session.LocalOwner && (actor.weaponRecoil==null || !actor.weaponRecoil.IsValid ||
                        actor.weaponRecoil.pauseRevision!=session.Pause.Revision ||
                        session.Pause.IsPaused(actor.level) && (actor.weaponRecoil.offsetX!=0 || actor.weaponRecoil.angleDegrees!=0)))
                        WeaponRecoilReplication.Normalize(actor,session.Pause.Revision,session.Pause.IsPaused(actor.level),"world="+session.World.worldId+", actor="+actor.id);
            }
            catch(Exception error) {session.ReportGameplayPreparationFailure(new InvalidOperationException("Ошибка симуляции отдачи; сессия остановлена до новых выстрелов.",error));}
        }
        public void Commit(ActorState actor,AttackEvent shot)
        {
            try {motion.CommitShot(session.World,session.LocalOwner,actor,shot,session.Pause.Revision);}
            catch(Exception error) {session.ReportGameplayPreparationFailure(new InvalidOperationException($"Ошибка принятия отдачи: actor={actor.id}, shot={shot.id}, weapon={shot.weapon}. Уже принятый запуск не откатывается.",error));}
        }
        private void Reject(string id)
        {
            try {motion.Reject(id,session.World,session.LocalOwner,session.Pause.Revision);}
            catch(Exception error) {session.ReportGameplayPreparationFailure(new InvalidOperationException("Ошибка отклонения отдачи: shot="+id,error));}
        }
    }
}
