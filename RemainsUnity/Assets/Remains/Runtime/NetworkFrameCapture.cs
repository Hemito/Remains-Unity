using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace Remains.Runtime
{
    // Render the existing client camera and live objects, without rebuilding a scene.
    public static class NetworkFrameCapture
    {
        public static void Save(Camera camera,string path,Canvas canvas=null,int width=0,int height=0)
        {
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)
                throw new InvalidOperationException("Съёмка сетевого кадра требует графического устройства; уберите -nographics.");
            var previousTarget=camera.targetTexture;
            var previousActive=RenderTexture.active;
            var previousMode=canvas!=null?canvas.renderMode:RenderMode.ScreenSpaceOverlay;
            var previousCamera=canvas!=null?canvas.worldCamera:null;
            float previousDistance=canvas!=null?canvas.planeDistance:0;
            int previousOrder=canvas!=null?canvas.sortingOrder:0;
            if(width==0 && height==0) {width=Screen.width;height=Screen.height;}
            if(width<=0 || height<=0) throw new ArgumentOutOfRangeException(nameof(width),"Задайте обе положительные стороны кадра.");
            var target=new RenderTexture(width,height,24);
            var image=new Texture2D(width,height,TextureFormat.RGBA32,false);
            try
            {
                target.Create();camera.targetTexture=target;
                if(canvas!=null)
                {
                    canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
                    canvas.sortingOrder=short.MaxValue;
                    Canvas.ForceUpdateCanvases();
                }
                SortingGroup.UpdateAllSortingGroups();
                camera.Render();RenderTexture.active=target;
                image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();
                File.WriteAllBytes(path,image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture=previousTarget;RenderTexture.active=previousActive;
                if(canvas!=null)
                {
                    canvas.renderMode=previousMode;canvas.worldCamera=previousCamera;canvas.planeDistance=previousDistance;
                    canvas.sortingOrder=previousOrder;
                    Canvas.ForceUpdateCanvases();
                }
                target.Release();
                if(Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(target);UnityEngine.Object.Destroy(image);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);
                }
            }
        }
    }
}
