using System;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class FurnitureLightChecks
    {
        const int Size=32;
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }

        public static void Run()
        {
            var compute=Resources.Load<ComputeShader>("Presentation/RadianceCascades");
            if(compute==null) throw new InvalidOperationException("Отсутствует compute-шейдер освещения мебели.");
            foreach(int mode in new[]{0,1})
            {
                float front=Evaluate(compute,mode,0);
                float wall=Evaluate(compute,mode,1);
                float floor=Evaluate(compute,mode,2);
                float corner=Evaluate(compute,mode,3);
                float partial=Evaluate(compute,mode,4);
                Require(front>.1f,$"Мебель потеряла доступный свет через собственный объём: режим={mode}, свет={front}.");
                Require(wall<.00001f && floor<.00001f && corner<.00001f && partial<.00001f,
                    $"Мебель заимствует свет через препятствие: режим={mode}, стена={wall}, пол={floor}, угол={corner}, отдельная балка={partial}.");
                Debug.Log($"Освещение мебели: PASS — режим={mode}, доступный свет={front}, стена={wall}, пол={floor}, угол={corner}, отдельная балка={partial}.");
            }
        }

        // Exercise Fill alone with controlled incident light, so wall leakage cannot
        // be hidden by the cascades, reflection history or renderer composition.
        static float Evaluate(ComputeShader compute,int mode,int barrier)
        {
            var scene=new Texture2D(Size,Size,TextureFormat.RGBAFloat,false,true);
            var raw=new Texture2D(Size,Size,TextureFormat.RGBAFloat,false,true);
            var output=new RenderTexture(Size,Size,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear)
                {enableRandomWrite=true};
            Texture2D read=null;
            var previous=RenderTexture.active;
            try
            {
                var scenePixels=new Color[Size*Size];
                var lightPixels=new Color[Size*Size];
                for(int y=14;y<=19;y++) for(int x=18;x<=23;x++) scenePixels[y*Size+x]=new Color(0,0,0,.5f);
                var source=barrier==2?new Vector2Int(20,8):barrier==3?new Vector2Int(12,8):new Vector2Int(12,16);
                lightPixels[source.y*Size+source.x]=new Color(1,.5f,.25f,1);
                if(barrier==1) for(int y=0;y<Size;y++) scenePixels[y*Size+16]=new Color(0,0,0,1);
                // A separate partly absorbing beam must not become part of the receiver's
                // own volume merely because neither object reaches the opaque-wall threshold.
                if(barrier==4) for(int y=0;y<Size;y++) scenePixels[y*Size+16]=new Color(0,0,0,.5f);
                if(barrier==2) for(int x=0;x<Size;x++) scenePixels[12*Size+x]=new Color(0,0,0,1);
                // The diagonal crosses a corner between (15,11) and (16,12).
                // This adjacent opaque texel must also block the borrowing path.
                if(barrier==3) scenePixels[11*Size+16]=new Color(0,0,0,1);
                scene.SetPixels(scenePixels);scene.Apply();raw.SetPixels(lightPixels);raw.Apply();output.Create();
                int kernel=compute.FindKernel("Fill");
                compute.SetInts("_Size",Size,Size);compute.SetInt("_ObjectGradient",mode);compute.SetFloat("_RimTexels",1);
                compute.SetTexture(kernel,"_Scene",scene);compute.SetTexture(kernel,"_FluenceRaw",raw);compute.SetTexture(kernel,"_Fluence",output);
                compute.Dispatch(kernel,Size/8,Size/8,1);
                RenderTexture.active=output;
                read=new Texture2D(Size,Size,TextureFormat.RGBAFloat,false,true);
                read.ReadPixels(new Rect(0,0,Size,Size),0,0);read.Apply();
                return read.GetPixel(20,16).r;
            }
            finally
            {
                RenderTexture.active=previous;
                if(read!=null) UnityEngine.Object.DestroyImmediate(read);
                output.Release();UnityEngine.Object.DestroyImmediate(output);
                UnityEngine.Object.DestroyImmediate(scene);UnityEngine.Object.DestroyImmediate(raw);
            }
        }

        static void Require(bool condition,string message)
        {if(!condition) throw new InvalidOperationException(message);}
    }
}
