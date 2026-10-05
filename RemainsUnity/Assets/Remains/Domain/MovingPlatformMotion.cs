using System;
using UnityEngine;

namespace Remains.Domain
{
    // Transient value result: no cached clock, no mutation, no network/save callbacks.
    public readonly struct MovingPlatformSample
    {
        public readonly Vector2 Anchor;
        public readonly Rect SupportBounds,GraphicBounds;
        public readonly double Progress;
        public readonly bool Started,Completed;
        public bool Moving=>Started && !Completed;
        public MovingPlatformSample(Vector2 anchor,Rect support,Rect graphic,double progress,bool started,bool completed)
        {Anchor=anchor;SupportBounds=support;GraphicBounds=graphic;Progress=progress;Started=started;Completed=completed;}
    }
    // Construct/rebind on explicit definition load/change. Type validation and its snapshot
    // happen once; sampling performs only bounded scalar/state checks and pose arithmetic.
    public sealed class MovingPlatformMotion
    {
        private readonly string definition;
        private readonly int version;
        private readonly float width,height,graphicOffsetX,graphicOffsetY,graphicWidth,graphicHeight;
        private readonly double durationSeconds;
        public MovingPlatformMotion(MovingPlatformDefinition type)
        {
            if(type==null) throw new ArgumentNullException(nameof(type));
            type.Validate();definition=type.id;version=type.version;
            width=type.width;height=type.height;graphicOffsetX=type.graphicOffsetX;graphicOffsetY=type.graphicOffsetY;
            graphicWidth=type.graphicWidth;graphicHeight=type.graphicHeight;durationSeconds=type.durationSeconds;
        }
        public MovingPlatformSample Evaluate(MovingPlatformState state,double levelElapsedSeconds)
        {
            if(state==null) throw new ArgumentNullException(nameof(state));
            MechanismStateValidation.Require(double.IsFinite(levelElapsedSeconds) && levelElapsedSeconds>=0,"level elapsedSeconds",state.id);
            state.Validate();
            MechanismStateValidation.Require(!state.removed,"removed: нельзя вычислять живую опору",state.id);
            MechanismStateValidation.Require(state.definition==definition && state.definitionVersion==version,"definition ID/version mismatch",state.id);
            var segment=state.segment;
            double progress=0;bool started=false,completed=false;
            Vector2 anchor=state.endpoint==0?state.Start:state.End;
            if(segment!=null)
            {
                MechanismStateValidation.Require(segment.durationSeconds==durationSeconds,"segment duration != definition version",state.id);
                started=levelElapsedSeconds>=segment.startElapsedSeconds;
                completed=levelElapsedSeconds>=segment.EndElapsedSeconds;
                progress=!started?0:completed?1:(levelElapsedSeconds-segment.startElapsedSeconds)/segment.durationSeconds;
                var from=segment.fromEndpoint==0?state.Start:state.End;
                var to=segment.toEndpoint==0?state.Start:state.End;
                // Double intermediate avoids overflow in (float end - float start).
                anchor=progress==0?from:progress==1?to:new Vector2(
                    (float)(from.x+((double)to.x-from.x)*progress),(float)(from.y+((double)to.y-from.y)*progress));
            }
            // Registration is bottom-centre for support. Artwork has separate local bounds.
            var support=new Rect(anchor.x-width*.5f,anchor.y,width,height);
            var graphic=new Rect(anchor.x+graphicOffsetX,anchor.y+graphicOffsetY,graphicWidth,graphicHeight);
            MechanismStateValidation.Require(Finite(support) && Finite(graphic),"sample bounds overflow",state.id);
            return new MovingPlatformSample(anchor,support,graphic,progress,started,completed);
        }
        private static bool Finite(Rect bounds)=>float.IsFinite(bounds.xMin) && float.IsFinite(bounds.yMin) &&
            float.IsFinite(bounds.xMax) && float.IsFinite(bounds.yMax);
    }
}
