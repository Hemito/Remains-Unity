using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Gameplay
{
    // Read the resolved support plane; never change grounding or collision bounds.
    public sealed class StandingSupportPose
    {
        private readonly List<StandingSupportInterval> supports=new();
        private readonly StandingEdgePoseDiagnostics diagnostics=new();

        public StandingSupportPose()=>StandingEdgePoseCatalog.Load();

        public string Select(ActorState actor,RoomLayout room,string target)
        {
            if(actor==null || room==null) throw new System.ArgumentException("Выбор стоячей позы требует актёра и его комнаты.");
            if(target is not ("idle" or "crouch") || actor.climbing || ActorSessionRules.IsPassive(actor) ||
                Mathf.Abs(actor.vx)>.1f || Mathf.Abs(actor.vy)>.001f ||
                !StandingEdgePoseCatalog.TryGet(actor.anatomy,out var profile))
            {diagnostics.Clear(actor.id);return target;}
            supports.Clear();
            bool slope=false;
            var hull=Anatomy.Movement(actor);
            foreach(var surface in RoomSurfaces.All(room))
            {
                if(actor.x+hull.width*.5f<=surface.x || actor.x-hull.width*.5f>=surface.x+surface.width ||
                    Mathf.Abs(actor.y-RoomSupport.Top(surface,actor.x,hull.width))>RoomSupport.ContactTolerance) continue;
                if(surface.slopeDirection!=0) {slope=true;continue;}
                supports.Add(new StandingSupportInterval(surface.x,surface.x+surface.width));
            }
            if(supports.Count==0 && !slope) {diagnostics.Clear(actor.id);return target;}
            var result=StandingEdgePoseSelector.Select(profile,actor.crouched,actor.facing,actor.x,supports,slope);
            diagnostics.Observe(actor,supports,result);
            return result.Outcome is StandingEdgePoseOutcome.Standard or StandingEdgePoseOutcome.Selected?result.PoseId:target;
        }
    }
}
