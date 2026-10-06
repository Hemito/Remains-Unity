using Remains.Domain;
using Remains.Networking;
using UnityEngine;

namespace Remains.Gameplay
{
    public sealed class RevivalController
    {
        private readonly CoopSession session;
        private readonly RevivalProgress progress=new();
        private string pending;
        private float deadline;
        public float Progress=>progress.Progress;
        public bool Pending=>pending!=null;
        public RevivalController(CoopSession session)
        {
            this.session=session;
            session.HealingFinished+=(transaction,success)=>{if(pending==transaction) {pending=null;progress.Reset();}};
            session.Changed+=()=>{if(session.LocalActor==null) {pending=null;progress.Reset();}};
        }
        public void Step(bool held,float dt)
        {
            // Pointerless compatibility for direct simulation tools. Live input
            // supplies the same explicit target as the hint and the E action.
            var healer=session.LocalActor;
            var target=session.World?.actors.Find(a=>HealingRules.CanApply(healer,a,false));
            StepTarget(target,held,dt);
        }
        public void Step(InteractionTarget selection,bool held,float dt)
        {
            var target=selection.kind==InteractionKind.Revival?session.World?.Find(selection.id):null;
            StepTarget(target,held,dt);
        }
        private void StepTarget(ActorState target,bool held,float dt)
        {
            if(pending!=null)
            {
                if(Time.unscaledTime>deadline) {pending=null;progress.Reset();session.ShowNotice("Нет подтверждения лечения. Проверьте соединение.");}
                return;
            }
            var healer=session.LocalActor;
            if(healer!=null && session.Pause.IsPaused(healer.level)) return;
            if(progress.Step(healer,target,held,dt))
            {pending=session.RequestRevive(target.id);deadline=Time.unscaledTime+10;}
        }
    }
}
