using System;
using System.Collections.Generic;

namespace Remains.Domain
{
    [Serializable] public sealed class StandingFootPadDefinition
    {
        public string shape,path;
        public float minimumX,maximumX,minimumY,lowestX,visibleMinimumX,visibleMaximumX,visibleMaximumY;
        public bool eligible;
    }
    [Serializable] public sealed class StandingFootPoseDefinition
    {
        public string poseId,side;
        public bool crouched;
        public int rank,sourceSprite,sourceFrame;
        public StandingFootPadDefinition[] contacts;
    }
    [Serializable] public sealed class StandingEdgePoseProfileDefinition
    {
        public string anatomy;
        public StandingFootPoseDefinition standing,crouching;
        public StandingFootPoseDefinition[] candidates;
    }
    [Serializable] public sealed class StandingEdgePoseManifest
    {
        public int schema,alphaMinimum;
        public string sourceHash,evidence;
        public bool originalRuntimeVerified;
        public float soleBandHeight;
        public string[] shapes;
        public StandingEdgePoseProfileDefinition[] profiles;
    }
    public readonly struct StandingSupportInterval
    {
        public readonly float Minimum,Maximum;
        public StandingSupportInterval(float minimum,float maximum)
        {
            if(!float.IsFinite(minimum) || !float.IsFinite(maximum) || minimum>=maximum)
                throw new ArgumentException($"Некорректный интервал стоячей опоры: [{minimum},{maximum}].");
            Minimum=minimum;Maximum=maximum;
        }
        public override string ToString()=>$"[{Minimum},{Maximum}]";
    }
    public enum StandingEdgePoseOutcome { Standard,Selected,MissingContact,NotApplicable }
    public readonly struct StandingEdgePoseResult
    {
        public readonly StandingEdgePoseOutcome Outcome;
        public readonly string PoseId,Reason;
        public readonly int ProjectedFeetCovered,ProjectedFeetContacting;
        internal StandingEdgePoseResult(StandingEdgePoseOutcome outcome,string pose,int covered,int contacting,string reason)
        {Outcome=outcome;PoseId=pose;ProjectedFeetCovered=covered;ProjectedFeetContacting=contacting;Reason=reason;}
    }
    // Immutable prepared data; graphic measurements run only in the exporter.
    public sealed class StandingEdgePoseProfile
    {
        public string Anatomy {get;}
        internal readonly StandingFootPoseDefinition Standing,Crouching;
        internal readonly StandingFootPoseDefinition[] Candidates;
        public StandingEdgePoseProfile(StandingEdgePoseProfileDefinition source)
        {
            if(source==null || string.IsNullOrWhiteSpace(source.anatomy) || source.candidates==null || source.candidates.Length==0)
                throw new ArgumentException("Не задан профиль стоячих краевых поз.");
            Anatomy=source.anatomy;
            Standing=Copy(source.standing,false,true);Crouching=Copy(source.crouching,true,true);
            var ids=new HashSet<string> {Standing.poseId,Crouching.poseId};
            Candidates=new StandingFootPoseDefinition[source.candidates.Length];
            for(int i=0;i<Candidates.Length;i++)
            {
                var pose=source.candidates[i];Candidates[i]=Copy(pose,pose?.crouched??false,false);
                if(!ids.Add(pose.poseId)) throw new ArgumentException($"Повторная краевая поза: anatomy={Anatomy}, pose={pose.poseId}.");
            }
        }
        private static StandingFootPoseDefinition Copy(StandingFootPoseDefinition pose,bool crouched,bool normal)
        {
            if(pose==null || string.IsNullOrWhiteSpace(pose.poseId) || pose.crouched!=crouched || pose.rank<0 ||
                (normal?pose.side!="normal":pose.side!="back" && pose.side!="front") || pose.contacts==null || pose.contacts.Length==0)
                throw new ArgumentException($"Некорректная стоячая поза: pose={pose?.poseId}, side={pose?.side}.");
            var result=new StandingFootPoseDefinition {poseId=pose.poseId,side=pose.side,crouched=crouched,rank=pose.rank,
                sourceSprite=pose.sourceSprite,sourceFrame=pose.sourceFrame,contacts=new StandingFootPadDefinition[pose.contacts.Length]};
            bool eligible=false;
            for(int i=0;i<pose.contacts.Length;i++)
            {
                var pad=pose.contacts[i];
                if(pad==null || string.IsNullOrWhiteSpace(pad.shape) || string.IsNullOrWhiteSpace(pad.path) ||
                    !float.IsFinite(pad.minimumX) || !float.IsFinite(pad.maximumX) || !float.IsFinite(pad.minimumY) || !float.IsFinite(pad.lowestX) ||
                    !float.IsFinite(pad.visibleMinimumX) || !float.IsFinite(pad.visibleMaximumX) || !float.IsFinite(pad.visibleMaximumY) ||
                    pad.minimumX>=pad.maximumX || pad.minimumX<pad.visibleMinimumX || pad.maximumX>pad.visibleMaximumX || pad.minimumY>pad.visibleMaximumY)
                    throw new ArgumentException($"Некорректная графическая подошва: pose={pose.poseId}, part={i}.");
                result.contacts[i]=new StandingFootPadDefinition {shape=pad.shape,path=pad.path,eligible=pad.eligible,
                    minimumX=pad.minimumX,maximumX=pad.maximumX,minimumY=pad.minimumY,lowestX=pad.lowestX,
                    visibleMinimumX=pad.visibleMinimumX,visibleMaximumX=pad.visibleMaximumX,visibleMaximumY=pad.visibleMaximumY};
                eligible|=pad.eligible;
            }
            if(!eligible) throw new ArgumentException("Нет опорной роли в стоячей позе: "+pose.poseId);
            return result;
        }
    }
    public static class StandingEdgePoseSelector
    {
        // The caller supplies actual flat support intervals at the already
        // resolved body support plane. No grounding, movement or vertical offset.
        public static StandingEdgePoseResult Select(StandingEdgePoseProfile profile,bool crouched,int facing,float actorX,
            IReadOnlyList<StandingSupportInterval> actualSupports,bool slopedSupport=false)
        {
            if(profile==null) return new(StandingEdgePoseOutcome.NotApplicable,null,0,0,"У анатомии не объявлены краевые позы.");
            if(slopedSupport) return new(StandingEdgePoseOutcome.NotApplicable,null,0,0,"Наклонная опора имеет отдельный контракт позы.");
            if(facing!=1 && facing!=-1 || !float.IsFinite(actorX) || actualSupports==null)
                throw new ArgumentException($"Некорректный запрос стоячей позы: anatomy={profile.Anatomy}, facing={facing}, x={actorX}.");
            foreach(var interval in actualSupports)
                if(!float.IsFinite(interval.Minimum) || !float.IsFinite(interval.Maximum) || interval.Minimum>=interval.Maximum)
                    throw new ArgumentException("Некорректный фактический интервал опоры.");
            var normal=crouched?profile.Crouching:profile.Standing;
            int normalCount=Covered(normal,actorX,facing,actualSupports);
            if(normalCount==Eligible(normal)) return new(StandingEdgePoseOutcome.Standard,normal.poseId,normalCount,normalCount,null);
            StandingFootPoseDefinition chosen=null;int count=0;
            foreach(var pose in profile.Candidates)
            {
                if(pose.crouched!=crouched || !HasSupportSide(pose.side,actorX,facing,actualSupports)) continue;
                int contacting=Contacting(pose,actorX,facing,actualSupports);
                if(contacting==0 || chosen!=null && (pose.rank>chosen.rank || pose.rank==chosen.rank &&
                    (contacting<count || contacting==count && String.CompareOrdinal(pose.poseId,chosen.poseId)>=0))) continue;
                chosen=pose;count=contacting;
            }
            return chosen==null?new(StandingEdgePoseOutcome.MissingContact,null,0,0,"Ни одна объявленная опорная подошва не пересекает фактическую опору."):
                new(StandingEdgePoseOutcome.Selected,chosen.poseId,Covered(chosen,actorX,facing,actualSupports),count,null);
        }
        private static bool HasSupportSide(string side,float x,int facing,IReadOnlyList<StandingSupportInterval> supports)
        {
            bool left=(side=="back")== (facing>0);
            foreach(var interval in supports) if(left?interval.Minimum<x:interval.Maximum>x) return true;
            return false;
        }
        private static int Eligible(StandingFootPoseDefinition pose)
        {int count=0;foreach(var pad in pose.contacts) if(pad.eligible) count++;return count;}
        private static int Covered(StandingFootPoseDefinition pose,float x,int facing,IReadOnlyList<StandingSupportInterval> supports)
        {
            int count=0;
            foreach(var pad in pose.contacts)
                if(pad.eligible && CoveredInterval(x+(facing>0?pad.minimumX:-pad.maximumX),
                    x+(facing>0?pad.maximumX:-pad.minimumX),supports)) count++;
            return count;
        }
        private static int Contacting(StandingFootPoseDefinition pose,float x,int facing,IReadOnlyList<StandingSupportInterval> supports)
        {
            int count=0;
            foreach(var pad in pose.contacts)
            {
                if(!pad.eligible) continue;
                float minimum=x+(facing>0?pad.minimumX:-pad.maximumX),maximum=x+(facing>0?pad.maximumX:-pad.minimumX);
                foreach(var interval in supports)
                    if(Math.Min(maximum,interval.Maximum)>Math.Max(minimum,interval.Minimum)) {count++;break;}
            }
            return count;
        }
        private static bool CoveredInterval(float minimum,float maximum,IReadOnlyList<StandingSupportInterval> supports)
        {
            float covered=minimum;
            // Union coverage without allocations/sorting in per-frame selection.
            // Touching seams join; no tolerance may bridge a real empty gap.
            for(int pass=0;pass<supports.Count;pass++)
            {
                float next=covered;
                foreach(var interval in supports)
                    if(interval.Minimum<=covered && interval.Maximum>next) next=interval.Maximum;
                if(next>=maximum) return true;
                if(next<=covered) return false;
                covered=next;
            }
            return false;
        }
    }
}
