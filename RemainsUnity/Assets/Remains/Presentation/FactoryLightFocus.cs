using System;
using UnityEngine;

namespace Remains.Presentation
{
    // Observe only the local camera per frame: its visible rectangle for the level light, and the room window
    // (pictures, background erase) once the camera has settled in another one.
    public sealed class FactoryLightFocus : MonoBehaviour
    {
        [SerializeField,InspectorName("Вид завода")] private FactoryLevelView view;
        private Camera camera;
        // A camera on a room boundary would flip the window every frame: a new window must hold this long.
        private const float WindowHysteresisSeconds=.25f;
        private RectInt candidate=new(-1,-1,0,0);
        private float candidateSince;
        public void Configure(FactoryLevelView source)=>view=source;
        public void Bind(Camera source)
        {
            if(view==null || source==null) throw new InvalidOperationException("Свет завода: нужны вид уровня и локальная камера.");
            camera=source;
        }
        private void LateUpdate()
        {
            using var frameSection=Remains.Diagnostics.FrameHitchLog.Section("FactoryLightFocus.LateUpdate");
            if(camera==null || view==null || !view.IsReady) return;
            Vector2 position=camera.transform.position;
            float height=2*camera.orthographicSize,width=height*camera.aspect;
            view.SetViewArea(new Rect(position.x-width*.5f,position.y-height*.5f,width,height));
            var target=view.WindowFor(position);
            if(target==view.RoomWindow) {candidate=new RectInt(-1,-1,0,0);return;}
            if(target!=candidate) {candidate=target;candidateSince=Time.unscaledTime;return;}
            if(Time.unscaledTime-candidateSince>=WindowHysteresisSeconds) view.Focus(position);
        }
        private void OnDisable() {candidate=new RectInt(-1,-1,0,0);}
    }
}
