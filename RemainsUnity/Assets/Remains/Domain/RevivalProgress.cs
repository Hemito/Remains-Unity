namespace Remains.Domain
{
    public sealed class RevivalProgress
    {
        private string healerId,targetId;
        private int healerGeneration,healerLifecycle,targetGeneration,targetLifecycle,damageSequence;
        private float elapsed;
        public float Progress=>elapsed/HealingRules.RevivalSeconds;
        public void Reset() {healerId=null;targetId=null;elapsed=0;}
        public bool Step(ActorState healer,ActorState target,bool held,float dt)
        {
            if(!held || !HealingRules.CanApply(healer,target,false)) {Reset();return false;}
            if(healerId!=null && (healer.id!=healerId || healer.generation!=healerGeneration || healer.lifecycleSequence!=healerLifecycle ||
                target.id!=targetId || target.generation!=targetGeneration || target.lifecycleSequence!=targetLifecycle || healer.damageSequence!=damageSequence))
            {Reset();return false;}
            if(healerId==null)
            {
                healerId=healer.id;targetId=target.id;healerGeneration=healer.generation;healerLifecycle=healer.lifecycleSequence;
                targetGeneration=target.generation;targetLifecycle=target.lifecycleSequence;damageSequence=healer.damageSequence;
            }
            elapsed=UnityEngine.Mathf.Min(HealingRules.RevivalSeconds,elapsed+UnityEngine.Mathf.Max(0,dt));
            return elapsed>=HealingRules.RevivalSeconds;
        }
    }
}
