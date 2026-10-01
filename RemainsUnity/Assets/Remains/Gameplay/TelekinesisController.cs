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
        private float nextPose;
        private float nextStats;
        public TelekinesisController(CoopSession session,InteractionAdapter adapter,SimulationGeometry geometry) { this.session=session;this.adapter=adapter;this.geometry=geometry; }
        public void Step(bool held,Vector2 aim,float dt)
        {
            var actor=session.LocalActor;
            if(actor==null) return;
            if(session.Pause.IsPaused(actor.level)) return;
            held &= !actor.down && actor.mana>0;
            if(held && !wasHeld)
            {
                var nearest=WorldProps.All(session.World).Where(p=>p.movable && p.level==actor.level && Vector2.Distance(p.Position,aim)<1 && Vector2.Distance(p.Position,actor.Position)<5).OrderBy(p=>Vector2.Distance(p.Position,aim)).FirstOrDefault();
                selected=nearest?.id;
                if(selected!=null) adapter.GrabProp(selected,false);
            }
            if(!held && wasHeld && selected!=null)
            {
                adapter.GrabProp(selected,true);selected=null;
                actor.revision++;session.PublishActor(actor);
            }
            wasHeld=held;
            foreach(var prop in WorldProps.All(session.World))
            {
                if(prop.owner!=session.LocalOwner || prop.level!=actor.level) continue;
                if(prop.held && prop.id==selected && held)
                {
                    Vector2 target=actor.Position+Vector2.ClampMagnitude(aim-actor.Position,5);
                    PropMotion.Step(prop,geometry.Get(prop.level),dt,target);
                    actor.mana=Mathf.Max(0,actor.mana-prop.mass*0.3f*dt);
                    actor.revision++;
                }
                else
                {
                    PropMotion.Step(prop,geometry.Get(prop.level),dt,null);
                }
            }
            if(Time.unscaledTime<nextPose) return;
            nextPose=Time.unscaledTime+0.05f;
            foreach(var prop in WorldProps.All(session.World)) if(prop.owner==session.LocalOwner && prop.level==actor.level) adapter.SendProp(prop);
            if(held && selected!=null && Time.unscaledTime>=nextStats)
            { nextStats=Time.unscaledTime+0.5f;session.PublishActor(actor); }
        }
    }
}
