using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Remains.Presentation
{
    // Detailed static direct is independent of moving actor masks and one-frame flashes.
    internal sealed class PolarLightWindow : IDisposable
    {
        private readonly PolarDirectLight direct;
        private readonly List<int> indices=new();
        private readonly List<int> versions=new();
        private readonly List<bool> enabled=new();
        private RectInt area;
        private int epoch;
        private long revision=-1;
        public RenderTexture Sum {get;private set;}
        public int Builds {get;private set;}
        public int SourceUpdates {get;private set;}
        public PolarLightWindow(PolarDirectLight direct){this.direct=direct;}
        public void Prepare(RectInt next,Rect world,int width,int height)
        {
            bool moved=Sum==null || area!=next || Sum.width!=width || Sum.height!=height;
            if(!moved && revision==direct.Revision)return;
            bool rebuild=moved || epoch!=direct.DetailEpoch;
            if(moved)
            {
                LightTextureBudget.Release(Sum);Sum=null;
                var texture=new RenderTexture(width,height,0,GraphicsFormat.R32G32B32A32_SFloat){name="Подробный свет: сумма камеры",enableRandomWrite=true};
                try{LightTextureBudget.Track(texture,(long)width*height*16);if(!texture.Create())throw new InvalidOperationException("Не создана сумма подробного света камеры.");Sum=texture;}
                catch{LightTextureBudget.Release(texture);throw;}
                area=next;direct.Sources.Overlapping(area,indices);versions.Clear();enabled.Clear();
                foreach(int index in indices){versions.Add(direct.DetailVersion(index));enabled.Add(false);}
            }
            for(int i=0;i<indices.Count && !rebuild;i++)if(versions[i]!=direct.DetailVersion(indices[i]))rebuild=true;
            if(rebuild)
            {
                var previous=RenderTexture.active;
                try{RenderTexture.active=Sum;GL.Clear(false,true,Color.clear);}finally{RenderTexture.active=previous;}
                for(int i=0;i<indices.Count;i++)
                {
                    int index=indices[i];bool on=direct.Sources.All[index].Enabled;
                    if(on)direct.DrawDetail(index,Sum,world);
                    versions[i]=direct.DetailVersion(index);enabled[i]=on;
                }
                Builds++;
            }
            else for(int i=0;i<indices.Count;i++)
            {
                int index=indices[i];bool on=direct.Sources.All[index].Enabled;
                if(on==enabled[i])continue;
                direct.DrawDetail(index,Sum,world,on?0:1);enabled[i]=on;SourceUpdates++;
            }
            epoch=direct.DetailEpoch;revision=direct.Revision;
        }
        public void Dispose(){LightTextureBudget.Release(Sum);Sum=null;}
    }
}
