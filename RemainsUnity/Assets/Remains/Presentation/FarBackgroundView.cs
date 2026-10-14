using System;
using UnityEngine;

namespace Remains.Presentation
{
    // Land far background (original World.drawFon/setFonSize): a screen-fixed image behind
    // everything, scaled to cover the view with its aspect kept and anchored at the view's
    // top-left corner; it does not scroll with the camera. Visible where a room draws no
    // backdrop (backwall "sky" or transparent backdrop areas).
    public sealed class FarBackgroundView : MonoBehaviour
    {
        public const int SortingOrder=-1000;
        private const float Distance=50;
        [SerializeField,InspectorName("Камера")] private Camera viewCamera;
        [SerializeField,InspectorName("Изображение")] private Texture2D image;
        private float fittedSize=-1,fittedAspect=-1;

        public static FarBackgroundView Create(Camera camera,Texture2D texture,Material material)
        {
            if(camera==null || texture==null || material==null) throw new ArgumentNullException(camera==null?nameof(camera):texture==null?nameof(texture):nameof(material));
            if(!camera.orthographic) throw new InvalidOperationException("Дальний фон рассчитан на ортографическую камеру.");
            var obj=new GameObject("Дальний фон: "+texture.name);
            obj.transform.SetParent(camera.transform,false);
            obj.AddComponent<MeshFilter>().sharedMesh=Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var renderer=obj.AddComponent<MeshRenderer>();
            renderer.sharedMaterial=material;
            renderer.sortingOrder=SortingOrder;
            var view=obj.AddComponent<FarBackgroundView>();
            view.viewCamera=camera;view.image=texture;
            view.Fit();
            return view;
        }

        // Cheap per-frame check: refit only when the camera's view size changes.
        private void LateUpdate()
        {
            using var frameSection=Remains.Diagnostics.FrameHitchLog.Section("FarBackgroundView.LateUpdate");
            if(viewCamera!=null && (viewCamera.orthographicSize!=fittedSize || viewCamera.aspect!=fittedAspect)) Fit();
        }

        public void Fit()
        {
            float viewHeight=viewCamera.orthographicSize*2,viewWidth=viewHeight*viewCamera.aspect;
            float aspect=(float)image.width/image.height;
            float width,height;
            if(viewWidth>=viewHeight*aspect) {width=viewWidth;height=viewWidth/aspect;}
            else {height=viewHeight;width=viewHeight*aspect;}
            // Anchor the image's top-left at the view's top-left (overflow goes right/down).
            transform.localPosition=new Vector3(-viewWidth/2+width/2,viewHeight/2-height/2,Distance);
            transform.localScale=new Vector3(width,height,1);
            fittedSize=viewCamera.orthographicSize;fittedAspect=viewCamera.aspect;
        }
    }
}
