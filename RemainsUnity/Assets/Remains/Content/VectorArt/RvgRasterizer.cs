using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace Remains.Content.VectorArt
{
    // Rasterizes an RVG resource frame on the GPU (docs/pipelines/vector-format.md, «Растеризация»):
    // stencil-then-cover. Every fill path is drawn as triangle fans that invert stencil bit 0 (even-odd parity), then a
    // bounding quad paints where the bit is set and clears it. Strokes are stencil-replaced, so overlaps blend once.
    // Masks (clipDepth) live in stencil bits 1–7, one per nesting level. An object with filters or a blend mode is a
    // group: drawn into its own texture, filtered, then composited onto its parent (as Flash does). Antialiasing: MSAA. Geometry is
    // transformed on the CPU into canvas pixels (Y up inside, top-left registration outside). Values are raw sRGB bytes:
    // linear textures and canvas, so blending happens in gamma space as in Flash.
    public sealed class RvgRasterizer : IDisposable
    {
        // Hairline rule (contract: established by the converter probe): a stroke thinner than this many canvas pixels
        // is drawn at this width; a zero-width stroke is a hairline of this width.
        public float MinLinePixels=1;
        public int Msaa=8;
        // Supersampling: the canvas is drawn this many times larger and box-averaged down, so edge coverage gets
        // Msaa × Supersample² levels instead of Msaa + 1. Line widths and filter sizes stay in output pixels.
        public int Supersample=1;
        // Filter sizes are stage pixels: at zoom N the stage is N times larger, the object's own transform does not scale
        // them (Flash; against the catalog references better than unscaled and than object-scaled on the whole, 2026-10-25).
        public bool FiltersScaleWithZoom=true;
        // Approximations (a mask inside a mask): counted and reported, never silently dropped.
        public readonly Dictionary<string,int> Approximations=new(StringComparer.Ordinal);

        private const int MaskLevels=7;
        private readonly Func<RvgBitmap,Texture2D> loadBitmap;
        private readonly Shader stencilShader,paintShader,finalShader,filterShader,compositeShader;
        private readonly Dictionary<(string,int),Material> compositeMaterials=new();
        // Render targets of the current frame. Owned here, not taken from RenderTexture.GetTemporary: its pool frees only as
        // player frames pass, so a loading screen or batch run would keep every size alive. Commands run in recorded order,
        // so a target a finished group no longer reads goes back to the free list for the next group of the same frame.
        private readonly List<RenderTexture> allocated=new(),inUse=new();
        private readonly Stack<RenderTexture> freeMultisampled=new(),freePlain=new();
        private Material filterMaterial;
        private RenderTexture current;
        private readonly Dictionary<(int,int,int,int,int),Material> stencilMaterials=new();
        private readonly Dictionary<(string,int,int,int,int,int,string),Material> paintMaterials=new();
        private readonly Dictionary<RvgFill,Texture2D> ramps=new();
        private readonly Dictionary<(RvgDocument,int),Texture2D> bitmaps=new();
        private readonly List<Mesh> meshes=new();
        private readonly CommandBuffer cmd=new() {name="RVG: растеризация кадра"};
        private readonly MaterialPropertyBlock block=new();
        private Material finalMaterial;
        private RvgDocument document;
        private int width,height;
        private float zoom;
        private int supersample=1;

        public RvgRasterizer(Func<RvgBitmap,Texture2D> loadBitmap)
        {
            this.loadBitmap=loadBitmap??throw new ArgumentNullException(nameof(loadBitmap));
            stencilShader=GameAssets.Load<Shader>("Presentation/RvgStencil")??throw new InvalidOperationException("Нет шейдера Presentation/RvgStencil.");
            paintShader=GameAssets.Load<Shader>("Presentation/RvgPaint")??throw new InvalidOperationException("Нет шейдера Presentation/RvgPaint.");
            finalShader=GameAssets.Load<Shader>("Presentation/RvgFinal")??throw new InvalidOperationException("Нет шейдера Presentation/RvgFinal.");
            filterShader=GameAssets.Load<Shader>("Presentation/RvgFilter")??throw new InvalidOperationException("Нет шейдера Presentation/RvgFilter.");
            compositeShader=GameAssets.Load<Shader>("Presentation/RvgComposite")??throw new InvalidOperationException("Нет шейдера Presentation/RvgComposite.");
        }

        // The frame as a straight-alpha RGBA32 texture (sRGB) of ceil(size × zoom); registration at origin × zoom from the top-left.
        // Render reads the pixels back (checks, the disk cache); RenderGpu keeps them on the GPU only — a non-readable texture
        // filled by a GPU copy, without a pipeline stall — for sprites the game draws straight away.
        public Texture2D Render(RvgDocument source,RvgResource resource,RvgResourceFrame frame,float scale)=>Draw(source,resource,frame,scale,true);
        public Texture2D RenderGpu(RvgDocument source,RvgResource resource,RvgResourceFrame frame,float scale)=>Draw(source,resource,frame,scale,false);
        private Texture2D Draw(RvgDocument source,RvgResource resource,RvgResourceFrame frame,float scale,bool readable)
        {
            if(source==null || resource==null || frame==null) throw new ArgumentNullException(nameof(source));
            if(!float.IsFinite(scale) || scale<=0) throw new ArgumentOutOfRangeException(nameof(scale));
            if(Supersample<1 || Supersample>4) throw new ArgumentOutOfRangeException(nameof(Supersample),Supersample,"RVG: суперсэмплинг 1…4.");
            document=source;supersample=Supersample;zoom=scale*supersample;
            int outputWidth=Mathf.CeilToInt(frame.size[0]*scale-1e-4f),outputHeight=Mathf.CeilToInt(frame.size[1]*scale-1e-4f);
            width=outputWidth*supersample;height=outputHeight*supersample;
            if(outputWidth<1 || outputHeight<1 || width>16384 || height>16384) throw new InvalidDataException($"RVG {resource.id}, кадр {frame.frame}: холст {width}×{height}.");
            var sprite=document.Sprite(resource.sprite);
            int index=frame.frame-1;
            if(index<0 || index>=sprite.frameCount) throw new InvalidDataException($"RVG {resource.id}: кадр {frame.frame} вне 1…{sprite.frameCount}.");
            // twips → top-left pixels → canvas (Y up).
            var root=Affine.Scale(zoom/20f).Then(Affine.Translate(frame.origin[0]*zoom,frame.origin[1]*zoom)).Then(new Affine(1,0,0,-1,0,height));
            var canvas=Temporary(true);
            var resolved=new RenderTexture(new RenderTextureDescriptor(outputWidth,outputHeight,RenderTextureFormat.ARGB32,0) {sRGB=false}) {hideFlags=HideFlags.HideAndDontSave};
            Create(resolved);
            try
            {
                cmd.Clear();
                cmd.SetViewProjectionMatrices(Matrix4x4.identity,GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(0,width,0,height,-1,1),false));
                Target(canvas,true);
                DrawSprite(sprite,index,root,ColorTransform.Identity,0);
                // Resolve (MSAA) and un-premultiply into the readable target.
                finalMaterial??=new Material(finalShader) {hideFlags=HideFlags.HideAndDontSave};
                cmd.SetRenderTarget(resolved);
                cmd.ClearRenderTarget(RTClearFlags.Color,Color.clear,1,0);
                block.Clear();block.SetTexture("_MainTex",canvas);block.SetFloat("_Supersample",supersample);
                cmd.DrawMesh(Quad(new Rect(0,0,width,height),true),Matrix4x4.identity,finalMaterial,0,0,block);
                Graphics.ExecuteCommandBuffer(cmd);
                var result=new Texture2D(outputWidth,outputHeight,TextureFormat.RGBA32,false,false,!readable) {name=$"{resource.id}#{frame.frame}@{scale}"};
                if(!readable)
                {
                    result.Apply(false,true);   // drop the CPU copy; the GPU copy below fills the texture
                    Graphics.CopyTexture(resolved,0,0,result,0,0);
                    return result;
                }
                var previous=RenderTexture.active;
                try {RenderTexture.active=resolved;result.ReadPixels(new Rect(0,0,outputWidth,outputHeight),0,0,false);result.Apply(false);}
                finally {RenderTexture.active=previous;}
                return result;
            }
            finally
            {
                cmd.Clear();
                foreach(var texture in allocated) {texture.Release();UnityEngine.Object.DestroyImmediate(texture);}
                allocated.Clear();inUse.Clear();freeMultisampled.Clear();freePlain.Clear();current=null;
                foreach(var mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh);
                meshes.Clear();
            }
        }

        // Objects in depth order; a clipDepth object starts a mask level for the depths up to its clipDepth.
        private void DrawSprite(RvgSprite sprite,int frameIndex,in Affine m,in ColorTransform color,int maskBits)
        {
            var clips=new List<(int End,int Bit)>();
            foreach(var placed in sprite.frames[frameIndex])
            {
                while(clips.Count>0 && placed.depth>clips[^1].End) {ClearBit(clips[^1].Bit);maskBits&=~clips[^1].Bit;clips.RemoveAt(clips.Count-1);}
                if(!placed.visible) continue;
                var om=Affine.From(placed.matrix).Then(m);
                if(placed.clipDepth>0)
                {
                    int level=BitCount(maskBits);
                    if(level>=MaskLevels) throw new InvalidDataException($"RVG: вложенных масок больше {MaskLevels} (спрайт {sprite.id}).");
                    int bit=1<<(level+1);
                    DrawMask(placed,om,maskBits,bit);
                    clips.Add((placed.clipDepth,bit));maskBits|=bit;
                    continue;
                }
                var oc=color.After(placed.color);
                if(placed.filters.Count>0 || placed.blend!="normal") DrawGroup(placed,om,oc,maskBits);
                else DrawObject(placed,om,oc,maskBits);
            }
            for(int i=clips.Count-1;i>=0;i--) ClearBit(clips[i].Bit);
        }
        private void DrawObject(RvgObject placed,in Affine m,in ColorTransform color,int maskBits)
        {
            switch(placed.kind)
            {
                case "shape": DrawShape(document.Shape(placed.character),m,color,maskBits);break;
                case "sprite": DrawSprite(document.Sprite(placed.character),placed.spriteFrame,m,color,maskBits);break;
                case "bitmap":
                    var bitmap=document.Bitmap(placed.character);
                    var fill=new RvgFill {type="bitmap",bitmap=bitmap.id,matrix=new float[]{20,0,0,20,0,0},repeat=false,smooth=true};
                    var shape=new RvgShape {id=-bitmap.id,bounds=new[]{0,0,bitmap.width*20,bitmap.height*20}};
                    shape.fills.Add(fill);
                    shape.paths.Add(new RvgPath {fill=0,commands=new[]{0,0,0,1,bitmap.width*20,0,1,bitmap.width*20,bitmap.height*20,1,0,bitmap.height*20,3}});
                    DrawShape(shape,m,color,maskBits);
                    break;
            }
        }
        private void DrawShape(RvgShape shape,in Affine m,in ColorTransform color,int maskBits)
        {
            if(shape.rule=="non-zero") throw new NotSupportedException($"RVG: фигура {shape.id} требует правила non-zero; растеризатор v1 поддерживает even-odd.");
            foreach(var path in shape.paths)
            {
                var contours=RvgGeometry.Contours(path.commands,m);
                if(contours.Count==0) continue;
                var vertices=new List<Vector3>();var indices=new List<int>();var bounds=RvgGeometry.EmptyBounds;
                RvgFill fill;
                if(path.fill!=null)
                {
                    RvgGeometry.Fans(contours,vertices,indices,ref bounds);
                    if(indices.Count==0) continue;
                    Stencil(Mesh(vertices,indices),maskBits==0?CompareFunction.Always:CompareFunction.Equal,maskBits,maskBits,1,StencilOp.Invert);
                    fill=shape.fills[path.fill.Value];
                }
                else
                {
                    var line=shape.lines[path.line.Value];
                    RvgGeometry.Stroke(contours,LineWidth(line,m),line,vertices,indices,ref bounds);
                    if(indices.Count==0) continue;
                    Stencil(Mesh(vertices,indices),maskBits==0?CompareFunction.Always:CompareFunction.Equal,maskBits|1,maskBits,1,StencilOp.Replace);
                    fill=line.fill??new RvgFill {type="solid",color=line.color};
                }
                Paint(fill,m,color,bounds,maskBits);
            }
        }
        private float LineWidth(RvgLine line,in Affine m)
        {
            float pixels=line.noHScale && line.noVScale?line.width/20f*zoom:line.width*Mathf.Sqrt(Mathf.Abs(m.Determinant));
            return Mathf.Max(pixels,MinLinePixels*supersample);
        }
        // Mask geometry (fills and strokes of a shape, or of every object of a sprite) into bit; bit 0 is used and cleared.
        private void DrawMask(RvgObject placed,in Affine m,int maskBits,int bit)
        {
            var shapes=new List<(RvgShape Shape,Affine Matrix)>();
            CollectMaskShapes(placed,m,shapes);
            foreach(var (shape,matrix) in shapes)
                foreach(var path in shape.paths)
                {
                    var contours=RvgGeometry.Contours(path.commands,matrix);
                    var vertices=new List<Vector3>();var indices=new List<int>();var bounds=RvgGeometry.EmptyBounds;
                    var compare=maskBits==0?CompareFunction.Always:CompareFunction.Equal;
                    if(path.fill!=null) {RvgGeometry.Fans(contours,vertices,indices,ref bounds);if(indices.Count==0) continue;Stencil(Mesh(vertices,indices),compare,maskBits,maskBits,1,StencilOp.Invert);}
                    else
                    {
                        var line=shape.lines[path.line.Value];
                        RvgGeometry.Stroke(contours,LineWidth(line,matrix),line,vertices,indices,ref bounds);if(indices.Count==0) continue;
                        Stencil(Mesh(vertices,indices),compare,maskBits|1,maskBits,1,StencilOp.Replace);
                    }
                    var cover=Mesh(QuadVertices(bounds),QuadIndices);
                    Stencil(cover,CompareFunction.Equal,maskBits|1|bit,maskBits|1,bit,StencilOp.Replace);   // set the mask bit where parity is odd
                    Stencil(cover,CompareFunction.Always,0,0,1,StencilOp.Zero);                            // clear parity
                }
        }
        private void CollectMaskShapes(RvgObject placed,in Affine m,List<(RvgShape,Affine)> output)
        {
            switch(placed.kind)
            {
                case "shape": output.Add((document.Shape(placed.character),m));break;
                case "sprite":
                    foreach(var child in document.Sprite(placed.character).frames[placed.spriteFrame])
                        if(child.visible && child.clipDepth==0) CollectMaskShapes(child,Affine.From(child.matrix).Then(m),output);
                        else if(child.clipDepth>0) Count("маска внутри маски");
                    break;
                case "bitmap":
                    var b=document.Bitmap(placed.character);
                    var rect=new RvgShape {id=-b.id,bounds=new[]{0,0,b.width*20,b.height*20}};
                    rect.fills.Add(new RvgFill {type="solid",color=new[]{0,0,0,255}});
                    rect.paths.Add(new RvgPath {fill=0,commands=new[]{0,0,0,1,b.width*20,0,1,b.width*20,b.height*20,1,0,b.height*20,3}});
                    output.Add((rect,m));
                    break;
            }
        }
        private void ClearBit(int bit)=>Stencil(Mesh(QuadVertices(new Rect(-1,-1,width+2,height+2)),QuadIndices),CompareFunction.Always,0,0,bit,StencilOp.Zero);

        private void Paint(RvgFill fill,in Affine m,in ColorTransform color,Rect bounds,int maskBits)
        {
            bounds=Rect.MinMaxRect(bounds.xMin-1,bounds.yMin-1,bounds.xMax+1,bounds.yMax+1);
            var material=PaintMaterial(fill.type,maskBits);
            block.Clear();
            block.SetVector("_Mul",color.Mul);block.SetVector("_Add",color.Add);
            switch(fill.type)
            {
                case "solid": block.SetVector("_Color",ToColor(fill.color));break;
                case "bitmap":
                {
                    var texture=Bitmap(fill.bitmap);
                    var inverse=Affine.From(fill.matrix).Then(m).Inverse();   // canvas pixels → bitmap pixels
                    SetInverse(inverse);
                    block.SetTexture("_Bitmap",texture);block.SetVector("_BitmapSize",new Vector4(texture.width,texture.height,0,0));
                    block.SetFloat("_Repeat",fill.repeat?1:0);block.SetFloat("_Smooth",fill.smooth?1:0);
                    break;
                }
                default:
                {
                    var inverse=Affine.From(fill.matrix).Then(m).Inverse();   // canvas pixels → gradient square
                    SetInverse(inverse);
                    block.SetTexture("_Ramp",Ramp(fill));
                    block.SetFloat("_Spread",fill.spread=="pad"?0:fill.spread=="reflect"?1:2);
                    block.SetFloat("_Focal",fill.focal);
                    break;
                }
            }
            cmd.DrawMesh(Mesh(QuadVertices(bounds),QuadIndices),Matrix4x4.identity,material,0,0,block);
        }
        private void SetInverse(in Affine inverse)
        {
            block.SetVector("_Inv0",new Vector4(inverse.a,inverse.c,inverse.tx,0));
            block.SetVector("_Inv1",new Vector4(inverse.b,inverse.d,inverse.ty,0));
        }
        private Material PaintMaterial(string type,int maskBits)
        {
            // Paint (premultiplied over) where the active masks and parity are set; clear parity.
            var key=(type,maskBits|1,maskBits|1,1,(int)CompareFunction.Equal,(int)StencilOp.Zero,"normal");
            if(paintMaterials.TryGetValue(key,out var material)) return material;
            material=new Material(paintShader) {hideFlags=HideFlags.HideAndDontSave};
            material.EnableKeyword("_FILL_"+type.ToUpperInvariant());
            material.SetFloat("_Ref",key.Item2);material.SetFloat("_ReadMask",key.Item3);material.SetFloat("_WriteMask",1);
            material.SetFloat("_Comp",(int)CompareFunction.Equal);material.SetFloat("_Pass",(int)StencilOp.Zero);
            material.SetFloat("_SrcBlend",(int)BlendMode.One);material.SetFloat("_DstBlend",(int)BlendMode.OneMinusSrcAlpha);material.SetFloat("_BlendOp",(int)BlendOp.Add);
            paintMaterials.Add(key,material);
            return material;
        }
        // A group: the object alone in its own texture (own stencil), its filters, then onto the parent with its blend mode.
        private void DrawGroup(RvgObject placed,in Affine m,in ColorTransform color,int maskBits)
        {
            var parent=current;
            int mark=inUse.Count;
            var group=Temporary(true);
            Target(group,true);
            DrawObject(placed,m,color,0);
            RenderTexture source=group;
            if(placed.filters.Count>0)
            {
                source=Pass(group,0,0);   // resolve the multisampled group (a radius 0 copy)
                foreach(var filter in placed.filters) source=Filter(filter,source);
            }
            RenderTexture backdrop=null;
            if(placed.blend is "overlay" or "hardlight" or "difference") backdrop=Pass(parent,0,0);
            Target(parent,false);
            block.Clear();block.SetTexture("_Group",source);if(backdrop!=null) block.SetTexture("_Backdrop",backdrop);
            cmd.DrawMesh(Quad(new Rect(0,0,width,height),true),Matrix4x4.identity,CompositeMaterial(placed.blend,maskBits),0,0,block);
            // Everything this group took (its texture, filter passes, backdrop) is free for later commands.
            for(int i=inUse.Count-1;i>=mark;i--) (inUse[i].depth>0?freeMultisampled:freePlain).Push(inUse[i]);
            inUse.RemoveRange(mark,inUse.Count-mark);
        }
        private RenderTexture Filter(RvgFilter filter,RenderTexture source)
        {
            float scale=FiltersScaleWithZoom?zoom:supersample;
            switch(filter.type)
            {
                case "blur": return Blur(source,filter.blurX*scale,filter.blurY*scale,filter.passes);
                case "glow": case "dropShadow":
                {
                    var blurred=Blur(source,filter.blurX*scale,filter.blurY*scale,filter.passes);
                    var offset=Vector2.zero;
                    if(filter.type=="dropShadow") offset=new Vector2(Mathf.Cos(filter.angle),-Mathf.Sin(filter.angle))*filter.distance*scale;   // image Y down, texture Y up
                    var target=Temporary(false);Target(target,false,true);
                    block.Clear();block.SetTexture("_MainTex",source);block.SetTexture("_Blurred",blurred);
                    block.SetVector("_TexelSize",new Vector4(1f/width,1f/height,0,0));block.SetVector("_Offset",offset);
                    block.SetVector("_GlowColor",ToColor(filter.color));block.SetFloat("_Strength",filter.strength);
                    block.SetFloat("_Inner",filter.inner?1:0);block.SetFloat("_Knockout",filter.knockout?1:0);block.SetFloat("_Composite",filter.compositeSource?1:0);
                    cmd.DrawMesh(Quad(new Rect(0,0,width,height),true),Matrix4x4.identity,FilterMaterial,0,2,block);
                    return target;
                }
                case "colorMatrix":
                {
                    var m=filter.matrix;
                    var target=Temporary(false);Target(target,false,true);
                    block.Clear();block.SetTexture("_MainTex",source);
                    block.SetVector("_Matrix0",new Vector4(m[0],m[1],m[2],m[3]));block.SetVector("_Matrix1",new Vector4(m[5],m[6],m[7],m[8]));
                    block.SetVector("_Matrix2",new Vector4(m[10],m[11],m[12],m[13]));block.SetVector("_Matrix3",new Vector4(m[15],m[16],m[17],m[18]));
                    block.SetVector("_MatrixOffset",new Vector4(m[4],m[9],m[14],m[19])/255f);
                    cmd.DrawMesh(Quad(new Rect(0,0,width,height),true),Matrix4x4.identity,FilterMaterial,0,3,block);
                    return target;
                }
                default: throw new NotSupportedException("RVG: фильтр "+filter.type);
            }
        }
        // Flash blur: a box of the given width along each axis, repeated passes times.
        private RenderTexture Blur(RenderTexture source,float blurX,float blurY,int passes)
        {
            int rx=Mathf.Max(0,Mathf.RoundToInt((blurX-1)*.5f)),ry=Mathf.Max(0,Mathf.RoundToInt((blurY-1)*.5f));
            for(int p=0;p<passes;p++) {source=Pass(source,0,rx);source=Pass(source,1,ry);}
            return source;
        }
        // One full-canvas filter pass into a new temporary texture.
        private RenderTexture Pass(RenderTexture source,int pass,int radius)
        {
            var target=Temporary(false);Target(target,false,true);
            block.Clear();block.SetTexture("_MainTex",source);block.SetFloat("_Radius",radius);block.SetVector("_TexelSize",new Vector4(1f/width,1f/height,0,0));
            cmd.DrawMesh(Quad(new Rect(0,0,width,height),true),Matrix4x4.identity,FilterMaterial,0,pass,block);
            return target;
        }
        private Material FilterMaterial=>filterMaterial??=new Material(filterShader) {hideFlags=HideFlags.HideAndDontSave};
        private Material CompositeMaterial(string blend,int maskBits)
        {
            if(compositeMaterials.TryGetValue((blend,maskBits),out var material)) return material;
            material=new Material(compositeShader) {hideFlags=HideFlags.HideAndDontSave};
            string keyword=blend switch {"overlay"=>"_MODE_OVERLAY","hardlight"=>"_MODE_HARDLIGHT","difference"=>"_MODE_DIFFERENCE","invert"=>"_MODE_INVERT",_=>"_MODE_FIXED"};
            material.EnableKeyword(keyword);
            var (src,dst,op)=blend switch
            {
                "screen"=>(BlendMode.One,BlendMode.OneMinusSrcColor,BlendOp.Add),
                "multiply"=>(BlendMode.DstColor,BlendMode.OneMinusSrcAlpha,BlendOp.Add),
                "add"=>(BlendMode.One,BlendMode.One,BlendOp.Add),
                "lighten"=>(BlendMode.One,BlendMode.One,BlendOp.Max),
                "darken"=>(BlendMode.One,BlendMode.One,BlendOp.Min),
                "subtract"=>(BlendMode.One,BlendMode.One,BlendOp.ReverseSubtract),
                "alpha"=>(BlendMode.Zero,BlendMode.SrcAlpha,BlendOp.Add),
                "erase"=>(BlendMode.Zero,BlendMode.OneMinusSrcAlpha,BlendOp.Add),
                "invert"=>(BlendMode.OneMinusDstColor,BlendMode.OneMinusSrcAlpha,BlendOp.Add),
                "overlay" or "hardlight" or "difference"=>(BlendMode.One,BlendMode.Zero,BlendOp.Add),
                _=>(BlendMode.One,BlendMode.OneMinusSrcAlpha,BlendOp.Add)   // normal, layer
            };
            material.SetFloat("_SrcBlend",(int)src);material.SetFloat("_DstBlend",(int)dst);material.SetFloat("_BlendOp",(int)op);
            material.SetFloat("_Ref",maskBits);material.SetFloat("_ReadMask",maskBits);
            material.SetFloat("_Comp",(int)(maskBits==0?CompareFunction.Always:CompareFunction.Equal));
            compositeMaterials.Add((blend,maskBits),material);
            return material;
        }
        private RenderTexture Temporary(bool multisampled)
        {
            var free=multisampled?freeMultisampled:freePlain;
            if(!free.TryPop(out var texture))
            {
                texture=new RenderTexture(multisampled?new RenderTextureDescriptor(width,height,RenderTextureFormat.ARGB32,24) {msaaSamples=Msaa,sRGB=false}
                    :new RenderTextureDescriptor(width,height,RenderTextureFormat.ARGB32,0) {sRGB=false}) {hideFlags=HideFlags.HideAndDontSave};
                Create(texture);
            }
            inUse.Add(texture);
            return texture;
        }
        // A target the GPU could not allocate renders nothing and reads back garbage: fail with its size instead.
        private void Create(RenderTexture texture)
        {
            allocated.Add(texture);
            if(!texture.Create()) throw new InvalidDataException($"RVG: видеокарта не выделила холст {texture.width}×{texture.height}, MSAA {texture.antiAliasing}.");
        }
        private void Target(RenderTexture target,bool clearAll,bool clearColor=false)
        {
            cmd.SetRenderTarget(target);current=target;
            if(clearAll) cmd.ClearRenderTarget(RTClearFlags.All,Color.clear,1,0);
            else if(clearColor) cmd.ClearRenderTarget(RTClearFlags.Color,Color.clear,1,0);
        }

        private void Stencil(Mesh mesh,CompareFunction compare,int reference,int readMask,int writeMask,StencilOp op)
        {
            var key=(reference,readMask,writeMask,(int)compare,(int)op);
            if(!stencilMaterials.TryGetValue(key,out var material))
            {
                material=new Material(stencilShader) {hideFlags=HideFlags.HideAndDontSave};
                material.SetFloat("_Ref",reference);material.SetFloat("_ReadMask",readMask);material.SetFloat("_WriteMask",writeMask);
                material.SetFloat("_Comp",(int)compare);material.SetFloat("_Pass",(int)op);
                stencilMaterials.Add(key,material);
            }
            cmd.DrawMesh(mesh,Matrix4x4.identity,material,0,0);
        }

        // 256-entry ramp of a gradient (ratio 0–255), interpolated in sRGB or linear RGB as declared.
        private Texture2D Ramp(RvgFill fill)
        {
            if(ramps.TryGetValue(fill,out var ramp)) return ramp;
            var pixels=new Color32[256];var stops=fill.stops;bool linear=fill.interpolation=="linear-rgb";
            for(int x=0;x<256;x++)
            {
                int k=0;while(k<stops.Length && stops[k][0]<x) k++;
                float[] a=stops[Mathf.Max(0,k-1)],b=stops[Mathf.Min(stops.Length-1,k)];
                float t=b[0]>a[0]?Mathf.Clamp01((x-a[0])/(b[0]-a[0])):0;
                byte Channel(int i)
                {
                    float va=a[i]/255f,vb=b[i]/255f;
                    if(linear && i<4) {va=Mathf.GammaToLinearSpace(va);vb=Mathf.GammaToLinearSpace(vb);float v=Mathf.Lerp(va,vb,t);return (byte)Mathf.RoundToInt(Mathf.LinearToGammaSpace(v)*255);}
                    return (byte)Mathf.RoundToInt(Mathf.Lerp(va,vb,t)*255);
                }
                pixels[x]=new Color32(Channel(1),Channel(2),Channel(3),(byte)Mathf.RoundToInt(Mathf.Lerp(a[4],b[4],t)));
            }
            ramp=new Texture2D(256,1,TextureFormat.RGBA32,false,true) {wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear,hideFlags=HideFlags.HideAndDontSave};
            ramp.SetPixels32(pixels);ramp.Apply(false);
            ramps.Add(fill,ramp);
            return ramp;
        }
        private Texture2D Bitmap(int id)
        {
            if(bitmaps.TryGetValue((document,id),out var texture)) return texture;
            var source=loadBitmap(document.Bitmap(id))??throw new InvalidDataException($"RVG {document.source.archive}: растр {id} не загружен.");
            // A premultiplied copy owned by the rasterizer: the SWF keeps bitmaps premultiplied and smoothing filters them so.
            var pixels=source.GetPixels32();
            for(int i=0;i<pixels.Length;i++)
            {
                var c=pixels[i];
                pixels[i]=new Color32((byte)((c.r*c.a+127)/255),(byte)((c.g*c.a+127)/255),(byte)((c.b*c.a+127)/255),c.a);
            }
            texture=new Texture2D(source.width,source.height,TextureFormat.RGBA32,false,true) {hideFlags=HideFlags.HideAndDontSave,name=source.name};
            texture.SetPixels32(pixels);texture.Apply(false,true);
            bitmaps.Add((document,id),texture);
            return texture;
        }

        private Mesh Mesh(List<Vector3> vertices,List<int> indices)
        {
            var mesh=new Mesh {hideFlags=HideFlags.HideAndDontSave,indexFormat=vertices.Count>65000?IndexFormat.UInt32:IndexFormat.UInt16};
            mesh.SetVertices(vertices);mesh.SetTriangles(indices,0,false);
            meshes.Add(mesh);
            return mesh;
        }
        private Mesh Quad(Rect r,bool uv)
        {
            var mesh=Mesh(QuadVertices(r),QuadIndices);
            if(uv) mesh.SetUVs(0,new List<Vector2> {new(0,0),new(1,0),new(1,1),new(0,1)});
            return mesh;
        }
        private static List<Vector3> QuadVertices(Rect r)=>new() {new(r.xMin,r.yMin),new(r.xMax,r.yMin),new(r.xMax,r.yMax),new(r.xMin,r.yMax)};
        private static readonly List<int> QuadIndices=new() {0,1,2,0,2,3};
        private static Vector4 ToColor(int[] c)=>new(c[0]/255f,c[1]/255f,c[2]/255f,c[3]/255f);
        private static int BitCount(int bits) {int n=0;while(bits!=0) {n+=bits&1;bits>>=1;}return n;}
        private void Count(string what) {Approximations.TryGetValue(what,out int n);Approximations[what]=n+1;}

        public void Dispose()
        {
            cmd.Release();
            foreach(var material in stencilMaterials.Values) UnityEngine.Object.DestroyImmediate(material);
            foreach(var material in paintMaterials.Values) UnityEngine.Object.DestroyImmediate(material);
            foreach(var material in compositeMaterials.Values) UnityEngine.Object.DestroyImmediate(material);
            if(filterMaterial!=null) UnityEngine.Object.DestroyImmediate(filterMaterial);
            foreach(var ramp in ramps.Values) UnityEngine.Object.DestroyImmediate(ramp);
            if(finalMaterial!=null) UnityEngine.Object.DestroyImmediate(finalMaterial);
            foreach(var bitmap in bitmaps.Values) UnityEngine.Object.DestroyImmediate(bitmap);
            stencilMaterials.Clear();paintMaterials.Clear();compositeMaterials.Clear();ramps.Clear();bitmaps.Clear();
        }
    }

    // Flash color transform: c' = c·mul + add/255 on straight color; children apply theirs first.
    public readonly struct ColorTransform
    {
        public readonly Vector4 Mul,Add;
        public ColorTransform(Vector4 mul,Vector4 add) {Mul=mul;Add=add;}
        public static readonly ColorTransform Identity=new(Vector4.one,Vector4.zero);
        // This (the parent's) applied after the child's own transform.
        public ColorTransform After(RvgColorTransform child)
        {
            if(child==null) return this;
            var cm=new Vector4(child.mul[0],child.mul[1],child.mul[2],child.mul[3]);
            var ca=new Vector4(child.add[0],child.add[1],child.add[2],child.add[3])/255f;
            return new ColorTransform(Vector4.Scale(cm,Mul),Vector4.Scale(ca,Mul)+Add);
        }
    }
}
