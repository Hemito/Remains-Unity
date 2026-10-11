using System;
using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Gameplay
{
    // Observe the physics contact; neither animation nor facing authors a support.
    public sealed class SlopeLocomotionPose
    {
        // Matches the strict footprint overlap used by ActorTerrainMotion.Supported.
        private const float ContactInset=.0001f;
        public SlopeLocomotionPose()=>SlopePoseCatalog.Load();
        public bool IsStanding(ActorState actor,string pose)=>SlopePoseCatalog.TryGet(actor.anatomy,out var profile) && profile.IsStanding(pose);
        public string Select(ActorState actor,RoomLayout room,string target)
        {
            if(actor==null || room==null) throw new ArgumentException("Наклонная поза требует актёра и его комнаты.");
            if(actor.crouched || actor.climbing || ActorSessionRules.IsPassive(actor) ||
                !SlopePoseCatalog.TryGet(actor.anatomy,out var profile)) return target;
            bool moving=target is "walk" or "run" || profile.IsMoving(target);
            if(!moving && target!="idle" && !profile.IsStanding(target)) return target;
            var support=ResolveSupport(room,Anatomy.Movement(actor),actor.Position);
            if(support==null || support.slopeDirection==0) return target;
            if(actor.facing is not (-1 or 1)) throw new InvalidOperationException($"Некорректное направление наклонной позы: actor={actor.id}, anatomy={actor.anatomy}, facing={actor.facing}, room={room.id}.");
            // Walking backwards does not turn an uphill-facing body into a downhill one.
            return profile.Select(moving,actor.facing*support.slopeDirection>0);
        }
        public static RoomSurface ResolveSupport(RoomLayout room,MovementHull hull,Vector2 feet)
        {
            if(room==null || hull==null) throw new ArgumentException("Поиск наклонного контакта требует комнаты и габарита тела.");
            if(!float.IsFinite(feet.x) || !float.IsFinite(feet.y) || !float.IsFinite(hull.width) || hull.width<=0)
                throw new InvalidOperationException($"Некорректные данные наклонного контакта: room={room.id}, feet={feet}, width={hull.width}.");
            RoomSurface selected=null;float selectedGap=0,selectedTop=0,selectedCoverage=0;
            float half=hull.width*.5f;
            foreach(var surface in RoomSurfaces.Near(room,Rect.MinMaxRect(feet.x-half,feet.y-RoomSupport.ContactTolerance,
                feet.x+half,feet.y+RoomSupport.ContactTolerance)))
            {
                if(feet.x+half<=surface.x+ContactInset || feet.x-half>=surface.x+surface.width-ContactInset) continue;
                float top=RoomSupport.Top(surface,feet.x,hull.width),gap=Mathf.Abs(feet.y-top);
                if(!float.IsFinite(top)) throw new InvalidOperationException($"Некорректная высота контакта: room={room.id}, feet={feet}, surface=({surface.x},{surface.y},{surface.width},{surface.height}), slope={surface.slopeDirection}.");
                if(gap>RoomSupport.ContactTolerance) continue;
                if(surface.slopeDirection is not (-1 or 0 or 1)) throw new InvalidOperationException($"Некорректное направление опоры: room={room.id}, slope={surface.slopeDirection}.");
                float coverage=Mathf.Min(feet.x+half,surface.x+surface.width)-Mathf.Max(feet.x-half,surface.x);
                if(selected==null || Better(surface,gap,top,coverage,selected,selectedGap,selectedTop,selectedCoverage))
                {selected=surface;selectedGap=gap;selectedTop=top;selectedCoverage=coverage;}
            }
            return selected;
        }
        private static bool Better(RoomSurface candidate,float gap,float top,float coverage,RoomSurface current,float currentGap,float currentTop,float currentCoverage)
        {
            int order=gap.CompareTo(currentGap);if(order!=0) return order<0;
            order=top.CompareTo(currentTop);if(order!=0) return order>0;
            // The plateau wins before coverage when a slope is clamped to that plane.
            if((candidate.slopeDirection==0)!=(current.slopeDirection==0)) return candidate.slopeDirection==0;
            order=coverage.CompareTo(currentCoverage);if(order!=0) return order>0;
            // Remaining ties are geometric, never array order or a resource/entity ID.
            float gradient=candidate.slopeDirection==0?0:candidate.height/candidate.width;
            float currentGradient=current.slopeDirection==0?0:current.height/current.width;
            order=gradient.CompareTo(currentGradient);if(order!=0) return order>0;
            order=candidate.slopeDirection.CompareTo(current.slopeDirection);if(order!=0) return order>0;
            order=candidate.x.CompareTo(current.x);if(order!=0) return order<0;
            order=candidate.y.CompareTo(current.y);if(order!=0) return order<0;
            order=candidate.width.CompareTo(current.width);if(order!=0) return order>0;
            return candidate.height>current.height;
        }
    }
}
