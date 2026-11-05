using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Domain;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Same PFEFOOT1 contract as the frozen original bank. New clips are prepared
    // separately from their PNGs, without rewriting the original's exact doubles.
    public static class PonyAuthoredSupportAuthoring
    {
        private const string AnatomyPath="Assets/Remains/Resources/Content/anatomy.json";
        private const string Output="Assets/Remains/Resources/Content/foot-support-authored.bytes";
        private sealed class Pixels {public int Width,Height;public Color32[] Data;public byte[] Png,Rgba;}
        private readonly struct Point
        {
            public readonly double X,Y;public readonly ushort Px,Py;
            public Point(double x,double y,int px,int py){X=x;Y=y;Px=(ushort)px;Py=(ushort)py;}
        }
        public static void BuildBatch()
        {try{Build();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
        public static void Build()
        {
            string raw=GameAssets.Load<TextAsset>(PonyAuthoredClips.Resource).text.Replace("\r\n","\n");
            var clips=(JArray)JObject.Parse(raw)["clips"];
            var anatomy=JObject.Parse(File.ReadAllText(AnatomyPath));
            var support=(JObject)((JArray)anatomy["anatomies"]).Single(a=>(string)a["id"]=="pony")["supportContacts"];
            int alpha=(int)support["alphaMinimum"];var shapes=((JArray)support["shapes"]).Select(s=>(string)s).ToArray();
            var textures=new Dictionary<string,Pixels>(StringComparer.Ordinal);
            foreach(var clip in clips)foreach(var frame in (JArray)clip["frames"])foreach(var part in (JArray)frame["parts"])
            {
                if(!shapes.Contains((string)part["shape"]) || textures.ContainsKey((string)part["texture"]))continue;
                string key=(string)part["texture"];byte[] png=File.ReadAllBytes(PonyContactSource.Png(key));
                var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
                try
                {
                    if(!ImageConversion.LoadImage(texture,png))throw new InvalidDataException("Не декодирован растр опоры: "+key);
                    var pixels=texture.GetPixels32();var rgba=new byte[pixels.Length*4];
                    for(int y=0;y<texture.height;y++)for(int x=0;x<texture.width;x++)
                    {var p=pixels[(texture.height-1-y)*texture.width+x];int i=(y*texture.width+x)*4;rgba[i]=p.r;rgba[i+1]=p.g;rgba[i+2]=p.b;rgba[i+3]=p.a;}
                    textures.Add(key,new Pixels {Width=texture.width,Height=texture.height,Data=pixels,Png=png,Rgba=rgba});
                }
                finally {UnityEngine.Object.DestroyImmediate(texture);}
            }
            using var payload=new MemoryStream();using var writer=new BinaryWriter(payload,Encoding.UTF8,true);
            void Text(string value){var bytes=Encoding.UTF8.GetBytes(value);writer.Write(bytes.Length);writer.Write(bytes);}
            void Hash(byte[] bytes){using var sha=SHA256.Create();writer.Write(sha.ComputeHash(bytes));}
            writer.Write(Encoding.ASCII.GetBytes("PFEFOOT1"));writer.Write(1);Hash(Encoding.UTF8.GetBytes(raw));Text("pony");writer.Write(alpha);
            writer.Write(shapes.Length);foreach(string shape in shapes)Text(shape);
            writer.Write(textures.Count);foreach(var pair in textures)
            {Text(pair.Key);writer.Write(pair.Value.Width);writer.Write(pair.Value.Height);Hash(pair.Value.Png);Hash(pair.Value.Rgba);}
            writer.Write(clips.Count);int points=0,frameCount=0;
            var tracks=(JArray)support["poses"];
            foreach(var clip in clips)
            {
                string id=(string)clip["id"];Text(id);var frames=(JArray)clip["frames"];writer.Write(frames.Count);
                var contactFrames=new JArray();
                foreach(var frame in frames)
                {
                    var selected=((JArray)frame["parts"]).Select((part,index)=>(Part:part,Index:index)).Where(p=>shapes.Contains((string)p.Part["shape"])).ToArray();
                    writer.Write(selected.Length);var contacts=new JArray();frameCount++;
                    foreach(var selectedPart in selected)
                    {
                        var part=selectedPart.Part;string key=(string)part["texture"];var texture=textures[key];
                        double[] b=((JArray)part["bounds"]).Select(v=>(double)v).ToArray(),m=((JArray)part["matrix"]).Select(v=>(double)v).ToArray();
                        var cloud=new List<Point>();
                        for(int y=0;y<texture.Height;y++)for(int x=0;x<texture.Width;x++)
                        {
                            if(texture.Data[(texture.Height-1-y)*texture.Width+x].a<alpha)continue;
                            double sx=b[0]+(x+.5)/texture.Width*(b[2]-b[0]),sy=b[1]+(y+.5)/texture.Height*(b[3]-b[1]);
                            cloud.Add(new Point((m[0]*sx+m[2]*sy+m[4])/1000,-(m[1]*sx+m[3]*sy+m[5])/1000,x,y));
                        }
                        if(cloud.Count==0)throw new InvalidDataException("Нет непрозрачной ноги: "+id+"/"+selectedPart.Index);
                        var lowest=cloud.OrderBy(p=>p.Y).First();contacts.Add(new JObject{["x"]=(float)lowest.X,["y"]=(float)lowest.Y});
                        var envelope=cloud.GroupBy(p=>p.X).Select(g=>g.OrderBy(p=>p.Y).First()).OrderBy(p=>p.X).ToArray();
                        writer.Write(selectedPart.Index);Text((string)part["shape"]);Text(key);Text((string)part["path"]);
                        writer.Write(cloud.Min(p=>p.X));writer.Write(lowest.Y);writer.Write(cloud.Max(p=>p.X));writer.Write(cloud.Max(p=>p.Y));
                        writer.Write(envelope.Length);foreach(var p in envelope){writer.Write(p.X);writer.Write(p.Y);writer.Write(p.Px);writer.Write(p.Py);}points+=envelope.Length;
                    }
                    contactFrames.Add(new JObject{["points"]=contacts});
                }
                if(BedRest.HasFootSupport(id))
                {
                    foreach(var previous in tracks.Where(t=>(string)t["pose"]==id).ToArray())previous.Remove();
                    tracks.Add(new JObject{["pose"]=id,["frames"]=contactFrames});
                }
            }
            writer.Flush();var body=payload.ToArray();Hash(body);writer.Flush();
            using var compressed=new MemoryStream();using(var gzip=new GZipStream(compressed,System.IO.Compression.CompressionLevel.Optimal,true))payload.WriteTo(gzip);
            byte[] packed=compressed.ToArray();
            var decoded=FootSupportCatalog.Create(packed,FootSupportSourceExpectation.Create("pony",raw,alpha,shapes));
            if(decoded.FrameCount!=frameCount || decoded.PointCount!=points)throw new InvalidDataException("Потеряны кадры/точки при упаковке опоры.");
            File.WriteAllBytes(Output,packed);File.WriteAllText(AnatomyPath,anatomy.ToString());
            AssetDatabase.ImportAsset(Output,ImportAssetOptions.ForceSynchronousImport);AssetDatabase.ImportAsset(AnatomyPath,ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"Опора своих анимаций: PASS — {clips.Count}клипа/{frameCount}кадров, {points}exact-X точек, {textures.Count}PNG, {packed.Length}gzip bytes; исходный бинарный банк не изменён.");
        }
    }
}

