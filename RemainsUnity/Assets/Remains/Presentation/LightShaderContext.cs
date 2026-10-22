using System;
using UnityEngine;
namespace Remains.Presentation
{
    // One window owns all globally published light resources. A prepared candidate has no publication rights.
    public sealed class LightShaderContext
    {
        private static LightShaderContext active;
        public bool IsActive=>ReferenceEquals(active,this);
        public void Activate() {active=this;}
        public void Publish(Action publish) {if(IsActive) publish();}
        public void Release()
        {
            if(!IsActive) return;
            active=null;
            Shader.SetGlobalFloat("_RemainsRadianceEnabled",0);
            Shader.SetGlobalFloat("_RemainsBackgroundRadianceEnabled",0);
            Shader.SetGlobalFloat("_RemainsEraseEnabled",0);
            Shader.SetGlobalVector("_RemainsAmbient",Vector4.zero);
        }
    }
}
