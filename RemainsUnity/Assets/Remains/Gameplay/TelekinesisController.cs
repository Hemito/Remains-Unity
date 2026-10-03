using System.Linq;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;

namespace Remains.Gameplay
{
    public sealed class TelekinesisController
    {
        private readonly CoopSession session;
        private readonly InteractionAdapter adapter;
        private readonly SimulationGeometry geometry;
        private bool wasHeld;
        private string selected;
        private readonly ReplicationCadence poseCadence=new(.05);
        private float nextStats;
        private readonly MagicRecovery recovery=new();
        private bool statsDirty;
        public TelekinesisController(CoopSession session,InteractionAdapter adapter,SimulationGeometry geometry) { this.session=session;this.adapter=adapter;this.geometry=geometry; }
        public void Step(bool held,Vector2 aim,float dt)
        {
            var actor=session.LocalActor;
            if(actor==null) return;
            if(session.Pause.IsPaused(actor.level)) return;
            var rules=session.Rules.Telekinesis;
            if(held && !wasHeld)
            {
                var nearest=WorldProps.All(session.World).Where(p=>p.level==actor.level && Vector2.Distance(p.Center,aim)<1).OrderBy(p=>Vector2.Distance(p.Center,aim)).FirstOrDefault();
                if(nearest!=null)
                {
                    string rejection=rules.Rejection(actor,nearest,false);
                    if(rejection==null && rules is ITelekinesisSightRule sight)
                        rejection=sight.SightRejection(actor,nearest,geometry.Get(actor.level));
                    if(rejection!=null) session.ShowNotice(rejection);
                    else if(nearest.held && nearest.owner!=actor.owner) session.ShowNotice("Объект уже удерживает другой игрок.");
                    else {selected=nearest.id;adapter.GrabProp(selected,false);}
                }
            }
            if(held && selected!=null)
            {
                var current=WorldProps.Find(session.World,selected);
                string rejection=current==null?"Удерживаемый объект больше не существует.":rules.Rejection(actor,current,true);
                if(rejection!=null)
                {
                    adapter.GrabProp(selected,true);selected=null;
                    session.ShowNotice(rejection);actor.revision++;session.PublishActor(actor);
                }
            }
            if(!held && wasHeld && selected!=null)
            {
                adapter.GrabProp(selected,true);selected=null;
                actor.revision++;session.PublishActor(actor);
            }
            wasHeld=held;
            bool holding=false;
            foreach(var prop in WorldProps.All(session.World))
            {
                if(prop.owner!=session.LocalOwner || prop.level!=actor.level) continue;
                if(prop.held && prop.id==selected && held)
                {
                    holding=true;
                    Vector2 target=rules.Target(actor,aim);
                    PropMotion.Step(prop,geometry.Get(prop.level),dt,target);
                    actor.mana=Mathf.Max(0,actor.mana-rules.ManaPerSecond(actor,prop)*dt);
                    actor.revision++;
                    statsDirty=true;
                }
                else
                {
                    PropMotion.Step(prop,geometry.Get(prop.level),dt,null);
                }
            }
            if(rules is ITelekinesisRecoveryRule recoveryRule &&
                recovery.Step(actor,holding,recoveryRule.RecoveryAcceleration(actor),dt))
            {actor.revision++;statsDirty=true;}
            if(!poseCadence.TryAdvance(Time.unscaledTimeAsDouble)) return;
            foreach(var prop in WorldProps.All(session.World)) if(prop.owner==session.LocalOwner && prop.level==actor.level) adapter.SendProp(prop);
            if(statsDirty && Time.unscaledTime>=nextStats)
            { nextStats=Time.unscaledTime+0.5f;session.PublishActor(actor);statsDirty=false; }
        }
    }
}
