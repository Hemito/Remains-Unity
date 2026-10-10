using System;
using Remains.Domain;

namespace Remains.Presentation
{
    // Admission and presentation depend on published light readiness, not on a particular renderer.
    public interface IRoomLighting
    {
        RoomLayout Room {get;}
        bool IsReady {get;}
        bool IsUpdating {get;}
        Exception Failure {get;}
        long AppliedOcclusionRevision {get;}
        event Action<IRoomLighting> PresentationChanged;
        event Action<IRoomLighting,Exception> PresentationFailed;
        void Synchronize(RoomLayout room,WorldState world,long revision);
        void Release();
    }
}
