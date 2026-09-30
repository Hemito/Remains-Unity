using System.Collections.Generic;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;

namespace Remains.Gameplay
{
    public sealed class GrenadeFlight
    {
        public GrenadeMessage message;
        public Vector2 position,velocity;
        public float remaining;
    }
    public sealed class GrenadeController
    {
        public IReadOnlyList<GrenadeFlight> Flights=>flights;
        private readonly List<GrenadeFlight> flights=new();
        private readonly CoopSession session;
        private readonly CombatController combat;
        private readonly Dictionary<string,Vector2> observedExplosions=new();
        public GrenadeController(CoopSession session,InteractionAdapter adapter,CombatController combat)
        {
            this.session=session;this.combat=combat;
            session.Changed+=()=> {if(session.World==null) {flights.Clear();observedExplosions.Clear();}};
            combat.LocalExplosionPosition=id=>
            {
                if(observedExplosions.TryGetValue(id,out var position)) return position;
                var active=flights.Find(f=>f.message.id==id);
                return active==null?(Vector2?)null:active.position;
            };
            adapter.GrenadeReceived+=message=>flights.Add(new GrenadeFlight { message=message,
                position=new Vector2(message.x,message.y),velocity=new Vector2(message.vx,message.vy),remaining=message.fuse });
        }
        public void Step(float dt)
        {
            for(int i=flights.Count-1;i>=0;i--)
            {
                var flight=flights[i];
                flight.velocity+=Vector2.down*MovementController.Gravity*dt;
                flight.position+=flight.velocity*dt;
                if(flight.position.y<0.1f) { flight.position.y=0.1f;flight.velocity.y=Mathf.Abs(flight.velocity.y)*0.45f;flight.velocity.x*=0.75f; }
                flight.remaining-=dt;
                if(flight.remaining>0) continue;
                observedExplosions[flight.message.id]=flight.position;
                var attacker=session.World?.Find(flight.message.attacker);
                if(attacker?.owner==session.LocalOwner) combat.Explode(attacker,flight.position,flight.message.id);
                flights.RemoveAt(i);
            }
        }
    }
}
