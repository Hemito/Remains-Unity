using System;
using System.Reflection;
using Remains.Presentation;
using Remains.RoomFormat;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Silhouettes shadow lamp light (RoomRadianceField.RenderTransient without flashes): a blocker between a
    // static emitter and a probe darkens the probe, moving it away restores the light, an unchanged silhouette set
    // does not recompute, an empty set restores the static field exactly.
    //   batch -executeMethod Remains.Editor.LampShadowChecks.RunBatch
    public static class LampShadowChecks
    {
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }

        public static void Run()
        {
            RoomRadianceField field=null;Texture2D mask=null;Sprite sprite=null;
            try
            {
                var room=new RoomFile {Template="remains:check:lamp-shadow",Width=8,Height=6,Cell=1};
                room.Options.Add("dark","0");room.Options.Add("color","");room.Options.Add("lon","0");
                var table=new RoomLightingTable {RadianceResolution=8,RadiancePower=1,RadianceLampSize=.2,
                    RadianceAlbedo=.35,ShadowEdge=.1,ObjectRimWidth=.1,ObjectLightMode="gradient"};
                field=RoomRadianceField.Build(room,new RoomDefinitionCatalog(),new RoomLandCatalog(),table,Vector2.zero,null);
                var scene=(Texture2D)Field(field,"baseScene");
                scene.SetPixel(8,24,new Color(4,4,4,1));scene.Apply();   // lamp at (1, 3) m
                field.UpdateOccluders(Array.Empty<(Rect,double)>());
                for(int i=0;i<8;i++) field.Render();
                var lampLight=Read(Shader.GetGlobalTexture("_RemainsRadiance"));
                float open=Mean(lampLight,64,40,44,22,26);   // probe at ~(5, 3) m
                Require(open>.01f,$"Лампа не освещает пробу: {open}.");

                mask=new Texture2D(4,4,TextureFormat.RGBA32,false,true){filterMode=FilterMode.Point};
                var pixels=new Color[16];for(int i=0;i<16;i++) pixels[i]=Color.white;mask.SetPixels(pixels);mask.Apply();
                sprite=Sprite.Create(mask,new Rect(0,0,4,4),new Vector2(.5f,.5f),4);
                var between=new[]{new RoomRadianceField.LightOccluder(sprite,Matrix4x4.TRS(new Vector3(3,3,0),Quaternion.identity,new Vector3(.5f,3,1)))};
                Require(field.RenderTransient(null,between),"Силуэт не вызвал расчёт света.");
                var shadowPixels=Read(Shader.GetGlobalTexture("_RemainsRadiance"));
                AssertNoAddedLight(lampLight,shadowPixels);
                float shadowed=Mean(shadowPixels,64,40,44,22,26);
                Require(shadowed<open*.5f,$"Силуэт не затеняет лампу: открыто={open}, в тени={shadowed}.");
                Require(!field.RenderTransient(null,between),"Неподвижный силуэт пересчитывает свет.");
                var away=new[]{new RoomRadianceField.LightOccluder(sprite,Matrix4x4.TRS(new Vector3(6.5f,.75f,0),Quaternion.identity,new Vector3(.5f,.5f,1)))};
                field.RenderTransient(null,away);
                var movedPixels=Read(Shader.GetGlobalTexture("_RemainsRadiance"));
                AssertNoAddedLight(lampLight,movedPixels);
                float moved=Mean(movedPixels,64,40,44,22,26);
                Require(moved>open*.85f,$"Тень осталась после ухода силуэта: открыто={open}, после={moved}.");
                Require(field.RenderTransient(null,null),"Пустой набор не восстановил статичный свет.");
                var restored=Read(Shader.GetGlobalTexture("_RemainsRadiance"));
                for(int i=0;i<restored.Length;i++)
                    if((restored[i]-lampLight[i]).maxColorComponent>1e-4f || (lampLight[i]-restored[i]).maxColorComponent>1e-4f)
                        throw new InvalidOperationException($"Статичный свет не восстановлен в текселе {i}.");
                Debug.Log($"Тени от ламп: PASS — открыто={open}, в тени={shadowed}, после ухода={moved}, неподвижный силуэт без пересчёта, восстановление точное.");
            }
            finally
            {
                if(field!=null) UnityEngine.Object.DestroyImmediate(field.gameObject);
                if(sprite!=null) UnityEngine.Object.DestroyImmediate(sprite);
                if(mask!=null) UnityEngine.Object.DestroyImmediate(mask);
            }
        }

        private static object Field(RoomRadianceField field,string name)
            =>typeof(RoomRadianceField).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(field);

        private static Color[] Read(Texture texture)
        {
            var target=(RenderTexture)texture;
            var previous=RenderTexture.active;RenderTexture.active=target;
            var copy=new Texture2D(target.width,target.height,TextureFormat.RGBAFloat,false,true);
            copy.ReadPixels(new Rect(0,0,target.width,target.height),0,0);copy.Apply();
            RenderTexture.active=previous;
            var pixels=copy.GetPixels();UnityEngine.Object.DestroyImmediate(copy);
            return pixels;
        }

        private static float Mean(Color[] pixels,int width,int x0,int x1,int y0,int y1)
        {
            float sum=0;int count=0;
            for(int y=y0;y<y1;y++) for(int x=x0;x<x1;x++) {sum+=pixels[y*width+x].r;count++;}
            return sum/count;
        }

        private static void Require(bool condition,string message) {if(!condition) throw new InvalidOperationException(message);}
        private static void AssertNoAddedLight(Color[] baseline,Color[] shadow)
        {
            for(int i=0;i<baseline.Length;i++)
                Require(shadow[i].r<=baseline[i].r+1e-4f && shadow[i].g<=baseline[i].g+1e-4f && shadow[i].b<=baseline[i].b+1e-4f,
                    $"Силуэт добавил свет в текселе {i}: исходно={baseline[i]}, с препятствием={shadow[i]}.");
        }
    }
}
