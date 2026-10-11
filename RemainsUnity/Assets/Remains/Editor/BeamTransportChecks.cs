using System;
using System.Reflection;
using Remains.Presentation;
using Remains.RoomFormat;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Read the raw transport buffer: neither object Fill nor bounce may hide beam leakage.
    public static class BeamTransportChecks
    {
        public static void RunBatch()
        {
            try { Run(); LampShadowChecks.Run(); EditorApplication.Exit(0); }
            catch(Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        public static void Run()
        {
            var open=Sample(1);
            var opaque=Sample(0);
            var beam=Sample(.05);
            float maxOpaque=0,maxBeam=0,maxOpen=0;
            for(int i=0;i<open.Length;i++)
            {
                maxOpen=Mathf.Max(maxOpen,open[i]);
                if(open[i]<.001f) continue;
                maxOpaque=Mathf.Max(maxOpaque,opaque[i]/open[i]);
                maxBeam=Mathf.Max(maxBeam,beam[i]/open[i]);
            }
            Debug.Log($"Перенос света через длинную балку без Fill/отражений: непрозрачная/open={maxOpaque}, 5%/open={maxBeam}.");
            if(maxOpen<=.001f || maxBeam<=0 || maxOpaque>.01f || maxBeam>.08f)
                throw new InvalidOperationException($"Ошибка переноса света: максимум open={maxOpen}, непрозрачная/open={maxOpaque}, 5%/open={maxBeam}; источник должен светить, балка — пропускать ненулевой свет, не более 1% и 8% соответственно.");
        }

        static float[] Sample(double transmission)
        {
            RoomRadianceField field=null; Texture2D copy=null;
            var previous=RenderTexture.active;
            try
            {
                var room=new RoomFile{Template="remains:check:beam-transport",Width=16,Height=12,Cell=1};
                room.Options.Add("dark","0");room.Options.Add("color","");room.Options.Add("lon","0");
                var table=new RoomLightingTable{RadianceResolution=8,RadiancePower=1,RadianceLampSize=.2,
                    RadianceAlbedo=0,ShadowEdge=.1,ObjectRimWidth=.1,ObjectLightMode="gradient"};
                field=RoomRadianceField.Build(room,new RoomDefinitionCatalog(),new RoomLandCatalog(),table,Vector2.zero,null);
                var scene=(Texture2D)Get(field,"baseScene");
                scene.SetPixel(64,12,new Color(4,4,4,1));
                float absorption=transmission==0?1:(float)(1-Math.Pow(transmission,1.0/4));
                for(int y=38;y<42;y++) for(int x=0;x<128;x++) scene.SetPixel(x,y,new Color(0,0,0,absorption));
                scene.Apply();field.UpdateOccluders(Array.Empty<(Rect,double)>());field.Render();
                var raw=(RenderTexture)Get(field,"fluenceRaw");
                RenderTexture.active=raw;
                copy=new Texture2D(raw.width,raw.height,TextureFormat.RGBAFloat,false,true);
                copy.ReadPixels(new Rect(0,0,raw.width,raw.height),0,0);copy.Apply();
                var result=new float[96*24]; int index=0;
                for(int y=48;y<72;y++) for(int x=16;x<112;x++) result[index++]=copy.GetPixel(x,y).r;
                return result;
            }
            finally
            {
                RenderTexture.active=previous;
                if(copy!=null) UnityEngine.Object.DestroyImmediate(copy);
                if(field!=null) UnityEngine.Object.DestroyImmediate(field.gameObject);
            }
        }
        static object Get(object value,string name)=>value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(value);
    }
}
