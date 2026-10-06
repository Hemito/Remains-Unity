using System;
using UnityEngine;
using Remains.Domain;

namespace Remains.Gameplay
{
    public readonly struct MeleeSegment
    {
        public readonly Vector2 start,end;
        public readonly Vector2 anchorStart,anchorEnd;
        public readonly float speed;
        public MeleeSegment(Vector2 start,Vector2 end,Vector2 anchorStart,Vector2 anchorEnd,float seconds)
        {this.start=start;this.end=end;this.anchorStart=anchorStart;this.anchorEnd=anchorEnd;speed=Vector2.Distance(start,end)/seconds;}
        public MeleeAttackAnchor Anchor()=>new(anchorStart,anchorEnd);
    }
    // Fixed pose samples preserve the outward and return paths even in a long frame.
    public sealed class MeleeSweep
    {
        public float Duration {get;}
        private const int Samples=36;
        private readonly float interval;
        private readonly float contactStart,contactEnd;
        private readonly string weapon;
        private readonly Vector2 direction;
        private readonly float restReach;
        private readonly bool stopAtTerrain;
        public const float ContactRadius=.08f;
        private Vector2 previousOrigin,previousTip,previousSampleOrigin;
        private float elapsed;
        private int sample=1;
        public Vector2 Tip {get;private set;}
        public bool Finished=>sample>Samples;
        public float Elapsed=>elapsed;
        public MeleeSweep(string weapon,Vector2 origin,Vector2 direction,float? restReach=null,RoomLayout room=null)
        {
            Duration=Remains.Content.WeaponCatalog.Get(weapon).cooldown;interval=Duration/Samples;
            var definition=Remains.Content.WeaponCatalog.Get(weapon);
            this.restReach=restReach??definition.range;
            stopAtTerrain=definition.stopAtTerrain;
            contactStart=definition.meleeContactStart*Duration;contactEnd=definition.meleeContactEnd*Duration;
            this.weapon=weapon;this.direction=direction;previousOrigin=previousSampleOrigin=origin;
            Tip=previousTip=SampleTip(room,origin,0);
        }
        public void Advance(Vector2 origin,float dt,Action<MeleeSegment> emit,RoomLayout room=null)
        {
            if(Finished || dt<=0) return;
            float end=Mathf.Min(Duration,elapsed+dt);
            while(sample<=Samples && sample*interval<=end+.000001f)
            {
                float time=sample*interval;
                var sampledOrigin=Vector2.Lerp(previousOrigin,origin,Mathf.Clamp01((time-elapsed)/dt));
                var tip=SampleTip(room,sampledOrigin,time/Duration);
                float activeStart=Mathf.Max(time-interval,contactStart),activeEnd=Mathf.Min(time,contactEnd);
                if(activeEnd-activeStart>0.000001f)
                {
                    float fromFraction=(activeStart-(time-interval))/interval,toFraction=(activeEnd-(time-interval))/interval;
                    var from=Vector2.Lerp(previousTip,tip,fromFraction);
                    var to=Vector2.Lerp(previousTip,tip,toFraction);
                    emit(new MeleeSegment(from,to,Vector2.Lerp(previousSampleOrigin,sampledOrigin,fromFraction),
                        Vector2.Lerp(previousSampleOrigin,sampledOrigin,toFraction),activeEnd-activeStart));
                }
                previousTip=tip;previousSampleOrigin=sampledOrigin;sample++;
            }
            Tip=SampleTip(room,Vector2.Lerp(previousOrigin,origin,(end-elapsed)/dt),end/Duration);
            elapsed=end;previousOrigin=origin;
        }
        private Vector2 SampleTip(RoomLayout room,Vector2 origin,float progress)
        {
            var proposed=WeaponPose.Tip(weapon,origin,direction,progress,restReach);
            // A null room is the explicit geometry-free sampler used by trajectory checks.
            return room==null?proposed:MeleeTerrainOcclusion.ClipTip(room,origin,proposed,ContactRadius,stopAtTerrain);
        }
    }
}
