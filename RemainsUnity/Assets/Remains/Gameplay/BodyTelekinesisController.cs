using Remains.Domain;
using Remains.Networking;
using UnityEngine;

namespace Remains.Gameplay
{
    public sealed class BodyTelekinesisController
    {
        private readonly CoopSession session;
        private readonly SimulationGeometry geometry;
        private string selected,token;
        private bool granted;
        public BodyTelekinesisController(CoopSession session,SimulationGeometry geometry)
        {
            this.session=session;this.geometry=geometry;
            session.ActorHolds.Replied+=reply=>{if(reply.id==token && !reply.accepted) Clear();};
            session.Changed+=()=>{if(session.LocalActor==null) Clear();};
        }
        public string Rejection(ActorState holder,ActorState body,bool alreadyHeld)
        {
            if(session.Rules.Telekinesis is not IActorTelekinesisRule rules)
                return "Текущее правило телекинеза не поддерживает перенос актёров.";
            var target=new TelekinesisTarget(body);
            string reason=rules.Rejection(holder,target,alreadyHeld);
            return reason??(!alreadyHeld?rules.SightRejection(holder,target,geometry.Get(holder.level)):null);
        }
        public void Begin(ActorState body)
        {
            string reason=ActorHoldRules.Rejection(session.World,session.LocalActor,body)??Rejection(session.LocalActor,body,false);
            if(reason!=null) {session.ShowNotice(reason);return;}
            selected=body.id;token=session.ActorHolds.Grab(selected);granted=false;
            if(token==null) Clear();
        }
        public float Step(bool held,Vector2 cursor,float dt,bool publish,out bool holding)
        {
            holding=false;
            if(selected==null) return 0;
            var body=session.World?.Find(selected);var holder=session.LocalActor;
            if(!held || body==null || holder==null)
            {if(body!=null) session.ActorHolds.Grab(selected,token,true);Clear();return 0;}
            if(body.hold==null || body.hold.token!=token || body.hold.holder!=holder.id)
            {if(granted) Clear();return 0;}
            granted=true;
            string reason=Rejection(holder,body,true);
            if(reason!=null)
            {session.ActorHolds.Grab(selected,token,true);Clear();session.ShowNotice(reason);return 0;}
            if(publish) session.ActorHolds.SendTarget(body,session.Rules.Telekinesis.Target(holder,cursor));
            if(body.hold.pendingAttack) return 0;
            holding=true;
            return ((IActorTelekinesisRule)session.Rules.Telekinesis).ManaPerSecond(holder,new TelekinesisTarget(body))*dt;
        }
        private void Clear() {selected=null;token=null;granted=false;}
    }
}
