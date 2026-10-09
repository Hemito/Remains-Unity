using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Remains.Content;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Remains.Editor
{
    // Diagnostic only (no asset writes): one pony frame drawn from the compiled render asset with
    // (A) the current textures, (B) a candidate texture folder as is (mipmaps on), (C) the same
    // compressed in memory (BC7), at a game-like and a close-up scale, side by side.
    //   -executeMethod Remains.Editor.PonyResolutionPreview.RenderBatch -ponyTextures <folder of <name>.png> -ponyPreviewOut <file.png> [-ponyClip idle] [-ponyFrame 0] [-ponySupersample N]
    // -ponySupersample N: each panel is composed N× larger and box-filtered down (seam check).
    // -ponyParts: instead of the comparison, a grid with each visible part highlighted in red over the
    //   pony (current textures), labelled by index, shape path and texture (find which part draws a seam).
    // -ponyFixUv Z: candidate textures were exported at zoom Z with the canvas rounded up to whole
    // pixels; map only the shape's exact span (span·Z / canvas) instead of stretching the whole canvas.
    public static class PonyResolutionPreview
    {
        private static int supersample=1;
        private static float fixUvZoom;
        private static Dictionary<string,float[]> shapeBounds;
        public static void RenderBatch()
        {
            int code=0;
            try
            {
                string folder=Argument("-ponyTextures")??throw new ArgumentException("Нет аргумента -ponyTextures <папка>.");
                string output=Argument("-ponyPreviewOut")??throw new ArgumentException("Нет аргумента -ponyPreviewOut <файл.png>.");
                string clipId=Argument("-ponyClip")??"idle";
                int frameIndex=int.Parse(Argument("-ponyFrame")??"0");
                supersample=Math.Max(1,int.Parse(Argument("-ponySupersample")??"1"));
                fixUvZoom=float.Parse(Argument("-ponyFixUv")??"0",System.Globalization.CultureInfo.InvariantCulture);
                if(fixUvZoom>0)
                {
                    shapeBounds=new Dictionary<string,float[]>();
                    var library=JsonUtility.FromJson<PonyLibrary>(Resources.Load<TextAsset>("Original/Pony/pony").text);
                    foreach(var c in library.clips) foreach(var f in c.frames) foreach(var p in f.parts) shapeBounds[p.texture]=p.bounds;
                }
                PonyAppearance.Prepare();
                var asset=Resources.Load<PonyRenderAsset>("Original/Pony/render")??throw new InvalidOperationException("Нет Original/Pony/render.");
                var clip=asset.Clips.FirstOrDefault(c=>c.id==clipId)??throw new InvalidOperationException("Нет позы "+clipId);
                var frame=clip.frames[frameIndex];
                var parts=frame.parts.Where(p=>p.visible && PonyAppearance.Matches(p.hairStyle,p.eyeStyle,p.slot,1,1,false)).OrderBy(p=>p.drawOrder).ToArray();

                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-ponyParts")>=0) {PartsGrid(parts,frame.bounds,output);EditorApplication.Exit(0);return;}
                var candidates=new Dictionary<Texture,Texture2D>();var compressed=new Dictionary<Texture,Texture2D>();
                long bytesNow=0,bytesCandidate=0,bytesCompressed=0;
                foreach(var part in parts)
                {
                    var current=part.material.mainTexture;
                    if(candidates.ContainsKey(current)) continue;
                    string file=Path.Combine(folder,current.name+".png");
                    if(!File.Exists(file)) throw new FileNotFoundException("Нет картинки-кандидата "+file);
                    var tex=new Texture2D(2,2,TextureFormat.RGBA32,true,false){name=current.name,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
                    tex.LoadImage(File.ReadAllBytes(file),false);
                    candidates.Add(current,tex);
                    var bc=new Texture2D(2,2,TextureFormat.RGBA32,true,false){name=current.name+" BC7",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
                    bc.LoadImage(File.ReadAllBytes(file),false);
                    EditorUtility.CompressTexture(bc,TextureFormat.BC7,TextureCompressionQuality.Best);
                    compressed.Add(current,bc);
                    bytesNow+=UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(current);
                    bytesCandidate+=UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(tex);
                    bytesCompressed+=UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(bc);
                }

                var bounds=frame.bounds;bounds.Expand(0.1f);
                var panels=new List<Texture2D>();
                foreach(float pixelsPerMeter in new[]{100f,600f})
                    foreach(int set in new[]{0,1,2})
                    {
                        var panel=Draw(parts,bounds,pixelsPerMeter,p=>set==0?p.material.mainTexture:set==1?candidates[p.material.mainTexture]:compressed[p.material.mainTexture]);
                        panels.Add(pixelsPerMeter<200?Enlarge(panel,3):panel);   // game scale shown ×3 without smoothing
                    }
                int w=Math.Max(panels.Take(3).Sum(p=>p.width),panels.Skip(3).Sum(p=>p.width))+20,h=panels[0].height+panels[3].height+10;
                var sheet=new Texture2D(w,h,TextureFormat.RGBA32,false);
                var fill=new Color32[w*h];for(int i=0;i<fill.Length;i++) fill[i]=new Color32(40,40,48,255);sheet.SetPixels32(fill);
                int x=0;for(int i=0;i<3;i++){Blit(sheet,panels[i],x,panels[3].height+10);x+=panels[i].width+10;}
                x=0;for(int i=3;i<6;i++){Blit(sheet,panels[i],x,0);x+=panels[i].width+10;}
                File.WriteAllBytes(output,sheet.EncodeToPNG());
                Debug.Log($"Пони {clipId}:{frameIndex}: частей {parts.Length}, текстур {candidates.Count}; память этих текстур: сейчас {bytesNow/1024} КБ, кандидат {bytesCandidate/1024} КБ, кандидат BC7 {bytesCompressed/1024} КБ. Колонки: сейчас | кандидат | кандидат BC7; сверху 100 px/м (показано ×3 без сглаживания), снизу 600 px/м. {output}");
            }
            catch(Exception error) {Debug.LogError("Сравнение графики пони прервано: "+error);code=1;}
            EditorApplication.Exit(code);
        }

        private static Texture2D Draw(PonyRenderPart[] parts,Bounds bounds,float pixelsPerMeter,Func<PonyRenderPart,Texture> texture)
        {
            if(supersample>1)
            {
                var big=DrawOnce(parts,bounds,pixelsPerMeter*supersample,texture);
                int w=big.width/supersample,h=big.height/supersample;var src=big.GetPixels();var dst=new Color[w*h];
                for(int y=0;y<h;y++) for(int x=0;x<w;x++)
                {
                    Color sum=default;
                    for(int j=0;j<supersample;j++) for(int i=0;i<supersample;i++) sum+=src[(y*supersample+j)*big.width+x*supersample+i];
                    dst[y*w+x]=sum/(supersample*supersample);
                }
                var small=new Texture2D(w,h,TextureFormat.RGBA32,false);small.SetPixels(dst);small.Apply();
                return small;
            }
            return DrawOnce(parts,bounds,pixelsPerMeter,texture);
        }

        private static Texture2D DrawOnce(PonyRenderPart[] parts,Bounds bounds,float pixelsPerMeter,Func<PonyRenderPart,Texture> texture,Action<int,MaterialPropertyBlock> modify=null)
        {
            int width=Mathf.CeilToInt(bounds.size.x*pixelsPerMeter),height=Mathf.CeilToInt(bounds.size.y*pixelsPerMeter);
            var target=new RenderTexture(width,height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            var cmd=new CommandBuffer{name="Пони"};
            cmd.SetRenderTarget(target);cmd.ClearRenderTarget(false,true,new Color(.16f,.16f,.19f,1));
            cmd.SetViewProjectionMatrices(Matrix4x4.identity,GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(bounds.min.x,bounds.max.x,bounds.min.y,bounds.max.y,-10,10),false));
            Shader.SetGlobalFloat("_WorldLightEnabled",0);
            var block=new MaterialPropertyBlock();
            for(int index=0;index<parts.Length;index++)
            {
                var part=parts[index];
                block.Clear();
                block.SetTexture("_MainTex",texture(part));
                block.SetColor("_Color",PonyAppearance.Tint(part.slot,1));
                block.SetFloat("_Emission",part.slot=="magic"?1:0);
                modify?.Invoke(index,block);
                var tex=texture(part);
                cmd.DrawMesh(fixUvZoom>0 && tex!=part.material.mainTexture?FixedUv(part.mesh,shapeBounds[part.material.mainTexture.name],tex):part.mesh,Matrix4x4.identity,part.material,0,0,block);
            }
            Graphics.ExecuteCommandBuffer(cmd);cmd.Release();
            var previous=RenderTexture.active;RenderTexture.active=target;
            var image=new Texture2D(width,height,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
            RenderTexture.active=previous;target.Release();UnityEngine.Object.DestroyImmediate(target);
            return image;
        }

        private static void PartsGrid(PonyRenderPart[] parts,Bounds bounds,string output)
        {
            bounds.Expand(0.1f);
            const float ppm=260f;const int columns=6;
            var tiles=new List<Texture2D>();
            for(int i=0;i<parts.Length;i++)
            {
                int highlight=i;
                tiles.Add(DrawOnce(parts,bounds,ppm,p=>p.material.mainTexture,(index,block)=>{if(index==highlight) block.SetColor("_Color",new Color(1,0,0,1));}));
            }
            int tw=tiles[0].width,th=tiles[0].height,rows=(tiles.Count+columns-1)/columns;
            var sheet=new Texture2D(columns*tw,rows*th,TextureFormat.RGBA32,false);
            for(int i=0;i<tiles.Count;i++) sheet.SetPixels((i%columns)*tw,(rows-1-i/columns)*th,tw,th,tiles[i].GetPixels());
            File.WriteAllBytes(output,sheet.EncodeToPNG());
            for(int i=0;i<parts.Length;i++) Debug.Log($"Часть {i}: {parts[i].sourcePath} текстура {parts[i].material.mainTexture.name} область {parts[i].slot} порядок {parts[i].drawOrder}");
        }

        private static readonly Dictionary<(Mesh,Texture),Mesh> fixedMeshes=new();
        private static Mesh FixedUv(Mesh mesh,float[] bounds,Texture texture)
        {
            if(fixedMeshes.TryGetValue((mesh,texture),out var cached)) return cached;
            float sx=(bounds[2]-bounds[0])/20f*fixUvZoom/texture.width,sy=(bounds[3]-bounds[1])/20f*fixUvZoom/texture.height;
            var copy=UnityEngine.Object.Instantiate(mesh);
            // Vertex order (Quad): (xmin,ymax),(xmax,ymax),(xmin,ymin),(xmax,ymin) in Flash Y-down; canvas top-left = (xmin,ymin).
            copy.uv=new[]{new Vector2(0,1-sy),new Vector2(sx,1-sy),new Vector2(0,1),new Vector2(sx,1)};
            fixedMeshes[(mesh,texture)]=copy;
            return copy;
        }

        private static Texture2D Enlarge(Texture2D source,int factor)
        {
            var result=new Texture2D(source.width*factor,source.height*factor,TextureFormat.RGBA32,false);
            var pixels=source.GetPixels32();var output=new Color32[result.width*result.height];
            for(int y=0;y<result.height;y++) for(int x=0;x<result.width;x++) output[y*result.width+x]=pixels[(y/factor)*source.width+x/factor];
            result.SetPixels32(output);return result;
        }

        private static void Blit(Texture2D sheet,Texture2D panel,int x,int y)=>sheet.SetPixels(x,y,panel.width,panel.height,panel.GetPixels());

        private static string Argument(string name)
        {
            var args=Environment.GetCommandLineArgs();
            for(int i=0;i<args.Length-1;i++) if(args[i]==name) return args[i+1];
            return null;
        }
    }
}
