using Remains.Networking;
using UnityEngine;

namespace Remains.Gameplay
{
    public sealed class RevivalController
    {
        private readonly CoopSession session;
        private string targetId;
        private float elapsed;
        private float startingHealth;
        public float Progress => elapsed/1.5f;
        public RevivalController(CoopSession session) => this.session=session;
        public void Step(bool held,float dt)
        {
            var healer=session.LocalActor;
            var target=session.World?.actors.Find(a=>a.player && a.connected && a.id!=healer?.id && a.down && a.level==healer.level && Vector2.Distance(a.Position,healer.Position)<2);
            if(!held || healer==null || healer.down || target==null || (targetId!=null && healer.health<startingHealth))
            { targetId=null; elapsed=0; return; }
            if(targetId!=target.id) { targetId=target.id; elapsed=0; startingHealth=healer.health; }
            elapsed+=dt;
            if(elapsed>=1.5f) { session.RequestRevive(target.id); elapsed=0; }
        }
    }
}
