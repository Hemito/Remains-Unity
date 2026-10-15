using Remains.Content;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;

namespace Remains.Gameplay
{
    // A lying mine held in telekinesis (original: a seen mine is levitPoss). The holding client moves it towards the
    // telekinesis target through the level geometry and relays poses; the host owns the state and, as the original,
    // may set an armed held mine off (SessionMines). Releasing drops it straight onto the support below.
    public sealed class MineTelekinesis
    {
        private const float Speed=10,ConfirmSeconds=1;
        private readonly CoopSession session;
        private readonly SimulationGeometry geometry;
        private readonly ReplicationCadence poses=new(.05);
        private readonly MovementHull hull=new();
        private string held;
        private Vector2 position;
        private float waiting;
        public bool Active=>held!=null;
        public MineTelekinesis(CoopSession session,SimulationGeometry geometry) {this.session=session;this.geometry=geometry;}

        // A free, seen mine of the holder's level whose body centre is nearer the cursor than `nearest`.
        public MineState Candidate(ActorState actor,Vector2 cursor,float nearest)
        {
            MineState best=null;
            foreach(var mine in session.World.mines)
            {
                if(mine.level!=actor.level || mine.holder!=null) continue;
                var profile=MineRules.Profile(mine);
                if(!MineRules.Visible(mine,actor,profile)) continue;
                float distance=Vector2.Distance(MineRules.Bounds(mine,profile).center,cursor);
                if(distance<nearest) {nearest=distance;best=mine;}
            }
            return best;
        }

        public void Begin(ActorState actor,MineState mine)
        {
            var rules=session.Rules.Telekinesis;var body=MineRules.TelekinesisBody(mine,MineRules.Profile(mine));
            string rejection=rules.Rejection(actor,body,false);
            if(rejection==null && rules is ITelekinesisSightRule sight) rejection=sight.SightRejection(actor,body,geometry.Get(actor.level));
            if(rejection!=null) {session.ShowNotice(rejection);return;}
            held=mine.id;position=mine.Position;waiting=0;
            session.Mines.RequestGrab(mine,false);
        }

        // Returns the mana spent this step.
        public float Step(ActorState actor,bool key,Vector2 aim,float dt)
        {
            if(held==null) return 0;
            var mine=session.World?.mines.Find(m=>m.id==held);
            if(mine==null || mine.level!=actor.level) {held=null;return 0;}   // exploded, disarmed or left behind
            if(mine.holder!=actor.id)
            {
                // Waiting for the host to confirm the grab; another holder or no answer ends the attempt.
                waiting+=dt;
                if(!key || mine.holder!=null || waiting>ConfirmSeconds) {if(mine.holder==null && key) session.ShowNotice("Захват мины не подтверждён.");held=null;}
                return 0;
            }
            var profile=MineRules.Profile(mine);var room=geometry.Get(mine.level);
            var body=MineRules.TelekinesisBody(mine,profile);body.x=position.x;body.y=position.y;
            string rejection=session.Rules.Telekinesis.Rejection(actor,body,true);
            if(!key || rejection!=null)
            {
                if(rejection!=null) session.ShowNotice(rejection);
                Release(mine,profile,room);return 0;
            }
            hull.width=profile.width;hull.height=profile.height;
            var target=session.Rules.Telekinesis.Target(actor,aim)-Vector2.up*(profile.height*.5f);
            position=ActorTerrainMotion.Move(room,hull,position,Vector2.ClampMagnitude(target-position,Speed*dt),false,bodyTops:false);
            if(poses.TryAdvance(Time.unscaledTimeAsDouble)) session.Mines.SendPose(mine,position);
            return session.Rules.Telekinesis.ManaPerSecond(actor,body)*dt;
        }

        private void Release(MineState mine,MineProfile profile,RoomLayout room)
        {
            held=null;
            session.Mines.SendPose(mine,position);
            if(!MineRules.TryDrop(room,position,profile,out var rest)) rest=position;
            session.Mines.RequestGrab(mine,true,rest,Mathf.Max(0,position.y-rest.y));
        }
    }
}
