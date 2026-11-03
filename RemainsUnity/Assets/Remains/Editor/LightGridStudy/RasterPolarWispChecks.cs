using System;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class RasterPolarWispChecks
    {
        public static void RunBatch()
        {try{Debug.Log(Run());EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
        public static string Run()
        {
            using var scene=new LevelLightScene(new float[200,200],Vector2.zero,10,Array.Empty<LevelLightScene.ObjectSprite>());
            var source=new Vector2(10,10);var rect=new Rect(10,9.95f,8.5f,.1f);
            using var light=new RasterPolarWisp(scene,rect,512,8);
            light.SetSource(source,Color.white);light.BuildMap();light.Render();
            var values=RasterPolarWisp.ReadFloatsForValidation(light.Texture);var diagnostic=light.ReadFalloffForValidation();
            float maximum=0,maximumWeightError=0,maximumBaseError=0,previous=float.PositiveInfinity;int outside=0;
            for(int x=0;x<512;x++)
            {
                float px=rect.x+(x+.5f)/512*rect.width,py=rect.y+4.5f/8*rect.height,d=Vector2.Distance(new Vector2(px,py),source);
                float window=Mathf.Max(0,1-d*d/64),reference=1-1f/64;
                var gpu=diagnostic[4*512+x];float expectedWeight=window*window/(reference*reference);
                float weightError=Mathf.Abs(gpu.g-expectedWeight);maximumWeightError=Mathf.Max(maximumWeightError,weightError);
                if(weightError>2e-6f)throw new InvalidOperationException($"Кривая затухания отличается от CPU: d={d}, GPU={gpu.g}, CPU={expectedWeight}.");
                // SM5 atan is an approximate intrinsic. Isolate its unchanged base from the NEW polynomial,
                // rather than silently widening the original renderer tolerance when their errors add up.
                maximumBaseError=Mathf.Max(maximumBaseError,Mathf.Abs(gpu.r-12*2*Mathf.Atan(.2f/(2*Mathf.Max(d,.05f)))));
                float expected=gpu.r*expectedWeight,actual=values[4*512+x];
                float error=Mathf.Abs(expected-actual);maximum=Mathf.Max(maximum,error);
                if(!float.IsFinite(actual) || error>Mathf.Max(.002f,expected*.001f))
                    throw new InvalidOperationException($"Затухание виспа: d={d}, GPU={actual}, ожидается={expected}, ошибка={error}.");
                if(actual>previous+.002f)throw new InvalidOperationException("Яркость виспа растёт с расстоянием: "+d);previous=actual;
                if(d>=8){outside++;if(actual!=0)throw new InvalidOperationException("Свет за техническим радиусом: "+actual);}
                if(d>7.2f && actual>.015f)throw new InvalidOperationException("Слишком яркий хвост у радиуса: "+actual);
            }
            if(outside<20)throw new InvalidOperationException("Не проверен участок за радиусом виспа.");
            return $"PASS RasterPolarWisp: 512точек плавного монотонного затухания, яркость на1м сохранена формулой, хвост у90%радиуса тусклый, за радиусом0; максимум half-ошибки{maximum}, ошибка нового веса{maximumWeightError}, отличие прежней atan-базы GPU/CPU{maximumBaseError}.";
        }
    }
}
