using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace Remains.Runtime
{
    // Render the existing client camera and live objects, without rebuilding a scene.
    public static class NetworkFrameCapture
    {
        public static void Save(Camera camera,string path)
        {
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)
                throw new InvalidOperationException("Съёмка сетевого кадра требует графического устройства; уберите -nographics.");
            var previousTarget=camera.targetTexture;
            var previousActive=RenderTexture.active;
            var target=new RenderTexture(Screen.width,Screen.height,24);
            var image=new Texture2D(Screen.width,Screen.height,TextureFormat.RGBA32,false);
            try
            {
                target.Create();camera.targetTexture=target;
                camera.Render();RenderTexture.active=target;
                image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();
                File.WriteAllBytes(path,image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture=previousTarget;RenderTexture.active=previousActive;
                target.Release();UnityEngine.Object.Destroy(target);UnityEngine.Object.Destroy(image);
            }
        }
    }
}
