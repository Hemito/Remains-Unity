using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Remains.Content.VectorArt
{
    // RVG v1 (docs/pipelines/vector-format.md): the original art as vector data, rasterized on the player's machine.
    // Plain data with strict validation on load; units are twips (20 per source pixel), Y down, as in SWF.
    public sealed class RvgDocument
    {
        public string format;
        public int version;
        public RvgSource source;
        public List<RvgBitmap> bitmaps=new();
        public List<RvgShape> shapes=new();
        public List<RvgSprite> sprites=new();
        public List<RvgResource> resources=new();
        public List<RvgUnsupported> unsupported=new();

        [NonSerialized] private Dictionary<int,RvgShape> shapeIndex;
        [NonSerialized] private Dictionary<int,RvgSprite> spriteIndex;
        [NonSerialized] private Dictionary<int,RvgBitmap> bitmapIndex;
        [NonSerialized] private Dictionary<string,RvgResource> resourceIndex;

        public static RvgDocument Parse(string json,string origin)
        {
            var document=JsonConvert.DeserializeObject<RvgDocument>(json,new JsonSerializerSettings {MissingMemberHandling=MissingMemberHandling.Error})
                ??throw new InvalidDataException($"RVG {origin}: пустой документ.");
            document.Validate(origin);
            return document;
        }
        public RvgShape Shape(int id)=>shapeIndex.TryGetValue(id,out var shape)?shape:throw new InvalidDataException($"RVG {source?.archive}: нет фигуры {id}.");
        public RvgSprite Sprite(int id)=>spriteIndex.TryGetValue(id,out var sprite)?sprite:throw new InvalidDataException($"RVG {source?.archive}: нет спрайта {id}.");
        public RvgBitmap Bitmap(int id)=>bitmapIndex.TryGetValue(id,out var bitmap)?bitmap:throw new InvalidDataException($"RVG {source?.archive}: нет растра {id}.");
        public bool TryResource(string id,out RvgResource resource)=>resourceIndex.TryGetValue(id,out resource);
        public bool HasShape(int id)=>shapeIndex.ContainsKey(id);
        public bool HasSprite(int id)=>spriteIndex.ContainsKey(id);
        public bool HasBitmap(int id)=>bitmapIndex.ContainsKey(id);

        private void Validate(string origin)
        {
            if(format!="remains-rvg" || version!=1) throw new InvalidDataException($"RVG {origin}: формат {format} версии {version}, ожидается remains-rvg 1.");
            if(source==null || string.IsNullOrEmpty(source.archive)) throw new InvalidDataException($"RVG {origin}: нет источника.");
            shapeIndex=Index(shapes,s=>s.id,"фигура",origin);spriteIndex=Index(sprites,s=>s.id,"спрайт",origin);bitmapIndex=Index(bitmaps,b=>b.id,"растр",origin);
            resourceIndex=new Dictionary<string,RvgResource>(StringComparer.Ordinal);
            foreach(var resource in resources)
            {
                if(string.IsNullOrEmpty(resource.id) || !resourceIndex.TryAdd(resource.id,resource)) throw new InvalidDataException($"RVG {origin}: пустой или повторный ресурс {resource.id}.");
                if(!spriteIndex.ContainsKey(resource.sprite)) throw new InvalidDataException($"RVG {origin}: ресурс {resource.id} ссылается на отсутствующий спрайт {resource.sprite}.");
                foreach(var frame in resource.frames)
                    if(frame.origin==null || frame.origin.Length!=2 || frame.size==null || frame.size.Length!=2 || frame.size[0]<=0 || frame.size[1]<=0)
                        throw new InvalidDataException($"RVG {origin}: ресурс {resource.id}, кадр {frame.frame}: нет точки привязки или размера кадра.");
            }
            foreach(var shape in shapes) shape.Validate(this,origin);
            foreach(var sprite in sprites)
            {
                if(sprite.frames==null || sprite.frames.Count!=sprite.frameCount) throw new InvalidDataException($"RVG {origin}: спрайт {sprite.id}: кадров {sprite.frames?.Count}, объявлено {sprite.frameCount}.");
                foreach(var frame in sprite.frames) foreach(var placed in frame) placed.Validate(this,sprite.id,origin);
            }
        }
        private static Dictionary<int,T> Index<T>(List<T> items,Func<T,int> id,string what,string origin)
        {
            var index=new Dictionary<int,T>();
            foreach(var item in items) if(!index.TryAdd(id(item),item)) throw new InvalidDataException($"RVG {origin}: повторный {what} {id(item)}.");
            return index;
        }
        internal static void Matrix(float[] m,string where)
        {
            if(m==null || m.Length!=6) throw new InvalidDataException($"RVG {where}: матрица должна иметь 6 чисел.");
            foreach(float value in m) if(!float.IsFinite(value)) throw new InvalidDataException($"RVG {where}: нечисловая матрица.");
        }
        internal static void Color(int[] c,string where)
        {
            if(c==null || c.Length!=4) throw new InvalidDataException($"RVG {where}: цвет должен иметь 4 канала.");
            foreach(int value in c) if(value<0 || value>255) throw new InvalidDataException($"RVG {where}: канал цвета {value} вне 0–255.");
        }
    }

    public sealed class RvgSource {public string archive,xml,xmlSha256,swfSha256,catalogSha256;}
    public sealed class RvgBitmap {public int id,width,height;public string file,sha256;public string source;}
    public sealed class RvgUnsupported {public string resource,feature,detail;public int frame;public int? character;}

    public sealed class RvgShape
    {
        public int id;
        public int[] bounds;
        public List<RvgFill> fills=new();
        public List<RvgLine> lines=new();
        public List<RvgPath> paths=new();
        public string rule;   // null: even-odd (contract); the converter records another rule explicitly
        internal void Validate(RvgDocument document,string origin)
        {
            string where=$"{origin}: фигура {id}";
            if(bounds==null || bounds.Length!=4) throw new InvalidDataException($"RVG {where}: нет границ.");
            if(rule!=null && rule!="even-odd" && rule!="non-zero") throw new InvalidDataException($"RVG {where}: неизвестное правило заполнения {rule}.");
            // Only styles that paths use are resolved: the source keeps unused bitmap styles pointing at no bitmap (id 65535).
            var usedFills=new HashSet<int>();var usedLines=new HashSet<int>();
            foreach(var path in paths)
            {
                if((path.fill==null)==(path.line==null)) throw new InvalidDataException($"RVG {where}: путь должен иметь ровно одну заливку или линию.");
                if(path.fill!=null && (path.fill<0 || path.fill>=fills.Count)) throw new InvalidDataException($"RVG {where}: путь ссылается на заливку {path.fill}.");
                if(path.line!=null && (path.line<0 || path.line>=lines.Count)) throw new InvalidDataException($"RVG {where}: путь ссылается на линию {path.line}.");
                RvgPath.Check(path.commands,where);
                if(path.fill!=null) usedFills.Add(path.fill.Value);else usedLines.Add(path.line.Value);
            }
            foreach(int i in usedFills) fills[i].Validate(document,$"{where}, заливка {i}");
            foreach(int i in usedLines) lines[i].Validate(document,$"{where}, линия {i}");
        }
    }
    public sealed class RvgPath
    {
        public int? fill,line;
        public int[] commands;
        // 0 x y move, 1 x y line, 2 cx cy x y quadratic, 3 close.
        internal static void Check(int[] commands,string where)
        {
            if(commands==null || commands.Length==0) throw new InvalidDataException($"RVG {where}: пустой путь.");
            int i=0;
            while(i<commands.Length)
            {
                int op=commands[i];
                int length=op switch {0=>3,1=>3,2=>5,3=>1,_=>throw new InvalidDataException($"RVG {where}: неизвестная команда пути {op}.")};
                if(i+length>commands.Length) throw new InvalidDataException($"RVG {where}: обрезанная команда пути {op}.");
                i+=length;
            }
            if(commands[0]!=0) throw new InvalidDataException($"RVG {where}: путь не начинается с moveTo.");
        }
    }
    public sealed class RvgFill
    {
        public string type;          // solid | linear | radial | focal | bitmap
        public int[] color;
        public float[] matrix;
        public string spread,interpolation;
        public float focal;
        public float[][] stops;      // [ratio, r, g, b, a]
        public int bitmap;
        public bool repeat,smooth;
        internal void Validate(RvgDocument document,string where)
        {
            switch(type)
            {
                case "solid": RvgDocument.Color(color,where);break;
                case "linear": case "radial": case "focal":
                    RvgDocument.Matrix(matrix,where);
                    if(spread!="pad" && spread!="reflect" && spread!="repeat") throw new InvalidDataException($"RVG {where}: режим продолжения градиента {spread}.");
                    if(interpolation!="rgb" && interpolation!="linear-rgb") throw new InvalidDataException($"RVG {where}: интерполяция градиента {interpolation}.");
                    if(stops==null || stops.Length==0) throw new InvalidDataException($"RVG {where}: градиент без точек.");
                    foreach(var stop in stops) if(stop==null || stop.Length!=5 || stop[0]<0 || stop[0]>255) throw new InvalidDataException($"RVG {where}: неверная точка градиента.");
                    if(type=="focal" && (focal<-1 || focal>1)) throw new InvalidDataException($"RVG {where}: фокус {focal} вне −1…1.");
                    break;
                case "bitmap":
                    RvgDocument.Matrix(matrix,where);
                    if(!document.HasBitmap(bitmap)) throw new InvalidDataException($"RVG {where}: нет растра {bitmap}.");
                    break;
                default: throw new InvalidDataException($"RVG {where}: неизвестный тип заливки {type}.");
            }
        }
    }
    public sealed class RvgLine
    {
        public float width;
        public int[] color;
        public RvgFill fill;
        public string startCap="round",endCap="round",join="round";
        public float miterLimit=3;
        public bool noHScale,noVScale,pixelHinting,noClose;
        internal void Validate(RvgDocument document,string where)
        {
            if(!float.IsFinite(width) || width<0) throw new InvalidDataException($"RVG {where}: толщина {width}.");
            if((color==null)==(fill==null)) throw new InvalidDataException($"RVG {where}: у линии должен быть ровно один цвет или заливка.");
            if(color!=null) RvgDocument.Color(color,where);else fill.Validate(document,where+", заливка линии");
            foreach(string cap in new[]{startCap,endCap}) if(cap!="round" && cap!="none" && cap!="square") throw new InvalidDataException($"RVG {where}: окончание линии {cap}.");
            if(join!="round" && join!="bevel" && join!="miter") throw new InvalidDataException($"RVG {where}: соединение линии {join}.");
        }
    }

    public sealed class RvgSprite {public int id,frameCount;public List<List<RvgObject>> frames=new();}
    public sealed class RvgObject
    {
        public int depth,character;
        public string kind;          // shape | sprite | bitmap
        public float[] matrix;
        public RvgColorTransform color;
        public string blend="normal";
        public List<RvgFilter> filters=new();
        public int clipDepth;
        public string name;
        public bool cacheAsBitmap,visible=true;
        public int? ratio;
        public int spriteFrame;
        internal void Validate(RvgDocument document,int sprite,string origin)
        {
            string where=$"{origin}: спрайт {sprite}, объект глубины {depth}";
            RvgDocument.Matrix(matrix,where);
            switch(kind)
            {
                case "shape": if(!document.HasShape(character)) throw new InvalidDataException($"RVG {where}: нет фигуры {character}.");break;
                case "sprite":
                    if(!document.HasSprite(character)) throw new InvalidDataException($"RVG {where}: нет спрайта {character}.");
                    if(spriteFrame<0 || spriteFrame>=document.Sprite(character).frameCount) throw new InvalidDataException($"RVG {where}: кадр вложенного спрайта {spriteFrame}.");
                    break;
                case "bitmap": if(!document.HasBitmap(character)) throw new InvalidDataException($"RVG {where}: нет растра {character}.");break;
                default: throw new InvalidDataException($"RVG {where}: неизвестный вид объекта {kind}.");
            }
            if(Array.IndexOf(Blends,blend)<0) throw new InvalidDataException($"RVG {where}: режим наложения {blend}.");
            color?.Validate(where);
            foreach(var filter in filters) filter.Validate(where);
            if(clipDepth<0 || clipDepth>0 && clipDepth<=depth) throw new InvalidDataException($"RVG {where}: глубина маски {clipDepth}.");
        }
        public static readonly string[] Blends={"normal","layer","multiply","screen","lighten","darken","difference","add","subtract","invert","alpha","erase","overlay","hardlight"};
    }
    public sealed class RvgColorTransform
    {
        public float[] mul,add;
        internal void Validate(string where)
        {
            if(mul==null || mul.Length!=4 || add==null || add.Length!=4) throw new InvalidDataException($"RVG {where}: цветовое преобразование должно иметь mul и add по 4 числа.");
            foreach(float value in mul) if(!float.IsFinite(value)) throw new InvalidDataException($"RVG {where}: нечисловой множитель цвета.");
            foreach(float value in add) if(!float.IsFinite(value) || value<-255 || value>255) throw new InvalidDataException($"RVG {where}: слагаемое цвета {value}.");
        }
    }
    public sealed class RvgFilter
    {
        public string type;          // glow | blur | dropShadow | colorMatrix
        public int[] color;
        public float blurX,blurY,strength=1,angle,distance;
        public int passes=1;
        public bool inner,knockout,compositeSource=true;
        public float[] matrix;
        internal void Validate(string where)
        {
            switch(type)
            {
                case "glow": case "dropShadow": RvgDocument.Color(color,where+", фильтр");goto case "blur";
                case "blur": if(!float.IsFinite(blurX) || !float.IsFinite(blurY) || blurX<0 || blurY<0 || passes<1) throw new InvalidDataException($"RVG {where}: параметры размытия фильтра.");break;
                case "colorMatrix": if(matrix==null || matrix.Length!=20) throw new InvalidDataException($"RVG {where}: цветовая матрица должна иметь 20 чисел.");break;
                default: throw new InvalidDataException($"RVG {where}: неизвестный фильтр {type}.");
            }
        }
    }
    public sealed class RvgResource {public string id,kind;public int sprite;public List<RvgResourceFrame> frames=new();}
    public sealed class RvgResourceFrame {public int frame;public float[] origin,size;public string reference,referenceSha256;}
}
