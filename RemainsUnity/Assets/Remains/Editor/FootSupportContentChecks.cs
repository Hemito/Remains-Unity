using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;
using Debug=UnityEngine.Debug;

namespace Remains.Editor
{
    // Real imported data + independent native PNG decode. No body fitting or
    // original-runtime equivalence is asserted by this content/query suite.
    public static class FootSupportContentChecks
    {
        [Serializable] private sealed class Library {public Clip[] clips;}
        [Serializable] private sealed class Clip {public string id;public Frame[] frames;}
        [Serializable] private sealed class Frame {public Part[] parts;}
        [Serializable] private sealed class Part {public string shape,texture,path;public double[] matrix,bounds;}
        private readonly struct CloudPoint
        {
            public readonly double X,Y;public readonly int Part,PixelX,PixelY;
            public CloudPoint(double x,double y,int part,int px,int py) {X=x;Y=y;Part=part;PixelX=px;PixelY=py;}
        }
        private sealed class Pixels {public int Width,Height;public Color32[] Data;}
        public static void Run()
        {
            string raw=Resources.Load<TextAsset>("Original/Pony/pony").text;
            var declaration=Array.Find(StateJson.FromJson<AnatomyCatalog>(Resources.Load<TextAsset>("Content/anatomy").text).anatomies,a=>a.id=="pony").supportContacts;
            var library=JsonConvert.DeserializeObject<Library>(raw,new JsonSerializerSettings {FloatParseHandling=FloatParseHandling.Double});
            var native=new Dictionary<string,Pixels>(StringComparer.Ordinal);
            var textures=new Dictionary<string,FootSupportTexture>(StringComparer.Ordinal);
            foreach(var clip in library.clips) foreach(var frame in clip.frames) foreach(var part in frame.parts)
                if(Array.IndexOf(declaration.shapes,part.shape)>=0 && !textures.ContainsKey(part.texture))
                {
                    string file=PonyContactSource.Png(part.texture);
                    var encoded=File.ReadAllBytes(file);var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
                    try
                    {
                        Require(ImageConversion.LoadImage(texture,encoded),"PNG ноги не декодирован: "+file);
                        var pixels=texture.GetPixels32();var topDown=new byte[pixels.Length*4];
                        for(int y=0;y<texture.height;y++) for(int x=0;x<texture.width;x++)
                        {
                            var value=pixels[(texture.height-1-y)*texture.width+x];int index=(y*texture.width+x)*4;
                            topDown[index]=value.r;topDown[index+1]=value.g;topDown[index+2]=value.b;topDown[index+3]=value.a;
                        }
                        native.Add(part.texture,new Pixels {Width=texture.width,Height=texture.height,Data=pixels});
                        textures.Add(part.texture,new FootSupportTexture(part.texture,texture.width,texture.height,Hash(encoded),Hash(topDown)));
                    }
                    finally {UnityEngine.Object.DestroyImmediate(texture);}
                }
            var bytes=Resources.Load<TextAsset>(FootSupportCatalog.Resource).bytes;
            var expected=FootSupportSourceExpectation.Create("pony",raw,declaration.alphaMinimum,declaration.shapes,textures);
            var watch=Stopwatch.StartNew();var data=FootSupportCatalog.Create(bytes,expected);double loadMs=watch.Elapsed.TotalMilliseconds;
            Require(data.ClipCount==61 && data.FrameCount==347 && data.PointCount==513580 && data.Textures.Count==4,"Неполный production foot-support набор");
            Require(data.Supports("pony") && !data.Supports("radroach"),"Потерян явный anatomy capability");
            Throws(()=>data.Get("pony","unknown-clip",0),"missing clip");Throws(()=>data.Get("pony","idle",-1),"invalid frame");
            var first=data.Get("pony","idle",0);
            Throws(()=>first.TryLowest(1,1,-1,1,out _),"missing preparation");
            foreach(double gradient in new[]{0,.5,1}) data.PrepareGradient(gradient);
            int previousCount=first.PreparedGradientCount;data.PrepareGradient(-1);
            Require(previousCount==5 && first.PreparedGradientCount==previousCount,"Повторный +/- gradient создал новые деревья");
            int frames=0,cases=0,empty=0,cloudCentres=0;double maximumError=0;
            foreach(var clip in library.clips)
            for(int frame=0;frame<clip.frames.Length;frame++)
            {
                frames++;var sampled=data.Get("pony",clip.id,frame);var cloud=new List<CloudPoint>();
                var parts=clip.frames[frame].parts;
                for(int part=0;part<parts.Length;part++)
                {
                    var source=parts[part];if(Array.IndexOf(declaration.shapes,source.shape)<0) continue;
                    var pixels=native[source.texture];var m=source.matrix;var b=source.bounds;
                    // Independently compose pixel-centre coordinates into one affine map.
                    double dx=(b[2]-b[0])/pixels.Width,dy=(b[3]-b[1])/pixels.Height;
                    double a=m[0]*dx/1000,c=m[2]*dy/1000,tx=(m[0]*(b[0]+dx*.5)+m[2]*(b[1]+dy*.5)+m[4])/1000;
                    double ab=-m[1]*dx/1000,cb=-m[3]*dy/1000,ty=-(m[1]*(b[0]+dx*.5)+m[3]*(b[1]+dy*.5)+m[5])/1000;
                    for(int y=0;y<pixels.Height;y++) for(int x=0;x<pixels.Width;x++)
                    {
                        if(pixels.Data[(pixels.Height-1-y)*pixels.Width+x].a<declaration.alphaMinimum) continue;
                        cloud.Add(new CloudPoint(a*x+c*y+tx,ab*x+cb*y+ty,part,x,y));
                    }
                }
                cloudCentres+=cloud.Count;
                double cloudMinX=double.PositiveInfinity,cloudMinY=cloudMinX,cloudMaxX=double.NegativeInfinity,cloudMaxY=cloudMaxX;
                foreach(var point in cloud)
                {cloudMinX=Math.Min(cloudMinX,point.X);cloudMinY=Math.Min(cloudMinY,point.Y);cloudMaxX=Math.Max(cloudMaxX,point.X);cloudMaxY=Math.Max(cloudMaxY,point.Y);}
                Require(Math.Abs(sampled.Bounds.MinimumX-cloudMinX)<=1e-12 && Math.Abs(sampled.Bounds.MinimumY-cloudMinY)<=1e-12 &&
                    Math.Abs(sampled.Bounds.MaximumX-cloudMaxX)<=1e-12 && Math.Abs(sampled.Bounds.MaximumY-cloudMaxY)<=1e-12,
                    $"Полные bounds потеряли upper/нижние opaque centres: pose={clip.id}, frame={frame}");
                double min=sampled.Bounds.MinimumX,max=sampled.Bounds.MaximumX,span=max-min;
                var ranges=new[]{(min-1,max+1),(min+span*.1,min+span*.7),(min+span*.45,min+span*.95),
                    (min-span,min-span*.1),(max+span*.1,max+span),(min+span*.3,min+span*.31)};
                foreach(int facing in new[]{-1,1}) foreach(double gradient in new[]{-1,-.5,0,.5,1}) foreach(var range in ranges)
                {
                    double left=Math.Min(facing*range.Item1,facing*range.Item2),right=Math.Max(facing*range.Item1,facing*range.Item2);
                    bool found=false;double lowest=0;
                    foreach(var point in cloud)
                    {
                        double x=facing*point.X;if(x<left || x>right) continue;
                        double value=point.Y-gradient*x;if(!found || value<lowest) {found=true;lowest=value;}
                    }
                    bool queried=sampled.TryLowest(gradient,facing,left,right,out var contact);cases++;
                    Require(queried==found,$"Finite eligibility расходится с PNG: pose={clip.id}, frame={frame}, gradient={gradient}, facing={facing}");
                    if(!found) {empty++;continue;}
                    double error=Math.Abs(contact.Y-gradient*facing*contact.X-lowest);maximumError=Math.Max(maximumError,error);
                    Require(error<=1e-12,$"Range-min потерял PNG экстремум: pose={clip.id}, frame={frame}, error={error:R}");
                    Require(contact.PartIndex>=0 && contact.PartIndex<parts.Length,"Нет part provenance результата");
                    var image=native[parts[contact.PartIndex].texture];
                    Require(contact.PixelX>=0 && contact.PixelX<image.Width && contact.PixelY>=0 && contact.PixelY<image.Height &&
                        image.Data[(image.Height-1-contact.PixelY)*image.Width+contact.PixelX].a>=declaration.alphaMinimum,
                        "Результат не указывает на реальный opaque pixel");
                }
                // Exact endpoint inclusion uses the stored identity, not affine tolerance.
                var endpoint=sampled.Groups[0].Points[0];
                Require(sampled.TryLowest(0,1,endpoint.X,endpoint.X,out var exact) && exact.X==endpoint.X,"Exact-X singleton был потерян");
            }
            Require(frames==347 && cloudCentres==515842,"Не проверено полное independent opaque cloud");
            Throws(()=>first.TryLowest(0,0,0,1,out _),"invalid facing");Throws(()=>first.TryLowest(0,1,2,1,out _),"inverted interval");
            Throws(()=>first.PrepareGradient(double.NaN),"invalid gradient");
            Throws(()=>((IList<FootSupportPoint>)first.Groups[0].Points)[0]=default,"mutable public points");
            var original=first.Groups[0].Points[0];var copy=(byte[])bytes.Clone();var detached=FootSupportCatalog.Create(copy,expected);copy[0]^=1;
            Require(detached.Get("pony","idle",0).Groups[0].Points[0].X==original.X,"Чужие compressed bytes изменили опубликованные точки");
            var payload=Unpack(bytes);int coordinate=FirstPointOffset(payload);
            Throws(()=>FootSupportCatalog.Create(copy,expected),"corrupted gzip");
            Throws(()=>FootSupportCatalog.Create(Change(payload,8,BitConverter.GetBytes(2)),expected),"schema");
            Throws(()=>FootSupportCatalog.Create(Change(payload,12,new[]{(byte)(payload[12]^1)}),expected),"source hash");
            double tamperedX=BitConverter.ToDouble(payload,coordinate);long bits=BitConverter.DoubleToInt64Bits(tamperedX);
            Throws(()=>FootSupportCatalog.Create(Change(payload,coordinate,BitConverter.GetBytes(BitConverter.Int64BitsToDouble(bits+1))),expected),"one ULP point tamper with valid checksum");
            Throws(()=>FootSupportCatalog.Create(Change(payload,coordinate+16,BitConverter.GetBytes((ushort)65535)),expected),"pixel provenance");
            var clipped=new byte[payload.Length-1];Array.Copy(payload,clipped,clipped.Length);
            Throws(()=>FootSupportCatalog.Create(Pack(clipped),expected),"truncated record");
            var trailing=new byte[payload.Length+1];Array.Copy(payload,trailing,payload.Length);
            Throws(()=>FootSupportCatalog.Create(Pack(trailing),expected),"trailing payload");
            FootSupportCatalog.Load();Require(FootSupportCatalog.Fingerprint==Hash(bytes),"Fingerprint не соответствует фактическому gzip");
            FootSupportCatalog.Supports("pony");Throws(()=>FootSupportCatalog.RegisterProvider(data),"late provider replacement");
            Debug.Log($"Foot-support: PASS — {frames} кадров, {cloudCentres} independent PNG centres, {cases} finite queries/{empty} empty, error={maximumError:R}; 4 PNG/RGBA hashes, exact-X/provenance, immutable bytes, corrupt/stale/one-ULP negatives, startup-only provider. Pure load={loadMs:F2}ms. Foot-fit/original runtime/GPU filtering не проверены.");
        }
        private static int FirstPointOffset(byte[] raw)
        {
            using var reader=new BinaryReader(new MemoryStream(raw));reader.BaseStream.Position=44;
            void Text() {int count=reader.ReadInt32();reader.BaseStream.Position+=count;}
            Text();reader.ReadInt32();int shapes=reader.ReadInt32();for(int i=0;i<shapes;i++) Text();
            int textures=reader.ReadInt32();for(int i=0;i<textures;i++) {Text();reader.BaseStream.Position+=72;}
            reader.ReadInt32();Text();reader.ReadInt32();reader.ReadInt32();reader.ReadInt32();Text();Text();Text();reader.BaseStream.Position+=32;reader.ReadInt32();
            return (int)reader.BaseStream.Position;
        }
        private static byte[] Change(byte[] raw,int offset,byte[] replacement)
        {var copy=(byte[])raw.Clone();Array.Copy(replacement,0,copy,offset,replacement.Length);return Pack(copy);}
        private static byte[] Unpack(byte[] bytes)
        {using var gzip=new GZipStream(new MemoryStream(bytes),CompressionMode.Decompress);using var result=new MemoryStream();gzip.CopyTo(result);var value=result.ToArray();Array.Resize(ref value,value.Length-32);return value;}
        private static byte[] Pack(byte[] payload)
        {using var output=new MemoryStream();using(var gzip=new GZipStream(output,CompressionMode.Compress,true)) {gzip.Write(payload,0,payload.Length);using var hash=SHA256.Create();var digest=hash.ComputeHash(payload);gzip.Write(digest,0,digest.Length);}return output.ToArray();}
        private static string Hash(byte[] bytes)
        {using var hash=SHA256.Create();return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        private static void Throws(Action action,string context)
        {try {action();} catch(InvalidOperationException) {return;} catch(NotSupportedException) {return;}throw new InvalidOperationException("Foot-support negative принят: "+context+".");}
        private static void Require(bool value,string message)
        {if(!value) throw new InvalidOperationException("Foot-support проверка: "+message+".");}
    }
}
