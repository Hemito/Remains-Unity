using System;
using Remains.Domain;
using UnityEngine;

namespace Remains.Presentation
{
    // One readiness contract for rendering, HUD and local commands. Subsequent door patches
    // keep a complete published pair visible; only the initial room binding gates presentation.
    public sealed class RoomPresentationGate : IDisposable
    {
        private readonly Camera camera;
        private readonly int visibleLayers;
        private RoomLighting lighting;
        private RoomLayout expected;
        private PrototypeHud hud;
        private bool actorPresent;
        private bool entryPending;
        public bool IsReady=>!entryPending && actorPresent && lighting!=null && lighting.IsReady && ReferenceEquals(lighting.Room,expected);
        public bool IsPreparing=>entryPending || actorPresent && !IsReady;
        public event Action<RoomLighting,Exception> Failed;
        public RoomPresentationGate(Camera camera)
        {
            this.camera=camera??throw new ArgumentNullException(nameof(camera));visibleLayers=camera.cullingMask;
        }
        public void SetHud(PrototypeHud value) {hud=value;Refresh();}
        public void SetEntryPending(bool value) {entryPending=value;Refresh();}
        public void SetContext(bool hasActor,RoomLayout room)
        {actorPresent=hasActor;expected=room;Refresh();}
        public void Bind(RoomLighting value)
        {
            if(lighting!=null) {lighting.ReadinessChanged-=OnReady;lighting.Failed-=OnFailure;}
            lighting=value;
            if(lighting!=null) {lighting.ReadinessChanged+=OnReady;lighting.Failed+=OnFailure;}
            Refresh();
            if(lighting?.Failure!=null) OnFailure(lighting,lighting.Failure);
        }
        private void OnReady(RoomLighting source) {if(source==lighting) Refresh();}
        private void OnFailure(RoomLighting source,Exception error)
        {if(source!=lighting) return;Refresh();Failed?.Invoke(source,error);}
        private void Refresh()
        {
            // Camera.Render used by captures follows this same production visibility policy.
            bool visible=!entryPending && lighting!=null && lighting.IsReady && (!actorPresent || IsReady);
            // A failed HUD publication must leave the world hidden. Publish the complete
            // interface before opening the camera, including readiness between Updates.
            if(camera!=null) camera.cullingMask=0;
            if(hud!=null) hud.SetRoomPresentationPending(IsPreparing);
            if(camera!=null && visible) camera.cullingMask=visibleLayers;
        }
        public void Dispose()
        {
            // Unity may already have destroyed the HUD or its children during application
            // shutdown. Unsubscribe directly; teardown must not refresh a dying interface.
            if(lighting!=null) {lighting.ReadinessChanged-=OnReady;lighting.Failed-=OnFailure;}
            lighting=null;expected=null;actorPresent=false;entryPending=false;hud=null;
            if(camera!=null) camera.cullingMask=visibleLayers;
        }
    }
}
