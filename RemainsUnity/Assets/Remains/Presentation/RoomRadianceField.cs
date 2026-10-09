using System;
using Remains.RoomFormat;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Remains.Presentation
{
    // Lamp light of one room by radiance cascades (prototype, experiment after the reference tracer):
    // flatland transport on the GPU, same model as RoomConvert trace (direct light, no bounces yet).
    // The scene texture (walls + lamp disks) comes from RoomLightScene; Render() recomputes on
    // demand (scene change), not per frame. The result is a world-space fluence texture read by
    // WorldTerrain instead of URP lamp lights (_RemainsRadiance*, one room at a time for now).
    public sealed class RoomRadianceField : MonoBehaviour
    {
        private const string ComputeResource="Presentation/RadianceCascades";
        private ComputeShader compute;
        private Texture2D scene;
        private RenderTexture seedsA,seedsB,distance,cascadeA,cascadeB,fluenceRaw,fluence;
        private int width,height,levels;
        private Vector4 rect;
        private float texel;

        public static RoomRadianceField Build(RoomFile room,RoomDefinitionCatalog definitions,RoomLandCatalog lands,RoomLightingTable table,Vector2 origin,Transform parent)
        {
            if(!SystemInfo.supportsComputeShaders) throw new InvalidOperationException("Поле излучения: видеокарта/API без compute-шейдеров.");
            var compute=Resources.Load<ComputeShader>(ComputeResource)??throw new InvalidOperationException("Нет compute-шейдера "+ComputeResource);
            int resolution=table.RadianceResolution;
            var light=RoomLightScene.Build(room,definitions,lands,table,table.RadiancePower,table.RadianceLampSize);
            foreach(string issue in light.Issues) Debug.LogWarning(issue);
            var walls=RoomLightScene.Walls(room,RoomGeometryBuilder.Build(room,definitions),resolution);
            int w=walls.GetLength(0),h=walls.GetLength(1);

            var pixels=new Color[w*h];
            for(int y=0;y<h;y++)
                for(int x=0;x<w;x++)
                    if(walls[x,y]) pixels[y*w+x]=new Color(0,0,0,1);
            float t=1f/resolution;
            foreach(var lamp in light.Lamps)
            {
                double r=Math.Max(lamp.DiskRadius,t*0.75);
                for(int y=Mathf.FloorToInt((float)((lamp.Y-r)*resolution));y<=Mathf.CeilToInt((float)((lamp.Y+r)*resolution));y++)
                    for(int x=Mathf.FloorToInt((float)((lamp.X-r)*resolution));x<=Mathf.CeilToInt((float)((lamp.X+r)*resolution));x++)
                    {
                        if(x<0 || y<0 || x>=w || y>=h) continue;
                        double dx=(x+.5)*t-lamp.X,dy=(y+.5)*t-lamp.Y;
                        if(dx*dx+dy*dy>r*r) continue;
                        pixels[y*w+x]=new Color((float)lamp.Radiance[0],(float)lamp.Radiance[1],(float)lamp.Radiance[2],1);
                    }
            }
            var sceneTexture=new Texture2D(w,h,TextureFormat.RGBAHalf,false,true){name="Сцена света",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            sceneTexture.SetPixels(pixels);
            sceneTexture.Apply(false,false);

            var obj=new GameObject("Поле излучения: "+room.Template);
            if(parent!=null) obj.transform.SetParent(parent,false);
            var field=obj.AddComponent<RoomRadianceField>();
            field.compute=compute;
            field.scene=sceneTexture;
            field.width=w;field.height=h;field.texel=t;
            field.rect=new Vector4(origin.x,origin.y,w*t,h*t);
            // Top cascade: the ray interval reaches the room diagonal, (4^(levels) - 1) / 3 ≥ diagonal.
            float diagonal=Mathf.Sqrt(w*w+h*h);
            field.levels=1;
            while((Mathf.Pow(4,field.levels)-1)/3<diagonal) field.levels++;
            field.Allocate();
            Debug.Log($"Поле излучения {room.Template}: сетка {w}×{h}, каскадов {field.levels}, ламп {light.Lamps.Count}.");
            return field;
        }

        private void Allocate()
        {
            seedsA=Target(width,height,GraphicsFormat.R32G32_SFloat,"Ближайшая стена A");
            seedsB=Target(width,height,GraphicsFormat.R32G32_SFloat,"Ближайшая стена B");
            distance=Target(width,height,GraphicsFormat.R32_SFloat,"Расстояние до стены");
            int cw=2*width+(2<<levels),ch=2*height+(2<<levels);
            cascadeA=Target(cw,ch,GraphicsFormat.R16G16B16A16_SFloat,"Каскад A");
            cascadeB=Target(cw,ch,GraphicsFormat.R16G16B16A16_SFloat,"Каскад B");
            fluenceRaw=Target(width,height,GraphicsFormat.R16G16B16A16_SFloat,"Освещённость по пробам");
            fluence=Target(width,height,GraphicsFormat.R16G16B16A16_SFloat,"Освещённость");
            fluence.filterMode=FilterMode.Bilinear;
        }

        private static RenderTexture Target(int w,int h,GraphicsFormat format,string name)
        {
            var texture=new RenderTexture(w,h,0,format){name=name,enableRandomWrite=true,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            texture.Create();
            return texture;
        }

        public void Render()
        {
            int seed=compute.FindKernel("Seed"),jump=compute.FindKernel("Jump"),dist=compute.FindKernel("Distance"),cascade=compute.FindKernel("Cascade"),flu=compute.FindKernel("Fluence"),fill=compute.FindKernel("Fill");
            compute.SetInts("_Size",width,height);
            int gx=(width+7)/8,gy=(height+7)/8;

            compute.SetTexture(seed,"_Scene",scene);
            compute.SetTexture(seed,"_SeedsOut",seedsA);
            compute.Dispatch(seed,gx,gy,1);
            var source=seedsA;var target=seedsB;
            for(int step=Mathf.NextPowerOfTwo(Mathf.Max(width,height))/2;step>=1;step/=2)
            {
                compute.SetInt("_Step",step);
                compute.SetTexture(jump,"_SeedsIn",source);
                compute.SetTexture(jump,"_SeedsOut",target);
                compute.Dispatch(jump,gx,gy,1);
                (source,target)=(target,source);
            }
            compute.SetTexture(dist,"_SeedsFinal",source);
            compute.SetTexture(dist,"_DistanceOut",distance);
            compute.Dispatch(dist,gx,gy,1);

            compute.SetInt("_Top",levels-1);
            RenderTexture upper=cascadeB,current=cascadeA;
            for(int c=levels-1;c>=0;c--)
            {
                compute.SetInt("_Level",c);
                compute.SetTexture(cascade,"_Scene",scene);
                compute.SetTexture(cascade,"_Distance",distance);
                compute.SetTexture(cascade,"_Upper",upper);
                compute.SetTexture(cascade,"_Cascade",current);
                int spacing=1<<c,block=2<<c;
                int cw=(width+spacing-1)/spacing*block,ch=(height+spacing-1)/spacing*block;
                compute.Dispatch(cascade,(cw+7)/8,(ch+7)/8,1);
                (upper,current)=(current,upper);
            }
            compute.SetTexture(flu,"_Cascade0",upper);
            compute.SetTexture(flu,"_Fluence",fluenceRaw);
            compute.Dispatch(flu,gx,gy,1);
            compute.SetTexture(fill,"_Scene",scene);
            compute.SetTexture(fill,"_FluenceRaw",fluenceRaw);
            compute.SetTexture(fill,"_Fluence",fluence);
            compute.Dispatch(fill,gx,gy,1);

            Shader.SetGlobalTexture("_RemainsRadiance",fluence);
            Shader.SetGlobalVector("_RemainsRadianceRect",rect);
            Shader.SetGlobalFloat("_RemainsRadianceTexel",texel);
            Shader.SetGlobalFloat("_RemainsRadianceEnabled",1);
        }

        private void OnDestroy()
        {
            Shader.SetGlobalFloat("_RemainsRadianceEnabled",0);
            foreach(var texture in new[]{seedsA,seedsB,distance,cascadeA,cascadeB,fluenceRaw,fluence}) if(texture!=null) texture.Release();
            foreach(var obj in new UnityEngine.Object[]{seedsA,seedsB,distance,cascadeA,cascadeB,fluenceRaw,fluence,scene})
                if(obj!=null) {if(Application.isPlaying) Destroy(obj);else DestroyImmediate(obj);}
        }
    }
}
