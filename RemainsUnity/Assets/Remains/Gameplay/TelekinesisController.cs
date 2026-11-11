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
        private readonly BodyTelekinesisController bodies;
        private readonly MineTelekinesis mines;
        private readonly GrenadeTelekinesis grenades;
        private readonly MovementHull hull=new();
        private WorldPropView props;
        public TelekinesisController(CoopSession session,InteractionAdapter adapter,SimulationGeometry geometry,GrenadeController flights)
        {
            this.session=session;this.adapter=adapter;this.geometry=geometry;
            bodies=new BodyTelekinesisController(session,geometry);mines=new MineTelekinesis(session,geometry);
            grenades=new GrenadeTelekinesis(session,adapter,geometry,flights);
        }
        public void Step(bool held,Vector2 aim,float dt)
        {
            var actor=session.LocalActor;
            if(actor==null) return;
            if(session.Pause.IsPaused(actor.level)) return;
            var rules=session.Rules.Telekinesis;
            bool publish=poseCadence.TryAdvance(Time.unscaledTimeAsDouble);
            if(held && !wasHeld)
            {
                var target=TelekinesisTarget.Nearest(session.World,actor,aim);
                // A seen mine competes with props and bodies by its distance to the cursor (within the same 1 m).
                float nearestDistance=target.HasValue?Vector2.Distance(target.Value.Center,aim):1;
                var mine=mines.Candidate(actor,aim,nearestDistance);
                if(mine!=null) nearestDistance=Vector2.Distance(MineRules.Bounds(mine,MineRules.Profile(mine)).center,aim);
                var grenade=grenades.Candidate(actor,aim,nearestDistance);
                if(grenade!=null) grenades.Begin(actor,grenade);
                else if(mine!=null) mines.Begin(actor,mine);
                else if(target?.Body!=null) bodies.Begin(target.Value.Body);
                else if(target?.Prop!=null)
                {
                    var nearest=target.Value.Prop;
                    string rejection=rules.Rejection(actor,nearest,false);
                    if(rejection==null && rules is ITelekinesisSightRule sight)
                        rejection=sight.SightRejection(actor,nearest,geometry.Get(actor.level));
                    if(rejection!=null) session.ShowNotice(rejection);
                    else if(nearest.held && nearest.owner!=actor.owner) session.ShowNotice(Remains.Content.GameLocalization.UiText("source.TelekinesisController.f86bc0cc93d8b548", "Объект уже удерживает другой игрок.", "RemainsUnity/Assets/Remains/Gameplay/TelekinesisController.cs"));
                    else {selected=nearest.id;adapter.GrabProp(selected,false);}
                }
            }
            if(held && selected!=null)
            {
                var current=WorldProps.Find(session.World,selected);
                string rejection=current==null?Remains.Content.GameLocalization.UiText("source.TelekinesisController.414ef6f9f34062c4", "Удерживаемый объект больше не существует.", "RemainsUnity/Assets/Remains/Gameplay/TelekinesisController.cs"):rules.Rejection(actor,current,true);
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
            float bodyCost=bodies.Step(held,aim,dt,publish,out bool holding);
            float grenadeCost=grenades.Step(actor,held,aim,dt,out bool holdingGrenade);
            holding|=holdingGrenade;
            if(mines.Active)
            {
                float mineCost=mines.Step(actor,held,aim,dt);
                if(mines.Active) {holding=true;actor.mana=Mathf.Max(0,actor.mana-mineCost);actor.revision++;statsDirty=true;}
            }
            if(holding)
            {actor.mana=Mathf.Max(0,actor.mana-bodyCost-grenadeCost);actor.revision++;statsDirty=true;}
            if(held && selected!=null)
            {
                var prop=WorldProps.Find(session.World,selected);
                if(prop!=null && prop.hasPhysicalBody && prop.movable && prop.owner==session.LocalOwner && prop.level==actor.level && prop.held)
                {
                    holding=true;
                    if(props?.World!=session.World) props=new WorldPropView(session.World);
                    Vector2 target=rules.Target(actor,aim);
                    var previous=prop.Position;
                    PropMotion.Step(prop,geometry.Get(prop.level),dt,target,props,null,hull);
                    adapter.NotifyPropMotion(prop,previous);
                    actor.mana=Mathf.Max(0,actor.mana-rules.ManaPerSecond(actor,prop)*dt);
                    actor.revision++;
                    statsDirty=true;
                    if(publish) adapter.SendProp(prop);
                }
            }
            if(rules is ITelekinesisRecoveryRule recoveryRule &&
                recovery.Step(actor,holding,recoveryRule.RecoveryAcceleration(actor),dt))
            {actor.revision++;statsDirty=true;}
            if(!publish) return;
            if(statsDirty && Time.unscaledTime>=nextStats)
            { nextStats=Time.unscaledTime+0.5f;session.PublishActor(actor);statsDirty=false; }
        }
    }
}
