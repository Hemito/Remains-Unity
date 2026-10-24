using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Content
{
    public readonly struct FootSupportPoint
    {
        public readonly double X,Y;
        public readonly int PartIndex,PixelX,PixelY;
        internal FootSupportPoint(double x,double y,int part,int pixelX,int pixelY)
        {X=x;Y=y;PartIndex=part;PixelX=pixelX;PixelY=pixelY;}
    }
    public readonly struct FootSupportBounds
    {
        public readonly double MinimumX,MinimumY,MaximumX,MaximumY;
        internal FootSupportBounds(double minX,double minY,double maxX,double maxY)
        {
            if(!double.IsFinite(minX) || !double.IsFinite(minY) || !double.IsFinite(maxX) || !double.IsFinite(maxY) || minX>maxX || minY>maxY)
                throw new InvalidOperationException("Некорректные границы силуэта ноги.");
            MinimumX=minX;MinimumY=minY;MaximumX=maxX;MaximumY=maxY;
        }
    }
    public sealed class FootSupportTexture
    {
        public string Key {get;}
        public int Width {get;}
        public int Height {get;}
        public string Sha256 {get;}
        public string RgbaSha256 {get;}
        public FootSupportTexture(string key,int width,int height,string sha256,string rgbaSha256)
        {
            if(string.IsNullOrWhiteSpace(key) || width<1 || height<1 || width>65535 || height>65535 ||
                !ValidHash(sha256) || !ValidHash(rgbaSha256)) throw new InvalidOperationException("Некорректные PNG provenance: key="+key+".");
            Key=key;Width=width;Height=height;Sha256=sha256;RgbaSha256=rgbaSha256;
        }
        private static bool ValidHash(string value)
        {
            if(value==null || value.Length!=64) return false;
            foreach(char c in value) if(!((c>='0' && c<='9') || (c>='a' && c<='f'))) return false;
            return value!=new string('0',64);
        }
    }
    public sealed class FootSupportGroup
    {
        public int PartIndex {get;}
        public string Shape {get;}
        public string Texture {get;}
        public string Path {get;}
        public FootSupportBounds Bounds {get;}
        public IReadOnlyList<FootSupportPoint> Points {get;}
        internal readonly FootSupportPoint[] RawPoints;
        internal FootSupportGroup(int part,string shape,string texture,string path,FootSupportBounds bounds,FootSupportPoint[] points)
        {PartIndex=part;Shape=shape;Texture=texture;Path=path;Bounds=bounds;RawPoints=points;Points=Array.AsReadOnly(points);}
    }
    public sealed class FootSupportFrame
    {
        public string Anatomy {get;}
        public string Clip {get;}
        public int Index {get;}
        public FootSupportBounds Bounds {get;}
        public IReadOnlyList<FootSupportGroup> Groups {get;}
        public int PreparedGradientCount=>prepared.Count;
        private readonly FootSupportGroup[] groups;
        private readonly Dictionary<double,RangeMinimum[]> prepared=new();
        internal FootSupportFrame(string anatomy,string clip,int index,FootSupportGroup[] source)
        {
            Anatomy=anatomy;Clip=clip;Index=index;groups=source;Groups=Array.AsReadOnly(source);
            double minX=double.PositiveInfinity,minY=minX,maxX=double.NegativeInfinity,maxY=maxX;
            foreach(var group in groups)
            {minX=Math.Min(minX,group.Bounds.MinimumX);minY=Math.Min(minY,group.Bounds.MinimumY);
             maxX=Math.Max(maxX,group.Bounds.MaximumX);maxY=Math.Max(maxY,group.Bounds.MaximumY);}
            Bounds=new FootSupportBounds(minX,minY,maxX,maxY);
        }
        // Explicit preparation only. +/- share effective-gradient tables, including zero.
        public void PrepareGradient(double gradient)
        {CheckGradient(gradient);Prepare(gradient);Prepare(-gradient);}
        private void Prepare(double effective)
        {
            if(prepared.ContainsKey(effective)) return;
            if(prepared.Count>=32) throw new InvalidOperationException($"Слишком много подготовленных уклонов: anatomy={Anatomy}, clip={Clip}, frame={Index}.");
            var values=new RangeMinimum[groups.Length];
            for(int i=0;i<values.Length;i++) values[i]=new RangeMinimum(groups[i],effective,Anatomy,Clip,Index);
            prepared.Add(effective,values);
        }
        // [left,right] is inclusive mirrored actor-local X. Result stays unmirrored.
        // No interpolation, allocation, asset IO, or point scan in this query.
        public bool TryLowest(double gradient,int facing,double left,double right,out FootSupportPoint point)
        {
            CheckGradient(gradient);
            if((facing!=1 && facing!=-1) || !double.IsFinite(left) || !double.IsFinite(right) || left>right)
                throw new InvalidOperationException($"Некорректный конечный запрос опоры: anatomy={Anatomy}, clip={Clip}, frame={Index}, facing={facing}, interval=[{left},{right}].");
            double effective=gradient*facing;
            if(!prepared.TryGetValue(effective,out var tables))
                throw new InvalidOperationException($"Уклон опоры не подготовлен до Update: anatomy={Anatomy}, clip={Clip}, frame={Index}, gradient={gradient}, facing={facing}.");
            if(facing==-1) {double value=left;left=-right;right=-value;}
            point=default;bool found=false;double best=0;
            for(int group=0;group<tables.Length;group++)
            {
                int index=tables[group].Query(left,right);
                if(index<0) continue;
                var candidate=groups[group].RawPoints[index];double value=candidate.Y-effective*candidate.X;
                if(!found || value<best) {found=true;best=value;point=candidate;}
            }
            return found;
        }
        private static void CheckGradient(double value)
        {if(!double.IsFinite(value)) throw new InvalidOperationException("Неконечный уклон запроса опоры: "+value+".");}

        private sealed class RangeMinimum
        {
            private readonly FootSupportPoint[] points;
            private readonly int[] tree;
            private readonly int size;
            private readonly double gradient;
            public RangeMinimum(FootSupportGroup group,double effective,string anatomy,string clip,int frame)
            {
                points=group.RawPoints;gradient=effective;size=1;
                while(size<points.Length) size*=2;
                tree=new int[size*2];Array.Fill(tree,-1);
                for(int i=0;i<points.Length;i++)
                {
                    if(!double.IsFinite(points[i].Y-gradient*points[i].X))
                        throw new InvalidOperationException($"Переполнение проекции ноги: anatomy={anatomy}, clip={clip}, frame={frame}, part={group.PartIndex}, gradient={effective}.");
                    tree[size+i]=i;
                }
                for(int i=size-1;i>0;i--) tree[i]=Choose(tree[i*2],tree[i*2+1]);
            }
            private int Choose(int a,int b)
            {
                if(a<0) return b;if(b<0) return a;
                double first=points[a].Y-gradient*points[a].X,second=points[b].Y-gradient*points[b].X;
                return first<second || (first==second && a<b)?a:b;
            }
            public int Query(double left,double right)
            {
                int low=0,high=points.Length;
                while(low<high) {int middle=(low+high)/2;if(points[middle].X<left) low=middle+1;else high=middle;}
                int first=low;low=0;high=points.Length;
                while(low<high) {int middle=(low+high)/2;if(points[middle].X<=right) low=middle+1;else high=middle;}
                int end=low,result=-1;
                for(first+=size,end+=size;first<end;first/=2,end/=2)
                {if((first&1)!=0) result=Choose(result,tree[first++]);if((end&1)!=0) result=Choose(result,tree[--end]);}
                return result;
            }
        }
    }
    public interface IFootSupportProvider
    {
        bool Supports(string anatomy);
        FootSupportFrame Get(string anatomy,string clip,int frame);
        void PrepareGradient(double gradient);
    }
    public sealed class FootSupportData : IFootSupportProvider
    {
        public string Anatomy {get;}
        public string SourceHash {get;}
        public int AlphaMinimum {get;}
        public int ClipCount=>clips.Count;
        public int FrameCount {get;}
        public int PointCount {get;}
        public IReadOnlyDictionary<string,FootSupportTexture> Textures {get;}
        private readonly Dictionary<string,FootSupportFrame[]> clips;
        internal FootSupportData(string anatomy,string sourceHash,int alpha,Dictionary<string,FootSupportTexture> textures,
            Dictionary<string,FootSupportFrame[]> values,int frames,int points)
        {Anatomy=anatomy;SourceHash=sourceHash;AlphaMinimum=alpha;Textures=new ReadOnlyDictionary<string,FootSupportTexture>(textures);clips=values;FrameCount=frames;PointCount=points;}
        public bool Supports(string anatomy)=>anatomy==Anatomy;
        public FootSupportFrame Get(string anatomy,string clip,int frame)
        {
            if(anatomy!=Anatomy || clip==null || !clips.TryGetValue(clip,out var frames) || frame<0 || frame>=frames.Length)
                throw new InvalidOperationException($"Нет точного силуэта опоры: anatomy={anatomy}, clip={clip}, frame={frame}.");
            return frames[frame];
        }
        public void PrepareGradient(double gradient)
        {foreach(var clip in clips.Values) foreach(var frame in clip) frame.PrepareGradient(gradient);}
    }
    // Captures raw JSON doubles rather than lossy PonyPart float registration.
    // No mutable library or texture arrays are retained after construction.
    public sealed class FootSupportSourceExpectation
    {
        internal string Anatomy,SourceHash;
        internal int Alpha;
        internal string[] Shapes;
        internal SourceClip[] Clips;
        internal IReadOnlyDictionary<string,FootSupportTexture> Textures;
        private FootSupportSourceExpectation() {}
        public static FootSupportSourceExpectation Create(string anatomy,string rawPonyJson,int alphaMinimum,
            IReadOnlyList<string> shapes,IReadOnlyDictionary<string,FootSupportTexture> expectedTextures=null)
        {
            if(string.IsNullOrWhiteSpace(anatomy) || rawPonyJson==null || alphaMinimum<1 || alphaMinimum>255 || shapes==null || shapes.Count==0)
                throw new InvalidOperationException("Нет явного графического источника для foot-support.");
            // Saved state intentionally preserves Decimal tokens; converting
            // those to double may round by one ULP. Resource registration needs
            // direct IEEE doubles, matching the exported JSON/PNG centres.
            using var json=new JsonTextReader(new StringReader(rawPonyJson))
                {FloatParseHandling=FloatParseHandling.Double,DateParseHandling=DateParseHandling.None,MaxDepth=128};
            SourceLibrary source;
            try
            {
                var token=JToken.Load(json,new JsonLoadSettings {DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});
                if(json.Read()) throw new InvalidOperationException("Лишние данные после raw Pony source JSON.");
                source=token.ToObject<SourceLibrary>();
            }
            catch(JsonException error)
            {throw new InvalidOperationException("Не удалось прочитать raw double-регистрацию ног: anatomy="+anatomy+". "+error.Message,error);}
            if(source?.clips==null || source.clips.Length<1 || source.clips.Length>4096)
                throw new InvalidOperationException("Некорректный исходный каталог foot-support: anatomy="+anatomy+".");
            using var hash=SHA256.Create();
            var result=new FootSupportSourceExpectation {Anatomy=anatomy,SourceHash=Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(rawPonyJson))),
                Alpha=alphaMinimum,Shapes=new string[shapes.Count],Clips=source.clips};
            var declared=new HashSet<string>(StringComparer.Ordinal);
            for(int i=0;i<shapes.Count;i++)
            {if(string.IsNullOrWhiteSpace(shapes[i]) || !declared.Add(shapes[i])) throw new InvalidOperationException("Повторная/пустая графическая нога: "+shapes[i]+".");result.Shapes[i]=shapes[i];}
            var ids=new HashSet<string>(StringComparer.Ordinal);var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var clip in source.clips)
            {
                if(clip==null || string.IsNullOrWhiteSpace(clip.id) || !ids.Add(clip.id) || clip.frames==null || clip.frames.Length<1 || clip.frames.Length>65536)
                    throw new InvalidOperationException("Некорректный исходный клип foot-support: "+clip?.id+".");
                foreach(var frame in clip.frames)
                {
                    if(frame?.parts==null || frame.parts.Length<1 || frame.parts.Length>65536) throw new InvalidOperationException("Нет исходных частей foot-support: "+clip.id+".");
                    var selected=new List<SourcePart>();
                    for(int index=0;index<frame.parts.Length;index++)
                    {
                        var part=frame.parts[index];
                        if(part==null || !declared.Contains(part.shape)) continue;
                        if(string.IsNullOrWhiteSpace(part.texture) || string.IsNullOrWhiteSpace(part.path) || part.bounds?.Length!=4 || part.matrix?.Length!=6)
                            throw new InvalidOperationException($"Некорректная исходная регистрация: clip={clip.id}, part={index}.");
                        foreach(double value in part.bounds) if(!double.IsFinite(value)) throw new InvalidOperationException("Неконечные bounds ноги.");
                        foreach(double value in part.matrix) if(!double.IsFinite(value)) throw new InvalidOperationException("Неконечная матрица ноги.");
                        part.index=index;selected.Add(part);seen.Add(part.shape);
                    }
                    if(selected.Count<1 || selected.Count>256) throw new InvalidOperationException("Нет объявленных нижних ног: "+clip.id+".");
                    frame.selected=selected.ToArray();frame.parts=null;
                }
            }
            if(!seen.SetEquals(declared)) throw new InvalidOperationException("Не все объявленные glyph найдены: anatomy="+anatomy+".");
            if(expectedTextures!=null) result.Textures=new ReadOnlyDictionary<string,FootSupportTexture>(new Dictionary<string,FootSupportTexture>(expectedTextures));
            return result;
        }
        [Serializable] private sealed class SourceLibrary {public SourceClip[] clips;}
        [Serializable] internal sealed class SourceClip {public string id;public SourceFrame[] frames;}
        [Serializable] internal sealed class SourceFrame {public SourcePart[] parts;[NonSerialized] public SourcePart[] selected;}
        [Serializable] internal sealed class SourcePart {public string shape,texture,path;public double[] matrix,bounds;[NonSerialized] public int index;}
    }
    public static class FootSupportCatalog
    {
        public const string Resource="Content/foot-support";
        public const int MaximumRawBytes=32*1024*1024,MaximumCompressedBytes=16*1024*1024;
        private static IFootSupportProvider provider;
        private static string fingerprint;
        private static bool queried;
        public static string Fingerprint=>fingerprint??throw new InvalidOperationException("Fingerprint foot-support ещё не загружен явно: "+Resource+".");
        // Startup-only code-mod replacement, before any cached frame is handed out.
        // Load still validates/fingerprints the shipped resource independently.
        public static void RegisterProvider(IFootSupportProvider value)
        {
            if(value==null) throw new ArgumentNullException(nameof(value),"Нельзя заменить каталог опоры отсутствующим provider.");
            if(queried) throw new InvalidOperationException("Замена provider опоры разрешена только при запуске, до Supports/Get/PrepareGradient. Перезагрузите игровой контекст явно.");
            provider=value;
        }
        public static void Load()
        {
            if(fingerprint!=null) return;
            var asset=GameAssets.Load<TextAsset>(Resource);var pony=GameAssets.Load<TextAsset>("Original/Pony/pony");
            var anatomy=GameAssets.Load<TextAsset>("Content/anatomy");
            if(asset==null || pony==null || anatomy==null) throw new InvalidOperationException("Нет ресурса foot-support/pony/anatomy: "+Resource+".");
            var definitions=StateJson.FromJson<AnatomyCatalog>(anatomy.text);
            if(definitions?.anatomies==null) throw new InvalidOperationException("Нет анатомий для foot-support: Content/anatomy.");
            var definition=Array.Find(definitions.anatomies,a=>a?.id=="pony")?.supportContacts;
            if(definition?.schema!=1 || definition.alphaMinimum!=192 || definition.sourceHash!=PonyRenderAsset.SourceHash())
                throw new InvalidOperationException("Нет актуальной отдельной декларации ног: anatomy=pony, "+Resource+".");
            var bytes=asset.bytes;
            var loaded=Create(bytes,FootSupportSourceExpectation.Create("pony",pony.text,definition.alphaMinimum,definition.shapes));
            using var hash=SHA256.Create();var computed=Hex(hash.ComputeHash(bytes));
            if(provider==null) provider=loaded;
            fingerprint=computed;
        }
        private static IFootSupportProvider Require()
        {
            if(provider==null) throw new InvalidOperationException("Каталог точной опоры не загружен явно: "+Resource+".");
            queried=true;return provider;
        }
        public static bool Supports(string anatomy)=>Require().Supports(anatomy);
        public static FootSupportFrame Get(string anatomy,string clip,int frame)
        {
            var value=Require().Get(anatomy,clip,frame);
            if(value==null || value.Anatomy!=anatomy || value.Clip!=clip || value.Index!=frame)
                throw new InvalidOperationException($"Provider опоры вернул неверный кадр: anatomy={anatomy}, clip={clip}, frame={frame}.");
            return value;
        }
        public static void PrepareGradient(double gradient)=>Require().PrepareGradient(gradient);

        // Pure CPU parser; native textures are not read here. Runtime validates raw
        // JSON registration/provenance. Editor supplies actual PNG/RGBA expectations.
        public static FootSupportData Create(byte[] compressed,FootSupportSourceExpectation expected)
        {
            if(compressed==null || compressed.Length==0 || compressed.Length>MaximumCompressedBytes || expected==null)
                throw new InvalidOperationException("Нет допустимых bytes/expectation foot-support: "+Resource+".");
            try
            {
                using var source=new MemoryStream(compressed,false);using var gzip=new GZipStream(source,CompressionMode.Decompress);
                using var output=new MemoryStream();var buffer=new byte[8192];int length;
                while((length=gzip.Read(buffer,0,buffer.Length))>0)
                {if(output.Length+length>MaximumRawBytes) throw new InvalidOperationException("Превышен raw binary cap foot-support.");output.Write(buffer,0,length);}
                var raw=output.ToArray();if(raw.Length<76) throw new InvalidOperationException("Обрезанный binary foot-support.");
                using var hash=SHA256.Create();var checksum=hash.ComputeHash(raw,0,raw.Length-32);
                for(int i=0;i<32;i++) if(checksum[i]!=raw[raw.Length-32+i]) throw new InvalidOperationException("Повреждён SHA256 payload foot-support.");
                using var stream=new MemoryStream(raw,0,raw.Length-32,false);using var reader=new BinaryReader(stream,new UTF8Encoding(false,true));
                if(Encoding.ASCII.GetString(Read(reader,8))!="PFEFOOT1" || reader.ReadInt32()!=1) throw new InvalidOperationException("Неизвестная схема binary foot-support.");
                string sourceHash=Convert.ToBase64String(Read(reader,32)),anatomy=Text(reader);int alpha=reader.ReadInt32();
                if(sourceHash!=expected.SourceHash || anatomy!=expected.Anatomy || alpha!=expected.Alpha)
                    throw new InvalidOperationException($"Устаревший foot-support: anatomy={anatomy}, alpha={alpha}, sourceHash={sourceHash}, expected={expected.SourceHash}.");
                int shapeCount=Count(reader,64,"shapes");if(shapeCount!=expected.Shapes.Length) throw new InvalidOperationException("Неполная декларация glyph foot-support.");
                for(int i=0;i<shapeCount;i++) if(Text(reader)!=expected.Shapes[i]) throw new InvalidOperationException("Изменены glyph/order foot-support.");
                int textureCount=Count(reader,4096,"textures");var textures=new Dictionary<string,FootSupportTexture>(StringComparer.Ordinal);
                for(int i=0;i<textureCount;i++)
                {
                    string key=Text(reader);int width=reader.ReadInt32(),height=reader.ReadInt32();
                    var texture=new FootSupportTexture(key,width,height,Hex(Read(reader,32)),Hex(Read(reader,32)));
                    if(textures.ContainsKey(key)) throw new InvalidOperationException("Повторная текстура foot-support: "+key+".");
                    if(expected.Textures!=null && (!expected.Textures.TryGetValue(key,out var actual) || actual.Width!=width || actual.Height!=height || actual.Sha256!=texture.Sha256 || actual.RgbaSha256!=texture.RgbaSha256))
                        throw new InvalidOperationException("PNG/RGBA provenance отличается от источника: "+key+".");
                    textures.Add(key,texture);
                }
                if(expected.Textures!=null && expected.Textures.Count!=textures.Count) throw new InvalidOperationException("Неполный набор PNG foot-support.");
                int clipCount=Count(reader,4096,"clips");if(clipCount!=expected.Clips.Length) throw new InvalidOperationException("Неполный набор клипов foot-support.");
                var clips=new Dictionary<string,FootSupportFrame[]>(StringComparer.Ordinal);var usedTextures=new HashSet<string>(StringComparer.Ordinal);
                int totalFrames=0,totalPoints=0;
                for(int clipIndex=0;clipIndex<clipCount;clipIndex++)
                {
                    string id=Text(reader);var sourceClip=expected.Clips[clipIndex];int frameCount=Count(reader,65536,"frames");
                    if(id!=sourceClip.id || clips.ContainsKey(id) || frameCount!=sourceClip.frames.Length) throw new InvalidOperationException("Неполный/повторный клип foot-support: "+id+".");
                    totalFrames+=frameCount;if(totalFrames>65536) throw new InvalidOperationException("Превышен общий frame cap foot-support.");
                    var frames=new FootSupportFrame[frameCount];
                    for(int frame=0;frame<frameCount;frame++)
                    {
                        int groupCount=Count(reader,256,"groups");var selected=sourceClip.frames[frame].selected;
                        if(groupCount!=selected.Length) throw new InvalidOperationException($"Неполные ноги: anatomy={anatomy}, clip={id}, frame={frame}.");
                        var groups=new FootSupportGroup[groupCount];
                        for(int group=0;group<groupCount;group++)
                        {
                            int part=reader.ReadInt32();string shape=Text(reader),key=Text(reader),path=Text(reader);var declared=selected[group];
                            if(part!=declared.index || shape!=declared.shape || key!=declared.texture || path!=declared.path || !textures.TryGetValue(key,out var texture))
                                throw new InvalidOperationException($"Неверная часть опоры: anatomy={anatomy}, clip={id}, frame={frame}, part={part}, shape={shape}, texture={key}, path={path}.");
                            usedTextures.Add(key);
                            var bounds=new FootSupportBounds(reader.ReadDouble(),reader.ReadDouble(),reader.ReadDouble(),reader.ReadDouble());
                            int count=Count(reader,8192,"points");totalPoints+=count;if(totalPoints>2000000) throw new InvalidOperationException("Превышен общий point cap foot-support.");
                            var points=new FootSupportPoint[count];double previous=double.NegativeInfinity;
                            for(int point=0;point<count;point++)
                            {
                                double x=reader.ReadDouble(),y=reader.ReadDouble();int px=reader.ReadUInt16(),py=reader.ReadUInt16();
                                var b=declared.bounds;var m=declared.matrix;
                                double sx=b[0]+(px+.5)/texture.Width*(b[2]-b[0]),sy=b[1]+(py+.5)/texture.Height*(b[3]-b[1]);
                                double actualX=(m[0]*sx+m[2]*sy+m[4])/1000,actualY=-(m[1]*sx+m[3]*sy+m[5])/1000;
                                if(!double.IsFinite(x) || !double.IsFinite(y) || x<=previous || px>=texture.Width || py>=texture.Height || x!=actualX || y!=actualY ||
                                    x<bounds.MinimumX || x>bounds.MaximumX || y<bounds.MinimumY || y>bounds.MaximumY)
                                    throw new InvalidOperationException($"Повреждена exact-X точка/регистрация: anatomy={anatomy}, clip={id}, frame={frame}, part={part}, point={point}, pixel=({px},{py}), actual=({x:R},{y:R}), expected=({actualX:R},{actualY:R}).");
                                previous=x;points[point]=new FootSupportPoint(x,y,part,px,py);
                            }
                            if(points[0].X!=bounds.MinimumX || points[count-1].X!=bounds.MaximumX) throw new InvalidOperationException($"Неполные X bounds: clip={id}, frame={frame}, part={part}.");
                            groups[group]=new FootSupportGroup(part,shape,key,path,bounds,points);
                        }
                        frames[frame]=new FootSupportFrame(anatomy,id,frame,groups);
                    }
                    clips.Add(id,frames);
                }
                if(stream.Position!=stream.Length || usedTextures.Count!=textures.Count) throw new InvalidOperationException("Лишние bytes/текстуры foot-support.");
                return new FootSupportData(anatomy,sourceHash,alpha,textures,clips,totalFrames,totalPoints);
            }
            catch(Exception error) when(error is InvalidDataException || error is IOException || error is ArgumentException || error is OverflowException)
            {throw new InvalidOperationException("Не удалось прочитать binary foot-support: "+Resource+". "+error.Message,error);}
        }
        private static int Count(BinaryReader reader,int cap,string context)
        {int value=reader.ReadInt32();if(value<1 || value>cap) throw new InvalidOperationException($"Некорректный binary count: {context}={value}, cap={cap}.");return value;}
        private static byte[] Read(BinaryReader reader,int count)
        {var value=reader.ReadBytes(count);if(value.Length!=count) throw new EndOfStreamException("Обрезанный binary foot-support.");return value;}
        private static string Text(BinaryReader reader)
        {var value=new UTF8Encoding(false,true).GetString(Read(reader,Count(reader,1024,"string")));if(string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("Пустой ID foot-support.");return value;}
        private static string Hex(byte[] value)=>BitConverter.ToString(value).Replace("-","").ToLowerInvariant();
    }
}
