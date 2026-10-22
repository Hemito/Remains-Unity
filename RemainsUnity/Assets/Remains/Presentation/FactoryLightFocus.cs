using System;
using System.Collections;
using UnityEngine;

namespace Remains.Presentation
{
    // Observe only the local camera per frame. A changed window queues an explicit cached rebuild.
    public sealed class FactoryLightFocus : MonoBehaviour
    {
        [SerializeField,InspectorName("Вид завода")] private FactoryLevelView view;
        private Camera camera;
        private Vector2 requested,previousPosition;
        private bool hasPosition;
        private const float PrefetchSeconds=4f;
        private const float TargetHysteresisSeconds=.2f;
        private readonly LightWindowPrefetchGate prefetchGate=new();
        public void Configure(FactoryLevelView source)=>view=source;
        public void Bind(Camera source)
        {
            if(view==null || source==null) throw new InvalidOperationException("Окно света завода: нужны вид уровня и локальная камера.");
            camera=source;
        }
        private void LateUpdate()
        {
            using var frameSection=Remains.Diagnostics.FrameHitchLog.Section("FactoryLightFocus.LateUpdate");
            if(camera==null || view==null || !view.IsReady) return;
            requested=camera.transform.position;
            Vector2 velocity=hasPosition && Time.unscaledDeltaTime>0?(requested-previousPosition)/Time.unscaledDeltaTime:Vector2.zero;
            previousPosition=requested;hasPosition=true;
            if(view.WindowFor(requested)!=view.LightWindowRooms) view.RequestLightWindow(requested,true);
            else
            {
                view.DeferLightWindowSwitch();
                Vector2 predicted=requested+Vector2.ClampMagnitude(velocity*PrefetchSeconds,24);
                var current=view.LightWindowRooms;var target=view.WindowFor(predicted);
                int dx=Mathf.Clamp(target.x-current.x,-1,1),dy=Mathf.Clamp(target.y-current.y,-1,1);
                if(dx!=0 && dy!=0) {if(Mathf.Abs(velocity.x)>=Mathf.Abs(velocity.y)) dy=0;else dx=0;}
                target=new RectInt(current.x+dx,current.y+dy,current.width,current.height);
                if(target!=current)
                {
                    if(prefetchGate.ShouldRequest(current,target,Time.unscaledTime,TargetHysteresisSeconds)) view.RequestLightWindow(predicted,false,target);
                }
                else prefetchGate.ShouldRequest(current,current,Time.unscaledTime,TargetHysteresisSeconds);
                // A stop or a noisy prediction inside the active window retains useful prepared work.
                // A stable different target replaces it after hysteresis; level/revision/disabling cancel explicitly.
            }
        }
        private void OnDisable() {hasPosition=false;prefetchGate.Reset();if(view!=null) view.CancelWindowPreparation();}
    }
}
