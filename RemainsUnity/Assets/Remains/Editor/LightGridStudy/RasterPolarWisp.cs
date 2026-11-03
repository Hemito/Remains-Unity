using System;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    // Editor-only moving point light over the SAME fine absorption scene as the factory's static light.
    // One GPU thread per angle marches once; the dense radial prefix retains partial absorption at every step.
    // Neither movement nor rendering reads GPU data back. The caller explicitly rebuilds after movement/doors.
    internal sealed class RasterPolarWisp : IDisposable
    {
        internal const float OuterPitch=.05f;
        internal const float OuterFilterWidth=.05f;
        private const string ShaderPath="Assets/Remains/Editor/LightGridStudy/RasterPolarWisp.compute";
        private readonly LevelLightScene scene;
        private readonly ComputeShader compute;
        private readonly ComputeBuffer marchDistances;
        private readonly int buildKernel,renderKernel,exactKernel;
        private readonly float maximumRadius;
        private readonly int maximumAngles;
        private readonly float marchStep;
        private bool sourceSet,mapDirty=true,disposed;
        private long mapSceneRevision=-1;

        internal RenderTexture Texture {get;private set;}
        internal RenderTexture Map {get;private set;}
        internal RenderTexture NearestOpaque {get;private set;}
        internal Rect WorldRect {get;}
        internal Vector2 SourcePosition {get;private set;}
        internal float Radius {get;private set;}
        internal int Angles {get;private set;}
        internal int Rows {get;}
        internal int Builds {get;private set;}
        // Includes all owned texture reservations; excludes the borrowed LevelLightScene and driver overhead.
        internal long GpuBytes=>disposed?0:(long)maximumAngles*(Rows+1)*4+Rows*4L+(long)Texture.width*Texture.height*8;

        internal RasterPolarWisp(LevelLightScene scene,Rect worldRect,int outputWidth,int outputHeight,float maximumRadius=8f)
        {
            this.scene=scene??throw new ArgumentNullException(nameof(scene));
            if(!Finite(worldRect.x) || !Finite(worldRect.y) || !Finite(worldRect.width) || !Finite(worldRect.height) || worldRect.width<=0 || worldRect.height<=0)
                throw new ArgumentException("Полярный огонёк: неверное окно света "+worldRect,nameof(worldRect));
            if(outputWidth<=0 || outputHeight<=0) throw new ArgumentOutOfRangeException(nameof(outputWidth));
            if(!Finite(maximumRadius) || maximumRadius<OuterPitch) throw new ArgumentOutOfRangeException(nameof(maximumRadius));
            this.maximumRadius=maximumRadius;WorldRect=worldRect;
            marchStep=.75f/scene.Resolution;
            maximumAngles=(int)Math.Ceiling(2*Math.PI*maximumRadius/OuterPitch);
            Rows=(int)Math.Ceiling(maximumRadius/marchStep)+1;
            if(maximumAngles>SystemInfo.maxTextureSize || Rows>SystemInfo.maxTextureSize || outputWidth>SystemInfo.maxTextureSize || outputHeight>SystemInfo.maxTextureSize)
                throw new ArgumentException($"Полярный огонёк: резерв {maximumAngles}×{Rows}, кадр {outputWidth}×{outputHeight}, предел текстуры {SystemInfo.maxTextureSize}.");
            try
            {
                if(!SystemInfo.supportsComputeShaders) throw new InvalidOperationException("Полярный огонёк: compute-шейдеры не поддерживаются.");
                var asset=AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath);
                if(asset==null) throw new InvalidOperationException("Полярный огонёк: нет Editor-шейдера "+ShaderPath);
                compute=Object.Instantiate(asset);
                buildKernel=compute.FindKernel("BuildMap");renderKernel=compute.FindKernel("Render");exactKernel=compute.FindKernel("RenderExact");
                var distances=new float[Rows];float at=marchStep*.5f;
                for(int row=0;row<Rows;row++,at+=marchStep) distances[row]=at;
                marchDistances=new ComputeBuffer(Rows,4);marchDistances.SetData(distances);
                Map=Target(maximumAngles,Rows,GraphicsFormat.R32_SFloat,"Огонёк: полярные префиксы пропускания");
                NearestOpaque=Target(maximumAngles,1,GraphicsFormat.R32_SFloat,"Огонёк: ближайшая непрозрачная проба");
                Texture=Target(outputWidth,outputHeight,GraphicsFormat.R16G16B16A16_SFloat,"Огонёк: прямой свет окна");
                compute.SetInts("_SceneSize",scene.Width,scene.Height);
                compute.SetInts("_OutputSize",outputWidth,outputHeight);
                compute.SetVector("_SceneRect",new Vector4(scene.WorldRect.x,scene.WorldRect.y,scene.WorldRect.width,scene.WorldRect.height));
                compute.SetVector("_OutputRect",new Vector4(worldRect.x,worldRect.y,worldRect.width,worldRect.height));
                compute.SetFloat("_PPU",scene.Resolution);compute.SetFloat("_MarchStep",marchStep);
                compute.SetInt("_RadialSamples",Rows);
                foreach(int kernel in new[]{buildKernel,renderKernel,exactKernel}) compute.SetTexture(kernel,"_Scene",scene.Texture);
                compute.SetTexture(buildKernel,"_MapOut",Map);compute.SetTexture(buildKernel,"_OpaqueOut",NearestOpaque);
                compute.SetBuffer(buildKernel,"_MarchDistances",marchDistances);compute.SetBuffer(renderKernel,"_MarchDistances",marchDistances);
                compute.SetTexture(renderKernel,"_Map",Map);compute.SetTexture(renderKernel,"_LightOut",Texture);
                compute.SetTexture(exactKernel,"_LightOut",Texture);
            }
            catch {Dispose();throw;}
        }

        internal void SetSource(Vector2 position,Color color,float radius=8f,float projectedWidth=.2f,float sourcePower=12f)
        {
            ThrowIfDisposed();
            if(!Finite(position.x) || !Finite(position.y) || !scene.WorldRect.Contains(position))
                throw new ArgumentException("Полярный огонёк: источник вне сцены поглощения: "+position,nameof(position));
            if(!Finite(radius) || radius<OuterPitch || radius>maximumRadius) throw new ArgumentOutOfRangeException(nameof(radius),$"Радиус огонька {radius}, допустимо {OuterPitch}…{maximumRadius} м.");
            if(!Finite(projectedWidth) || projectedWidth<=0) throw new ArgumentOutOfRangeException(nameof(projectedWidth));
            if(!Finite(sourcePower) || sourcePower<0) throw new ArgumentOutOfRangeException(nameof(sourcePower));
            if(!Finite(color.r) || !Finite(color.g) || !Finite(color.b) || color.r<0 || color.g<0 || color.b<0)
                throw new ArgumentException("Полярный огонёк: неверный цвет источника "+color,nameof(color));
            mapDirty|=!sourceSet || SourcePosition.x!=position.x || SourcePosition.y!=position.y || Radius!=radius;
            SourcePosition=position;Radius=radius;
            Angles=(int)Math.Ceiling(2*Math.PI*radius/OuterPitch);sourceSet=true;
            compute.SetVector("_Source",new Vector4(position.x,position.y,0,0));
            compute.SetVector("_SourceColor",new Vector4(color.r,color.g,color.b,1));
            compute.SetFloat("_Radius",radius);compute.SetFloat("_ProjectedWidth",projectedWidth);compute.SetFloat("_SourcePower",sourcePower);
            compute.SetInt("_Angles",Angles);
        }

        internal void BuildMap()
        {
            RequireSource();
            compute.Dispatch(buildKernel,(Angles+63)/64,1,1);
            mapSceneRevision=scene.Revision;mapDirty=false;Builds++;
        }

        internal void Render()
        {
            RequireMap();
            compute.Dispatch(renderKernel,(Texture.width+7)/8,(Texture.height+7)/8,1);
        }

        // Explicit offline reference; overwrites Texture. Call Render again to restore the polar result.
        internal void RenderExactForValidation()
        {
            RequireSource();
            compute.Dispatch(exactKernel,(Texture.width+7)/8,(Texture.height+7)/8,1);
        }
        internal Color[] ReadFalloffForValidation()
        {
            RequireSource();int kernel=compute.FindKernel("ValidateFalloff");
            var target=Target(Texture.width,Texture.height,GraphicsFormat.R32G32B32A32_SFloat,"Проверка затухания: раздельные мощность/вес/результат");
            try
            {
                compute.SetTexture(kernel,"_FalloffDiagnostic",target);compute.Dispatch(kernel,(Texture.width+7)/8,(Texture.height+7)/8,1);
                return PolarShadowField.Read(target);
            }
            finally{Object.DestroyImmediate(target);}
        }

        internal readonly struct MapValidation
        {
            internal readonly int Samples,PartialSamples,BlockedSamples;
            internal readonly float MaximumError;
            internal MapValidation(int samples,int partial,int blocked,float error) {Samples=samples;PartialSamples=partial;BlockedSamples=blocked;MaximumError=error;}
        }

        // BLOCKING diagnostics only, outside every benchmark/frame loop. Input is row-major fine absorption.
        // Samples all radial prefixes of selected angles, including the transmission behind partial absorbers.
        internal MapValidation ValidateMapAgainstCpu(float[] absorption,int angleStride=1,float tolerance=.002f)
        {
            RequireMap();
            if(absorption==null || absorption.Length!=scene.Width*scene.Height) throw new ArgumentException("Проверка огонька: неверный размер fine-поглощения.",nameof(absorption));
            if(angleStride<1 || !Finite(tolerance) || tolerance<=0) throw new ArgumentOutOfRangeException(nameof(angleStride));
            var actual=ReadFloatsForValidation(Map);
            int samples=0,partial=0,blocked=0;float maximumError=0;
            for(int angle=0;angle<Angles;angle+=angleStride)
            {
                float a=(angle+.5f)*(2*Mathf.PI)/Angles;
                float dx=Mathf.Cos(a),dy=Mathf.Sin(a),transmission=1,at=marchStep*.5f;
                bool stopped=false;
                for(int radial=0;radial<Rows;radial++,at+=marchStep)
                {
                    if(!stopped)
                    {
                        int x=Mathf.FloorToInt((SourcePosition.x+dx*at-scene.WorldRect.x)*scene.Resolution);
                        int y=Mathf.FloorToInt((SourcePosition.y+dy*at-scene.WorldRect.y)*scene.Resolution);
                        if(x<0 || y<0 || x>=scene.Width || y>=scene.Height) {transmission=0;stopped=true;}
                        else
                        {
                            float alpha=Mathf.Clamp01(absorption[y*scene.Width+x]);
                            if(alpha>=.99f) {transmission=0;stopped=true;}
                            else if(alpha>.0001f) transmission*=Mathf.Pow(Mathf.Max(0,1-alpha),marchStep*scene.Resolution);
                            if(transmission<.0001f) transmission=0;
                        }
                    }
                    float observed=actual[radial*maximumAngles+angle],error=Mathf.Abs(observed-transmission);
                    if(!Finite(observed) || error>tolerance)
                        throw new InvalidOperationException($"Полярный огонёк: несовпадение префикса, источник={SourcePosition}, угол={angle}/{Angles}, ряд={radial}/{Rows}, расстояние={at}, GPU={observed}, CPU={transmission}, ошибка={error}, допуск={tolerance}, ревизия={mapSceneRevision}.");
                    maximumError=Mathf.Max(maximumError,error);samples++;
                    if(transmission==0) blocked++;else if(transmission<.9999f) partial++;
                }
            }
            return new MapValidation(samples,partial,blocked,maximumError);
        }

        internal static float[] ReadFloatsForValidation(UnityEngine.Texture texture)
        {
            if(texture==null) throw new ArgumentNullException(nameof(texture));
            var request=AsyncGPUReadback.Request(texture,0,TextureFormat.RFloat);request.WaitForCompletion();
            if(request.hasError) throw new InvalidOperationException("Полярный огонёк: ошибка контрольного чтения GPU: "+texture.name);
            return request.GetData<float>().ToArray();
        }

        // CPU direct ray for offline point comparisons. Unlike Map validation this follows the target's true angle.
        internal float DirectTransmissionForValidation(float[] absorption,Vector2 target)
        {
            RequireSource();
            if(absorption==null || absorption.Length!=scene.Width*scene.Height) throw new ArgumentException("Проверка огонька: неверный размер fine-поглощения.",nameof(absorption));
            Vector2 delta=target-SourcePosition;float length=delta.magnitude;
            if(length<.5f/scene.Resolution) return 1;
            Vector2 direction=delta/length;float finish=Mathf.Max(0,length-.6f/scene.Resolution),transmission=1;
            for(float at=marchStep*.5f;at<finish;at+=marchStep)
            {
                Vector2 world=SourcePosition+direction*at;
                int x=Mathf.FloorToInt((world.x-scene.WorldRect.x)*scene.Resolution),y=Mathf.FloorToInt((world.y-scene.WorldRect.y)*scene.Resolution);
                if(x<0 || y<0 || x>=scene.Width || y>=scene.Height) return 0;
                float alpha=Mathf.Clamp01(absorption[y*scene.Width+x]);
                if(alpha>=.99f) return 0;
                if(alpha>.0001f) transmission*=Mathf.Pow(Mathf.Max(0,1-alpha),marchStep*scene.Resolution);
                if(transmission<.0001f) return 0;
            }
            return transmission;
        }

        private static RenderTexture Target(int width,int height,GraphicsFormat format,string name)
        {
            var target=new RenderTexture(width,height,0,format) {name=name,enableRandomWrite=true,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            if(!target.Create()) {Object.DestroyImmediate(target);throw new InvalidOperationException("Полярный огонёк: не создана текстура "+name);}
            return target;
        }
        private static bool Finite(float value)=>float.IsFinite(value);
        private void ThrowIfDisposed() {if(disposed) throw new ObjectDisposedException(nameof(RasterPolarWisp));}
        private void RequireSource() {ThrowIfDisposed();if(!sourceSet) throw new InvalidOperationException("Полярный огонёк: сначала задайте источник SetSource.");}
        private void RequireMap()
        {
            RequireSource();
            if(mapDirty || mapSceneRevision!=scene.Revision) throw new InvalidOperationException($"Полярный огонёк: требуется BuildMap; источник={SourcePosition}, сцена={scene.Revision}, карта={mapSceneRevision}, позиция/радиус изменены={mapDirty}.");
        }
        public void Dispose()
        {
            if(disposed) return;disposed=true;
            foreach(var target in new[]{Map,NearestOpaque,Texture}) if(target!=null) {target.Release();Object.DestroyImmediate(target);}
            Map=NearestOpaque=Texture=null;
            marchDistances?.Dispose();
            if(compute!=null) Object.DestroyImmediate(compute);
        }
    }
}
