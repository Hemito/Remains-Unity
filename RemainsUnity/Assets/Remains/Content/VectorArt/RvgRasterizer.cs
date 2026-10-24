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
    // Masks (clipDepth) live in stencil bits 1–7, one per nesting level. Antialiasing: MSAA on the canvas. Geometry is
    // transformed on the CPU into canvas pixels (Y up inside, top-left registration outside). Values are raw sRGB bytes:
    // linear textures and canvas, so blending happens in gamma space as in Flash.
    public sealed class RvgRasterizer : IDisposable
    {
        // Hairline rule (contract: established by the converter probe): a stroke thinner than this many canvas pixels
        // is drawn at this width; a zero-width stroke is a hairline of this width.
        public float MinLinePixels=1;
        public int Msaa=8;
        // Effects not yet rasterized (group blend modes, filters): counted and reported, never silently dropped.
        public readonly Dictionary<string,int> Approximations=new(StringComparer.Ordinal);

        private const int MaskLevels=7;
        private readonly Func<RvgBitmap,Texture2D> loadBitmap;
        private readonly Shader stencilShader,paintShader,finalShader;
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

        public RvgRasterizer(Func<RvgBitmap,Texture2D> loadBitmap)
        {
            this.loadBitmap=loadBitmap??throw new ArgumentNullException(nameof(loadBitmap));
            stencilShader=GameAssets.Load<Shader>("Presentation/RvgStencil")??throw new InvalidOperationException("Нет шейдера Presentation/RvgStencil.");
            paintShader=GameAssets.Load<Shader>("Presentation/RvgPaint")??throw new InvalidOperationException("Нет шейдера Presentation/RvgPaint.");
            finalShader=GameAssets.Load<Shader>("Presentation/RvgFinal")??throw new InvalidOperationException("Нет шейдера Presentation/RvgFinal.");
        }

        // The frame as a straight-alpha RGBA32 texture (sRGB) of ceil(size × zoom); registration at origin × zoom from the top-left.
        public Texture2D Render(RvgDocument source,RvgResource resource,RvgResourceFrame frame,float scale)
        {
            if(source==null || resource==null || frame==null) throw new ArgumentNullException(nameof(source));
            if(!float.IsFinite(scale) || scale<=0) throw new ArgumentOutOfRangeException(nameof(scale));
            document=source;zoom=scale;
            width=Mathf.CeilToInt(frame.size[0]*zoom-1e-4f);height=Mathf.CeilToInt(frame.size[1]*zoom-1e-4f);
            if(width<1 || height<1 || width>16384 || height>16384) throw new InvalidDataException($"RVG {resource.id}, кадр {frame.frame}: холст {width}×{height}.");
            var sprite=document.Sprite(resource.sprite);
            int index=frame.frame-1;
            if(index<0 || index>=sprite.frameCount) throw new InvalidDataException($"RVG {resource.id}: кадр {frame.frame} вне 1…{sprite.frameCount}.");
            // twips → top-left pixels → canvas (Y up).
            var root=Affine.Scale(zoom/20f).Then(Affine.Translate(frame.origin[0]*zoom,frame.origin[1]*zoom)).Then(new Affine(1,0,0,-1,0,height));
            var canvas=RenderTexture.GetTemporary(new RenderTextureDescriptor(width,height,RenderTextureFormat.ARGB32,24) {msaaSamples=Msaa,sRGB=false});
            var resolved=RenderTexture.GetTemporary(new RenderTextureDescriptor(width,height,RenderTextureFormat.ARGB32,0) {sRGB=false});
            try
            {
                cmd.Clear();
                cmd.SetRenderTarget(canvas);
                cmd.ClearRenderTarget(RTClearFlags.All,Color.clear,1,0);
                cmd.SetViewProjectionMatrices(Matrix4x4.identity,GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(0,width,0,height,-1,1),false));
                DrawSprite(sprite,index,root,ColorTransform.Identity,0,"normal");
                // Resolve (MSAA) and un-premultiply into the readable target.
                finalMaterial??=new Material(finalShader) {hideFlags=HideFlags.HideAndDontSave};
                cmd.SetRenderTarget(resolved);
                cmd.ClearRenderTarget(RTClearFlags.Color,Color.clear,1,0);
                block.Clear();block.SetTexture("_MainTex",canvas);
                cmd.DrawMesh(Quad(new Rect(0,0,width,height),true),Matrix4x4.identity,finalMaterial,0,0,block);
                Graphics.ExecuteCommandBuffer(cmd);
                var result=new Texture2D(width,height,TextureFormat.RGBA32,false,false) {name=$"{resource.id}#{frame.frame}@{zoom}"};
                var previous=RenderTexture.active;
                try {RenderTexture.active=resolved;result.ReadPixels(new Rect(0,0,width,height),0,0,false);result.Apply(false);}
                finally {RenderTexture.active=previous;}
                return result;
            }
            finally
            {
                cmd.Clear();
                RenderTexture.ReleaseTemporary(canvas);RenderTexture.ReleaseTemporary(resolved);
                foreach(var mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh);
                meshes.Clear();
            }
        }

        // Objects in depth order; a clipDepth object starts a mask level for the depths up to its clipDepth.
        private void DrawSprite(RvgSprite sprite,int frameIndex,in Affine m,in ColorTransform color,int maskBits,string inherited)
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
                // A sprite's blend applies to its shapes one by one (approximation of the group, counted below).
                string blend=placed.blend is "normal" or "layer"?inherited:placed.blend;
                if(placed.filters.Count>0) Count("фильтры: "+string.Join(",",placed.filters.ConvertAll(f=>f.type)));
                if(Unsupported(blend)) {Count("наложение "+blend);blend="normal";}
                DrawObject(placed,om,oc,maskBits,blend);
            }
            for(int i=clips.Count-1;i>=0;i--) ClearBit(clips[i].Bit);
        }
        private void DrawObject(RvgObject placed,in Affine m,in ColorTransform color,int maskBits,string blend)
        {
            switch(placed.kind)
            {
                case "shape": DrawShape(document.Shape(placed.character),m,color,maskBits,blend);break;
                case "sprite":
                    var child=document.Sprite(placed.character);
                    if(blend!="normal") Count("наложение спрайта "+blend+" по фигурам");
                    DrawSprite(child,placed.spriteFrame,m,color,maskBits,blend);
                    break;
                case "bitmap":
                    var bitmap=document.Bitmap(placed.character);
                    var fill=new RvgFill {type="bitmap",bitmap=bitmap.id,matrix=new float[]{20,0,0,20,0,0},repeat=false,smooth=true};
                    var shape=new RvgShape {id=-bitmap.id,bounds=new[]{0,0,bitmap.width*20,bitmap.height*20}};
                    shape.fills.Add(fill);
                    shape.paths.Add(new RvgPath {fill=0,commands=new[]{0,0,0,1,bitmap.width*20,0,1,bitmap.width*20,bitmap.height*20,1,0,bitmap.height*20,3}});
                    DrawShape(shape,m,color,maskBits,blend);
                    break;
            }
        }
        private void DrawShape(RvgShape shape,in Affine m,in ColorTransform color,int maskBits,string blend)
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
                Paint(fill,m,color,bounds,maskBits,blend);
            }
        }
        private float LineWidth(RvgLine line,in Affine m)
        {
            float pixels=line.noHScale && line.noVScale?line.width/20f*zoom:line.width*Mathf.Sqrt(Mathf.Abs(m.Determinant));
            return Mathf.Max(pixels,MinLinePixels);
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

        private void Paint(RvgFill fill,in Affine m,in ColorTransform color,Rect bounds,int maskBits,string blend)
        {
            bounds=Rect.MinMaxRect(bounds.xMin-1,bounds.yMin-1,bounds.xMax+1,bounds.yMax+1);
            var material=PaintMaterial(fill.type,maskBits,blend);
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
        private Material PaintMaterial(string type,int maskBits,string blend)
        {
            // Paint where the active masks and parity are set; clear parity.
            var key=(type,maskBits|1,maskBits|1,1,(int)CompareFunction.Equal,(int)StencilOp.Zero,blend);
            if(paintMaterials.TryGetValue(key,out var material)) return material;
            material=new Material(paintShader) {hideFlags=HideFlags.HideAndDontSave};
            material.EnableKeyword("_FILL_"+type.ToUpperInvariant());
            material.SetFloat("_Ref",key.Item2);material.SetFloat("_ReadMask",key.Item3);material.SetFloat("_WriteMask",1);
            material.SetFloat("_Comp",(int)CompareFunction.Equal);material.SetFloat("_Pass",(int)StencilOp.Zero);
            var (src,dst,op)=Blend(blend);
            material.SetFloat("_SrcBlend",(int)src);material.SetFloat("_DstBlend",(int)dst);material.SetFloat("_BlendOp",(int)op);
            paintMaterials.Add(key,material);
            return material;
        }
        // Premultiplied blending; group-only modes (difference, overlay, hardlight, invert, alpha, erase) are approximated.
        private static bool Unsupported(string blend)=>blend is "difference" or "overlay" or "hardlight" or "invert" or "alpha" or "erase";
        private static (BlendMode,BlendMode,BlendOp) Blend(string blend)=>blend switch
        {
            "screen"=>(BlendMode.One,BlendMode.OneMinusSrcColor,BlendOp.Add),
            "multiply"=>(BlendMode.DstColor,BlendMode.OneMinusSrcAlpha,BlendOp.Add),
            "add"=>(BlendMode.One,BlendMode.One,BlendOp.Add),
            "lighten"=>(BlendMode.One,BlendMode.One,BlendOp.Max),
            "darken"=>(BlendMode.One,BlendMode.One,BlendOp.Min),
            "subtract"=>(BlendMode.One,BlendMode.One,BlendOp.ReverseSubtract),
            _=>(BlendMode.One,BlendMode.OneMinusSrcAlpha,BlendOp.Add)
        };
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
            texture=loadBitmap(document.Bitmap(id))??throw new InvalidDataException($"RVG {document.source.archive}: растр {id} не загружен.");
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
            foreach(var ramp in ramps.Values) UnityEngine.Object.DestroyImmediate(ramp);
            if(finalMaterial!=null) UnityEngine.Object.DestroyImmediate(finalMaterial);
            stencilMaterials.Clear();paintMaterials.Clear();ramps.Clear();bitmaps.Clear();
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
