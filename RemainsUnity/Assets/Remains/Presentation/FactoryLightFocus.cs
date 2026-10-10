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
        private Coroutine rebuild;
        private Vector2 requested;
        public void Configure(FactoryLevelView source)=>view=source;
        public void Bind(Camera source)
        {
            if(view==null || source==null) throw new InvalidOperationException("Окно света завода: нужны вид уровня и локальная камера.");
            camera=source;
        }
        private void LateUpdate()
        {
            if(camera==null || view==null || !view.IsReady) return;
            requested=camera.transform.position;
            if(view.WindowFor(requested)==view.LightWindowRooms || rebuild!=null) return;
            rebuild=StartCoroutine(Rebuild());
        }
        private IEnumerator Rebuild()
        {
            yield return null;
            try {if(view!=null && view.IsReady) view.Focus(requested);}
            catch(Exception error) {view.ReportFailure(error);}
            rebuild=null;
        }
        private void OnDisable() {if(rebuild!=null) StopCoroutine(rebuild);rebuild=null;}
    }
}
