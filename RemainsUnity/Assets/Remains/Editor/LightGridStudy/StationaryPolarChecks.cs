using System;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Small deterministic GPU regressions; no benchmark, asset export, full factory or Player mutation.
    public static class StationaryPolarChecks
    {
        private const int Width=160,Height=120,Resolution=10;
        private static readonly Color Radiance=new(2,.8f,.3f);
        public static void RunBatch()
        {try{Debug.Log(Run());EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
        public static string Run()
        {
            string empty=Check(false),layered=Check(true);
            return "PASS StationaryPolarChecks: "+empty+"; "+layered+".";
        }
        private static string Check(bool layered)
        {
            var walls=new float[Width,Height];var absorption=new float[Width*Height];
            if(layered)
            {
                // Eight emitting points begin inside the same connected wall attachment.
                for(int y=12;y<100;y++)for(int x=32;x<36;x++)walls[x,y]=1;
                // Lower rays lose light in furniture; upper rays see through the empty region above it.
                for(int y=0;y<56;y++)for(int x=61;x<67;x++)walls[x,y]=.25f;
                for(int y=80;y<Height;y++)for(int x=82;x<87;x++)walls[x,y]=1;
            }
            for(int y=0;y<Height;y++)for(int x=0;x<Width;x++)absorption[y*Width+x]=walls[x,y];
            using var scene=new LevelLightScene(walls,Vector2.zero,Resolution,Array.Empty<LevelLightScene.ObjectSprite>());
            var points=new Vector4[8];for(int i=0;i<points.Length;i++)points[i]=new Vector4(3.35f,1.45f+i*1.2f,i+1,0);
            string id=layered?"eight-layered":"eight-empty";
            var source=new LevelLightSource(id,LevelLightSourceKind.Lamp,LevelLightSources.Normalise(points,id),Radiance,.9f,12,
                new Rect(3.2f,1.2f,.4f,8.8f),new RectInt(0,0,Width,Height),true);
            var sources=new LevelLightSources(new[]{source},Width,Height);
            using var polar=new StationaryPolarLight(scene,sources);polar.BuildAll();
            var map=polar.ValidateMapAgainstCpu(id,absorption,37);
            if(map.ShapeSamples!=8 || map.Samples<=0 || layered && (map.PartialSamples==0 || map.BlockedSamples==0))
                throw new InvalidOperationException($"Проверка восьми слоёв {id}: недостаточно контрольных префиксов {map.Samples}, partial={map.PartialSamples}, blocked={map.BlockedSamples}, shape={map.ShapeSamples}.");
            if(!polar.TryGetField(id,out var texture,out _))throw new InvalidOperationException("Нет готового контрольного полярного поля "+id);
            var observed=PolarShadowField.Read((RenderTexture)texture);
            using var direct=new DirectLight(scene,sources,8*1024*1024);direct.BuildAll();direct.WaitForMeasurements();
            var exact=PolarShadowField.Read((RenderTexture)direct.Sum);
            int checkedPixels=0,mixedPixels=0;float maximumEnergyError=0,maximumFieldError=0;
            for(int y=3;y<Height-3;y+=layered?2:1)
            for(int x=3;x<Width-3;x+=layered?2:1)
            {
                int pixel=y*Width+x;if(walls[x,y]>=.99f)continue;
                var target=new Vector2((x+.5f)/Resolution,(y+.5f)/Resolution);
                bool mixed=false;
                if(layered && !StableDirections(source,absorption,target,out mixed))continue;
                float tolerance=Math.Max(.002f,Math.Max(exact[pixel].r,Math.Max(exact[pixel].g,exact[pixel].b))*.0015f);
                float difference=Difference(observed[pixel],exact[pixel]);maximumFieldError=Math.Max(maximumFieldError,difference);
                if(!Finite(observed[pixel]) || difference>tolerance)
                    throw new InvalidOperationException($"Полярное поле {id}: точка={target}, GPU={observed[pixel]}, production={exact[pixel]}, разница={difference}, допуск={tolerance}.");
                if(!layered)
                {
                    Color expected=EmptyEnergy(source,target);float error=Difference(observed[pixel],expected);
                    maximumEnergyError=Math.Max(maximumEnergyError,error);
                    if(error>Math.Max(.004f,Math.Max(expected.r,Math.Max(expected.g,expected.b))*.002f))
                        throw new InvalidOperationException($"Энергия восьми точек: точка={target}, GPU={observed[pixel]}, CPU={expected}, ошибка={error}.");
                }
                checkedPixels++;if(mixed)mixedPixels++;
            }
            if(checkedPixels<(layered?50:10000) || layered && mixedPixels<8)
                throw new InvalidOperationException($"Проверка восьми слоёв {id}: мало устойчивых приёмников {checkedPixels}, с разным пропусканием слоёв={mixedPixels}.");
            return $"{id}: {checkedPixels}пикселей, mixed={mixedPixels}, {map.Samples}префиксов/{map.ShapeSamples}слоёв, partial={map.PartialSamples}, map-error={map.MaximumError}, field-error={maximumFieldError}, energy-error={maximumEnergyError}";
        }
        private static bool StableDirections(LevelLightSource source,float[] scene,Vector2 target,out bool mixed)
        {
            mixed=false;int angles=(int)Math.Ceiling(2*Math.PI*source.Reach/StationaryPolarLight.OuterPitch);
            float filterWidth=StationaryPolarLight.OuterFilterWidth*angles/(2*Mathf.PI*source.Reach);
            float minimum=1,maximum=0;bool anyInRange=false;
            foreach(var point in source.Samples)
            {
                var from=new Vector2(point.x,point.y);Vector2 delta=target-from;float distance=delta.magnitude;
                if(distance>=source.Reach || distance<.05f)continue;
                anyInRange=true;float exact=Transmission(source,scene,from,delta/distance,distance);
                minimum=Math.Min(minimum,exact);maximum=Math.Max(maximum,exact);
                float phase=(Mathf.Atan2(delta.y,delta.x)/(2*Mathf.PI)+1)%1*angles;
                // Also check the neighbouring bins: no dependence on atan rounding or a near-edge march sample.
                int first=Mathf.FloorToInt(phase-filterWidth*.5f)-1,last=Mathf.FloorToInt(phase+filterWidth*.5f)+1;
                for(int bin=first;bin<=last;bin++)
                {
                    int wrapped=(bin%angles+angles)%angles;float a=(wrapped+.5f)*2*Mathf.PI/angles;
                    float polar=Transmission(source,scene,from,new Vector2(Mathf.Cos(a),Mathf.Sin(a)),distance);
                    if(Mathf.Abs(exact-polar)>1e-5f)return false;
                }
            }
            mixed=maximum-minimum>.1f;return anyInRange;
        }
        private static float Transmission(LevelLightSource source,float[] scene,Vector2 from,Vector2 direction,float distance)
        {
            float finish=Math.Max(0,distance-.6f/Resolution),transmission=1,step=.75f/Resolution;bool leaving=true;
            for(float at=step*.5f;at<finish;at+=step)
            {
                Vector2 world=from+direction*at;int x=Mathf.FloorToInt(world.x*Resolution),y=Mathf.FloorToInt(world.y*Resolution);
                if(x<0 || y<0 || x>=Width || y>=Height)return 0;
                float alpha=scene[y*Width+x];bool emitter=world.x>=source.Bounds.xMin && world.x<=source.Bounds.xMax && world.y>=source.Bounds.yMin && world.y<=source.Bounds.yMax;
                if(leaving && emitter && alpha>=.99f)continue;leaving=false;
                if(alpha>=.99f)return 0;
                if(alpha>.0001f)transmission*=Mathf.Pow(1-alpha,step*Resolution);
                if(transmission<.0001f)return 0;
            }
            return transmission;
        }
        private static Color EmptyEnergy(LevelLightSource source,Vector2 target)
        {
            float value=0;
            foreach(var point in source.Samples)
            {
                float distance=Vector2.Distance(target,new Vector2(point.x,point.y));
                float edge=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(source.Reach*.9f,source.Reach,distance));
                value+=2*Mathf.Atan(point.z*source.Width/(2*Math.Max(distance,.05f)))*edge;
            }
            return new Color(Radiance.r*value,Radiance.g*value,Radiance.b*value,1);
        }
        private static float Difference(Color a,Color b)=>Math.Max(Math.Abs(a.r-b.r),Math.Max(Math.Abs(a.g-b.g),Math.Abs(a.b-b.b)));
        private static bool Finite(Color c)=>float.IsFinite(c.r) && float.IsFinite(c.g) && float.IsFinite(c.b);
    }
}
