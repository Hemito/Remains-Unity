using System.Collections;
using System.Collections.Generic;

namespace Remains.Domain
{
    // Allocation-free foreach over authored/door geometry and the sampled dynamic frame.
    public static class RoomSurfaces
    {
        public static SurfaceSet All(RoomLayout room,RoomSurface excluded=null)=>new(room,excluded);
        public readonly struct SurfaceSet : IEnumerable<RoomSurface>
        {
            private readonly RoomLayout room;
            private readonly RoomSurface excluded;
            public SurfaceSet(RoomLayout room,RoomSurface excluded) {this.room=room;this.excluded=excluded;}
            public Enumerator GetEnumerator()=>new(room,excluded);
            IEnumerator<RoomSurface> IEnumerable<RoomSurface>.GetEnumerator()=>GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
        }
        public struct Enumerator : IEnumerator<RoomSurface>
        {
            private readonly RoomLayout room;
            private readonly RoomSurface excluded;
            private int index;
            public RoomSurface Current {get;private set;}
            object IEnumerator.Current=>Current;
            public Enumerator(RoomLayout room,RoomSurface excluded) {this.room=room;this.excluded=excluded;index=-1;Current=null;}
            public bool MoveNext()
            {
                int authored=room.platforms.Length,total=authored+(room.dynamicSurfaces?.Count??0);
                while(++index<total)
                {
                    Current=index<authored?room.platforms[index]:room.dynamicSurfaces.Pose(index-authored).Current;
                    if(!ReferenceEquals(Current,excluded)) return true;
                }
                Current=null;return false;
            }
            public void Reset() {index=-1;Current=null;}
            public void Dispose() {}
        }
    }
}
