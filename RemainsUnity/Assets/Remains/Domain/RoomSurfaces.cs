using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Domain
{
    // Allocation-free authored-order iteration, followed by the current dynamic frame.
    public static class RoomSurfaces
    {
        // Call after publication, including explicit in-place geometry edits in tools.
        public static void Prepare(RoomLayout room)=>room.surfaceIndex=new RoomSurfaceIndex(room.platforms);
        public static SurfaceSet All(RoomLayout room,RoomSurface excluded=null)=>new(room,excluded,default,false);
        public static SurfaceSet Near(RoomLayout room,Rect bounds,RoomSurface excluded=null)
        {
            // Conservative margin absorbs float rounding at authored contact boundaries.
            const float margin=.0001f;
            return new SurfaceSet(room,excluded,Rect.MinMaxRect(bounds.xMin-margin,bounds.yMin-margin,
                bounds.xMax+margin,bounds.yMax+margin),true);
        }
        // Near plus the tops of physical bodies (BodyTopSurfaces): for actor movement queries only.
        public static SurfaceSet NearForActors(RoomLayout room,Rect bounds,RoomSurface excluded=null)
        {
            const float margin=.0001f;
            return new SurfaceSet(room,excluded,Rect.MinMaxRect(bounds.xMin-margin,bounds.yMin-margin,
                bounds.xMax+margin,bounds.yMax+margin),true,true);
        }
        public static float MaximumSlopeGradient(RoomLayout room)
        {
            float result=0;
            if(room.surfaceIndex!=null && ReferenceEquals(room.surfaceIndex.Source,room.platforms))
                result=room.surfaceIndex.MaximumSlopeGradient;
            else foreach(var surface in room.platforms)
                if(surface.slopeDirection!=0 && surface.width>0) result=Mathf.Max(result,surface.height/surface.width);
            if(room.dynamicSurfaces!=null) for(int i=0;i<room.dynamicSurfaces.Count;i++)
            {
                var surface=room.dynamicSurfaces.Pose(i).Current;
                if(surface.slopeDirection!=0 && surface.width>0) result=Mathf.Max(result,surface.height/surface.width);
            }
            return result;
        }
        public readonly struct SurfaceSet : IEnumerable<RoomSurface>
        {
            private readonly RoomLayout room;
            private readonly RoomSurface excluded;
            private readonly Rect bounds;
            private readonly bool near,bodies;
            public SurfaceSet(RoomLayout room,RoomSurface excluded,Rect bounds,bool near,bool bodies=false)
            {this.room=room;this.excluded=excluded;this.bounds=bounds;this.near=near;this.bodies=bodies;}
            public Enumerator GetEnumerator()=>new(room,excluded,bounds,near,bodies);
            IEnumerator<RoomSurface> IEnumerable<RoomSurface>.GetEnumerator()=>GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
        }
        public struct Enumerator : IEnumerator<RoomSurface>
        {
            private readonly RoomLayout room;
            private readonly RoomSurface excluded;
            private readonly Rect bounds;
            private readonly bool near;
            private readonly RoomSurfaceIndex spatial;
            private readonly int left,right,bottom,top;
            private readonly bool bodies;
            private int index,dynamicIndex,bodyIndex;
            private bool authoredDone;
            public RoomSurface Current {get;private set;}
            object IEnumerator.Current=>Current;
            public Enumerator(RoomLayout room,RoomSurface excluded,Rect bounds,bool near,bool bodies=false)
            {
                this.room=room;this.excluded=excluded;this.bounds=bounds;this.bodies=bodies;bodyIndex=-1;
                this.near=near && room.surfaceIndex!=null && ReferenceEquals(room.surfaceIndex.Source,room.platforms);
                left=RoomSurfaceIndex.Cell(bounds.xMin);right=RoomSurfaceIndex.Cell(bounds.xMax);
                bottom=RoomSurfaceIndex.Cell(bounds.yMin);top=RoomSurfaceIndex.Cell(bounds.yMax);
                // Small/unprepared layouts and wide queries intentionally use linear traversal;
                // geometry is never rebuilt by a simulation query.
                spatial=this.near &&
                    (long)(right-left+1)*(top-bottom+1)<=64 ? room.surfaceIndex:null;
                index=-1;dynamicIndex=-1;authoredDone=false;Current=null;
            }
            public bool MoveNext()
            {
                while(!authoredDone)
                {
                    index=spatial==null?index+1:spatial.Next(index,left,right,bottom,top);
                    if(index<0 || index>=room.platforms.Length) {authoredDone=true;break;}
                    Current=room.platforms[index];
                    if(Accept()) return true;
                }
                while(++dynamicIndex<(room.dynamicSurfaces?.Count??0))
                {
                    Current=room.dynamicSurfaces.Pose(dynamicIndex).Current;
                    if(Accept()) return true;
                }
                if(bodies) while(++bodyIndex<(room.bodySurfaces?.Count??0))
                {
                    Current=room.bodySurfaces[bodyIndex];
                    if(Accept()) return true;
                }
                Current=null;return false;
            }
            private bool Accept()=>!ReferenceEquals(Current,excluded) && (!near ||
                Current.x<=bounds.xMax && Current.x+Current.width>=bounds.xMin &&
                Current.y<=bounds.yMax && Current.y+Current.height>=bounds.yMin);
            public void Reset() {index=-1;dynamicIndex=-1;bodyIndex=-1;authoredDone=false;Current=null;}
            public void Dispose() {}
        }
    }
}
