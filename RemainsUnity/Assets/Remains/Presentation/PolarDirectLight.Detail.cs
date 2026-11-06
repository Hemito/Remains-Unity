using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Remains.Presentation
{
    public partial class PolarDirectLight
    {
        // Only maps used by the active camera. No per-level atlas and no readbacks.
        public const long DetailMapBudget=64L*1024*1024;
        private sealed class DetailMap {public RenderTexture Texture;public LinkedListNode<int> Node;}
        private readonly Dictionary<int,DetailMap> detailMaps=new();
        private readonly Dictionary<int,int> detailVersions=new();
        private readonly LinkedList<int> detailLru=new();
        public long DetailMapBytes {get;private set;}
        public int DetailMapBuilds {get;private set;}
        public int DetailDraws {get;private set;}
        public int DetailEpoch {get;private set;}
        public int DetailVersion(int index)=>detailVersions.TryGetValue(index,out int version)?version:0;
        private void InvalidateDetailMap(int index)
        {
            detailVersions[index]=DetailVersion(index)+1;
            if(!detailMaps.Remove(index,out var entry))return;
            detailLru.Remove(entry.Node);DetailMapBytes-=(long)entry.Texture.width*entry.Texture.height*entry.Texture.volumeDepth*4;
            LightTextureBudget.Release(entry.Texture);
        }
        private void ReleaseDetailMaps()
        {
            foreach(var entry in detailMaps.Values)LightTextureBudget.Release(entry.Texture);
            detailMaps.Clear();detailLru.Clear();DetailMapBytes=0;DetailEpoch++;
        }
        private Texture DetailMapFor(int index)
        {
            Configure(index);
            if(detailMaps.TryGetValue(index,out var entry))
            {detailLru.Remove(entry.Node);detailLru.AddLast(entry.Node);return entry.Texture;}
            var source=sources.All[index];int angles=Angles(source),rows=Rows(source);
            long bytes=(long)angles*rows*source.Samples.Length*4;
            if(bytes>DetailMapBudget)throw new InvalidOperationException($"Подробный свет: карта {source.Id} требует {bytes} байт, бюджет={DetailMapBudget}.");
            while(DetailMapBytes+bytes>DetailMapBudget)
            {
                int oldest=detailLru.First.Value;var old=detailMaps[oldest];detailLru.RemoveFirst();detailMaps.Remove(oldest);
                DetailMapBytes-=(long)old.Texture.width*old.Texture.height*old.Texture.volumeDepth*4;LightTextureBudget.Release(old.Texture);
            }
            var texture=Target(angles,rows,GraphicsFormat.R32_SFloat,"Подробный свет: карта "+source.Id,source.Samples.Length);
            compute.SetTexture(buildKernel,"_MapOut",texture);
            compute.Dispatch(buildKernel,(angles+63)/64,source.Samples.Length,1);
            compute.SetTexture(buildKernel,"_MapOut",map);
            entry=new DetailMap{Texture=texture,Node=detailLru.AddLast(index)};detailMaps.Add(index,entry);DetailMapBytes+=bytes;DetailMapBuilds++;
            return texture;
        }
        // operation: add, remove, subtract masked source, add unoccluded transient.
        public void DrawDetail(int index,RenderTexture target,Rect world,int operation=0,Texture mask=null,Rect? clip=null)
        {
            EnsureBuilt();if(index<0 || index>=sources.Count)throw new ArgumentOutOfRangeException(nameof(index));
            var source=sources.All[index];var prefix=DetailMapFor(index);
            DispatchDetail(source,prefix,samples,offsets[index],target,world,operation,mask,clip);
        }
        public void DrawDetailTransient(LevelLightSource source,RenderTexture front,RenderTexture back,Rect world,Texture mask)
        {
            EnsureBuilt();
            if(source.Samples.Length>DirectLight.MaximumSamples || source.Reach>maximumRadius)
                throw new ArgumentException("Подробная вспышка вне резерва: "+source.Id);
            int offset=transientSlot*DirectLight.MaximumSamples;transientSlot=(transientSlot+1)%TransientSlots;
            transientSamples.SetData(source.Samples,0,offset,source.Samples.Length);Configure(source,transientSamples,offset);
            compute.SetTexture(buildKernel,"_MapOut",map);compute.Dispatch(buildKernel,(Angles(source)+63)/64,source.Samples.Length,1);
            DispatchDetail(source,map,transientSamples,offset,front,world,0,mask,null,back);TransientBuilds++;
        }
        private void DispatchDetail(LevelLightSource source,Texture prefix,ComputeBuffer points,int offset,RenderTexture target,Rect world,int operation,Texture mask,Rect? limit,RenderTexture flashBack=null)
        {
            Configure(source,points,offset);int kernel=flashBack==null?detailKernel:detailFlashKernel;
            float sx=target.width/world.width,sy=target.height/world.height;
            var s=source.Texels;var bounds=new Rect(scene.WorldRect.x+s.x*scene.Texel,scene.WorldRect.y+s.y*scene.Texel,s.width*scene.Texel,s.height*scene.Texel);
            if(limit.HasValue){var r=limit.Value;bounds=Rect.MinMaxRect(Mathf.Max(bounds.xMin,r.xMin),Mathf.Max(bounds.yMin,r.yMin),Mathf.Min(bounds.xMax,r.xMax),Mathf.Min(bounds.yMax,r.yMax));}
            int x0=Mathf.Clamp(Mathf.FloorToInt((bounds.xMin-world.xMin)*sx),0,target.width),y0=Mathf.Clamp(Mathf.FloorToInt((bounds.yMin-world.yMin)*sy),0,target.height);
            int x1=Mathf.Clamp(Mathf.CeilToInt((bounds.xMax-world.xMin)*sx),0,target.width),y1=Mathf.Clamp(Mathf.CeilToInt((bounds.yMax-world.yMin)*sy),0,target.height);
            if(x1<=x0 || y1<=y0)return;
            compute.SetTexture(kernel,"_Scene",scene.Texture);compute.SetBuffer(kernel,"_Samples",points);compute.SetBuffer(kernel,"_MarchDistances",marchDistances);
            compute.SetTexture(kernel,"_Map",prefix);compute.SetTexture(kernel,"_DetailOut",target);compute.SetTexture(kernel,"_DetailMask",mask??Texture2D.blackTexture);
            if(flashBack!=null)compute.SetTexture(kernel,"_DetailBack",flashBack);
            compute.SetInts("_DetailSize",target.width,target.height);compute.SetInts("_DetailClip",x0,y0,x1,y1);
            compute.SetVector("_DetailRect",new Vector4(world.x,world.y,world.width,world.height));compute.SetInt("_DetailMode",operation);
            compute.Dispatch(kernel,(x1-x0+7)/8,(y1-y0+7)/8,1);DetailDraws++;
        }
    }
}
