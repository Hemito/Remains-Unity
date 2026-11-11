using Remains.Content;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;

namespace Remains.Gameplay
{
    public sealed class GrenadeTelekinesis
    {
        private readonly CoopSession session;
        private readonly InteractionAdapter adapter;
        private readonly SimulationGeometry geometry;
        private readonly GrenadeController flights;
        private PropState body;
        private string selected;
        private int requestedGeneration;
        private float waiting;

        public GrenadeTelekinesis(CoopSession session,InteractionAdapter adapter,SimulationGeometry geometry,GrenadeController flights)
        {this.session=session;this.adapter=adapter;this.geometry=geometry;this.flights=flights;}

        public GrenadeState Candidate(ActorState actor,Vector2 cursor,float nearest)
        {
            GrenadeState best=null;
            foreach(var flight in flights.Flights)
            {
                var grenade=flight.state;
                if(grenade.level!=actor.level || grenade.holder!=null || grenade.remaining<=0) continue;
                float distance=Vector2.Distance(grenade.Position,cursor);
                if(distance<nearest) {nearest=distance;best=grenade;}
            }
            return best;
        }

        public void Begin(ActorState actor,GrenadeState grenade)
        {
            body=GrenadeRules.TelekinesisBody(grenade,body);
            var rule=session.Rules.Telekinesis;
            string rejection=rule.Rejection(actor,body,false);
            if(rejection==null && rule is ITelekinesisSightRule sight)
                rejection=sight.SightRejection(actor,body,geometry.Get(actor.level));
            if(rejection!=null) {session.ShowNotice(rejection);return;}
            selected=grenade.id;requestedGeneration=grenade.generation;waiting=0;
            adapter.RequestGrenadeHold(selected,requestedGeneration,false);
        }

        public float Step(ActorState actor,bool key,Vector2 aim,float dt,out bool holding)
        {
            holding=false;
            if(selected==null) return 0;
            var grenade=flights.Find(selected)?.state;
            if(grenade==null || grenade.level!=actor.level || grenade.remaining<=0) {selected=null;return 0;}
            if(grenade.holder!=actor.id || grenade.owner!=actor.owner)
            {
                if(grenade.holder!=null || grenade.generation!=requestedGeneration) {selected=null;return 0;}
                waiting+=dt;
                if(!key || waiting>=1)
                {
                    // Release also covers a successful capture whose reply is still in transit.
                    adapter.RequestGrenadeHold(selected,requestedGeneration+1,true);
                    if(key) session.ShowNotice(Remains.Content.GameLocalization.UiText("source.GrenadeTelekinesis.6170ab3347e77d13", "Захват гранаты не подтверждён.", "RemainsUnity/Assets/Remains/Gameplay/GrenadeTelekinesis.cs"));
                    selected=null;
                }
                return 0;
            }
            body=GrenadeRules.TelekinesisBody(grenade,body);
            var rule=session.Rules.Telekinesis;
            string rejection=rule.Rejection(actor,body,true);
            if(!key || rejection!=null)
            {
                if(rejection!=null) session.ShowNotice(rejection);
                grenade.angularVelocity=GrenadeLaunch.AngularVelocity(grenade.Velocity,actor.facing,WeaponCatalog.Get(grenade.weapon));
                adapter.SendGrenade(grenade,false);
                adapter.RequestGrenadeHold(selected,grenade.generation,true);
                selected=null;
                return 0;
            }
            GrenadeRules.MoveHeld(grenade,geometry.Get(grenade.level),rule.Target(actor,aim),dt);
            holding=true;
            return rule.ManaPerSecond(actor,body)*dt;
        }
    }
}
