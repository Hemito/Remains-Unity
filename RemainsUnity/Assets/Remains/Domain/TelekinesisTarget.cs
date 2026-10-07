using Remains.Content;
using UnityEngine;

namespace Remains.Domain
{
    // A read-only view of authoritative state. Actors are never inserted into WorldProps.
    public readonly struct TelekinesisTarget
    {
        public PropState Prop {get;}
        public ActorState Body {get;}
        private readonly ActorTypeDefinition bodyType;
        public TelekinesisTarget(PropState prop) {Prop=prop;Body=null;bodyType=null;}
        public TelekinesisTarget(ActorState body) {Body=body;Prop=null;bodyType=ActorTypes.Get(body.actorType);}
        public string Id=>Prop?.id??Body.id;
        public string Level=>Prop?.level??Body.level;
        public Vector2 Position=>Prop?.Position??Body.Position;
        public Vector2 Center=>Prop?.Center??Body.BodyPosition+Vector2.up*(Anatomy.Movement(Body).height*.5f);
        // Initial hold targets the physical hull. Cursor/LOS use the resolved body above.
        public Vector2 PhysicalCenter=>Prop?.Center??Body.Position+Vector2.up*(Anatomy.Movement(Body).height*.5f);
        public float Mass=>Prop?.mass??bodyType.bodyMassKg;
        public bool Movable=>Prop!=null?Prop.movable && Prop.hasPhysicalBody:bodyType.telekinesisMovable;
        public static TelekinesisTarget? Nearest(WorldState world,ActorState holder,Vector2 cursor)
        {
            TelekinesisTarget? nearest=null;float distance=1;
            void Consider(TelekinesisTarget target)
            {
                if(target.Level!=holder.level) return;
                float candidate=Vector2.Distance(target.Center,cursor);
                if(candidate<distance || candidate==distance && nearest.HasValue &&
                    string.CompareOrdinal(target.Id,nearest.Value.Id)<0)
                {nearest=target;distance=candidate;}
            }
            foreach(var prop in WorldProps.All(world)) Consider(new TelekinesisTarget(prop));
            foreach(var body in world.actors)
                if(body.id!=holder.id && ActorPresence.IsPresent(body)) Consider(new TelekinesisTarget(body));
            return nearest;
        }
    }
    public interface IActorTelekinesisRule
    {
        string Rejection(ActorState holder,TelekinesisTarget target,bool alreadyHeld);
        string SightRejection(ActorState holder,TelekinesisTarget target,RoomLayout room);
        float ManaPerSecond(ActorState holder,TelekinesisTarget target);
    }
}
