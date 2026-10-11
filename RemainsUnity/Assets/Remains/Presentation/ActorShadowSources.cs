using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Presentation
{
    public readonly struct ActorShadowSource
    {
        public readonly string Id;
        public readonly Vector2 Position;
        public readonly float Radius,Strength;
        public ActorShadowSource(string id,Vector2 position,float radius,float strength)
        {Id=id;Position=position;Radius=radius;Strength=strength;}
    }

    // Prepared once per light window. Select is called explicitly by the actor coordinator,
    // never from Update here; it reads canonical cached terrain and current door rectangles.
    public sealed class ActorShadowSources
    {
        private float[,] absorption;
        private Vector2 origin;
        private float texel,ambient;
        private Matrix4x4 worldToLevel;
        private IReadOnlyList<ActorShadowSource> lamps;
        public void Clear() {absorption=null;lamps=null;}
        public float Visibility(Vector2 from,Vector2 to,IReadOnlyList<(Rect bounds,double opacity)> doors)
        {
            if(absorption==null) return 0;
            Vector2 a=worldToLevel.MultiplyPoint3x4(from),b=worldToLevel.MultiplyPoint3x4(to);
            float transmission=Transmission(a,b);
            foreach(var door in doors)
                if(SegmentIntersects(a,b,door.bounds)) transmission*=(float)(1-door.opacity);
            return transmission;
        }

        public void Configure(float[,] terrain,Vector2 levelOrigin,float texelSize,Matrix4x4 inverseLevel,
            IReadOnlyList<ActorShadowSource> sources,float ambientContribution)
        {
            absorption=terrain??throw new ArgumentNullException(nameof(terrain));
            if(texelSize<=0 || !float.IsFinite(texelSize)) throw new ArgumentOutOfRangeException(nameof(texelSize));
            lamps=sources??throw new ArgumentNullException(nameof(sources));
            origin=levelOrigin;texel=texelSize;worldToLevel=inverseLevel;ambient=Mathf.Max(0,ambientContribution);
        }

        public void Select(Vector2 actor,ISet<string> dark,IReadOnlyList<(Rect bounds,double opacity)> doors,
            int maxSources,List<ActorShadowSource> output)
        {
            if(output==null) throw new ArgumentNullException(nameof(output));
            if(maxSources<1 || maxSources>3) throw new ArgumentOutOfRangeException(nameof(maxSources));
            output.Clear();
            if(absorption==null) return; // No light window has been prepared; no sources exist yet.
            float total=ambient;
            foreach(var source in lamps)
            {
                if(dark.Contains(source.Id) || source.Strength<=0) continue;
                float visibility=Visibility(actor,source.Position,doors);
                if(visibility<=0) continue;
                float strength=source.Strength*visibility/Mathf.Max(.5f,Vector2.Distance(actor,source.Position));
                if(strength<=0) continue;
                total+=strength;
                int index=0;
                while(index<output.Count && (output[index].Strength>strength ||
                    output[index].Strength==strength && string.CompareOrdinal(output[index].Id,source.Id)<0)) index++;
                if(index>=maxSources) continue;
                output.Insert(index,new ActorShadowSource(source.Id,source.Position,source.Radius,strength));
                if(output.Count>maxSources) output.RemoveAt(maxSources);
            }
            if(total<=0) return;
            for(int i=0;i<output.Count;i++)
            {
                var source=output[i];
                output[i]=new ActorShadowSource(source.Id,source.Position,source.Radius,source.Strength/total);
            }
        }

        private float Transmission(Vector2 from,Vector2 to)
        {
            Vector2 a=(from-origin)/texel,b=(to-origin)/texel,delta=b-a;
            int x=Mathf.FloorToInt(a.x),y=Mathf.FloorToInt(a.y);
            int endX=Mathf.FloorToInt(b.x),endY=Mathf.FloorToInt(b.y);
            int sx=delta.x>=0?1:-1,sy=delta.y>=0?1:-1;
            float dx=delta.x==0?float.PositiveInfinity:Mathf.Abs(1/delta.x);
            float dy=delta.y==0?float.PositiveInfinity:Mathf.Abs(1/delta.y);
            float tx=delta.x==0?float.PositiveInfinity:(sx>0?x+1-a.x:a.x-x)*dx;
            float ty=delta.y==0?float.PositiveInfinity:(sy>0?y+1-a.y:a.y-y)*dy;
            float result=1;
            int limit=absorption.GetLength(0)+absorption.GetLength(1)+1;
            for(int step=0;step<limit;step++)
            {
                if(!Inside(x,y)) return 0;
                // The emitting end texel is not an obstacle to its own outgoing light.
                if(x==endX && y==endY) return result;
                result*=1-absorption[x,y];
                if(result<=.0001f) return 0;
                if(Mathf.Abs(tx-ty)<.000001f)
                {
                    if(!Inside(x+sx,y) || !Inside(x,y+sy) ||
                        absorption[x+sx,y]>=.99f || absorption[x,y+sy]>=.99f) return 0;
                    x+=sx;y+=sy;tx+=dx;ty+=dy;
                }
                else if(tx<ty) {x+=sx;tx+=dx;}
                else {y+=sy;ty+=dy;}
            }
            throw new InvalidOperationException("Выбор источников тени: трассировка не достигла лампы в границах кешированной сетки.");
        }
        private bool Inside(int x,int y)=>x>=0 && y>=0 && x<absorption.GetLength(0) && y<absorption.GetLength(1);

        private static bool SegmentIntersects(Vector2 a,Vector2 b,Rect rect)
        {
            Vector2 delta=b-a;
            float lo=0,hi=1;
            return Clip(a.x,delta.x,rect.xMin,rect.xMax,ref lo,ref hi) &&
                Clip(a.y,delta.y,rect.yMin,rect.yMax,ref lo,ref hi);
        }
        private static bool Clip(float start,float delta,float min,float max,ref float lo,ref float hi)
        {
            if(delta==0) return start>=min && start<=max;
            float a=(min-start)/delta,b=(max-start)/delta;
            if(a>b) (a,b)=(b,a);
            lo=Mathf.Max(lo,a);hi=Mathf.Min(hi,b);
            return hi>=lo;
        }
    }
}
