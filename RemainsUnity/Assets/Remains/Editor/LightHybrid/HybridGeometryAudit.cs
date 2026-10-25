using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using Remains.RoomFormat;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    // Geometry-only audit of the prototype's actual GPU downsample; no light transport or runtime edits.
    public static class HybridGeometryAudit
    {
        [Serializable] private sealed class Frame {public string room;public Rect roomBounds;}
        [Serializable] private sealed class Input {public Frame[] frames;}
        [Serializable] private sealed class Result
        {
            public string room;public int divisor,fineComponents,coarseComponents,splitFineComponents,lostFineComponents,largestSplitPieces,lostOpenTexels,expandedOpaqueTexels;
            public Vector2 splitA,splitB;
        }
        [Serializable] private sealed class Report
        {
            public string method="Fine/coarse alpha >=.99 is opaque. Four-neighbour open connectivity, own-room core excludes .8m boundary. A fine connected component mapping to multiple coarse connected components is a split opening; components mapping to no open coarse cell are lost. Does not prove optical ray visibility or rule out all diagonal routes. Uses the exact prototype GPU downsample, closed doors, all lamp on-frames.";
            public Result[] results;
        }
        public static void RunBatch()
        {
            GameObject visual=null;HybridScenePrototype fine=null;RenderTexture zero=null,coarse=null,seed=null;
            try
            {
                string output=Path.GetFullPath("../.reference/light-hybrid");var input=JsonUtility.FromJson<Input>(File.ReadAllText(Path.Combine(output,"report.json")));
                Anatomy.Load();WeaponCatalog.Load();var world=new WorldState {seed=104};
                FactoryPreparation.Create(world,AssetDatabase.LoadAssetAtPath<FactoryCatalogAsset>(FactoryCatalogAuthoring.AssetPath),0);
                FactoryContentSources.Prepare(world,"factory:0");var authored=RoomAuthoring.Load("factory:0",world);
                DoorContentInstaller.Apply(world,RoomDoorContent.Read(authored,"factory:0"));var layout=authored.CreateLayout(false);
                visual=AuthoredRoomVisual.Create("factory:0",layout,world);var view=visual.GetComponent<FactoryLevelView>();view.Synchronize(layout,world,1);
                var factory=visual.GetComponent<FactoryLevelRoot>();var lamps=typeof(FactoryLevelView).GetField("lamps",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view);
                typeof(LightHybridPreview).GetMethod("ForceOn",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new[]{lamps,(object)factory});
                var doors=world.doors.Where(d=>d.level=="factory:0").ToArray();foreach(var door in doors) door.open=false;
                view.SyncDoors(doors,100);view.SyncLamps(Array.Empty<string>(),101);fine=new HybridScenePrototype(view,factory);
                zero=Target(fine.Width,fine.Height);var old=RenderTexture.active;RenderTexture.active=zero;GL.Clear(false,true,Color.clear);RenderTexture.active=old;
                var opaque=ReadOpaque((RenderTexture)fine.Scene);var results=new List<Result>();
                var compute=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Remains/Editor/LightHybrid/HybridBounce.compute");int kernel=compute.FindKernel("DownsampleAndSeed");
                foreach(int divisor in new[]{2,4})
                {
                    int width=fine.Width/divisor,height=fine.Height/divisor;coarse=Target(width,height);seed=Target(width,height);
                    compute.SetInts("_FineSize",fine.Width,fine.Height);compute.SetInts("_CoarseSize",width,height);compute.SetInt("_Divisor",divisor);compute.SetFloat("_WallGain",0);
                    compute.SetTexture(kernel,"_FineScene",fine.Scene);compute.SetTexture(kernel,"_FineDirect",zero);compute.SetTexture(kernel,"_CoarseScene",coarse);compute.SetTexture(kernel,"_FirstBounce",seed);
                    compute.Dispatch(kernel,(width+7)/8,(height+7)/8,1);var coarseOpaque=ReadOpaque(coarse);
                    foreach(var sample in input.frames.GroupBy(f=>f.room).Select(g=>g.First()))
                    {
                        int x=Mathf.RoundToInt((sample.roomBounds.x-fine.WorldRect.x+.8f)*fine.FineResolution),y=Mathf.RoundToInt((sample.roomBounds.y-fine.WorldRect.y+.8f)*fine.FineResolution);
                        int w=Mathf.RoundToInt((sample.roomBounds.width-1.6f)*fine.FineResolution),h=Mathf.RoundToInt((sample.roomBounds.height-1.6f)*fine.FineResolution);
                        if(x%divisor!=0 || y%divisor!=0 || w%divisor!=0 || h%divisor!=0) throw new InvalidOperationException("Ядро комнаты не совмещено с coarse-сеткой: "+sample.room);
                        int[] fineLabels=Label(opaque,fine.Width,x,y,w,h,out int fineCount),coarseLabels=Label(coarseOpaque,width,x/divisor,y/divisor,w/divisor,h/divisor,out int coarseCount);
                        var sets=new Dictionary<int,Dictionary<int,Vector2>>();var sizes=new Dictionary<int,int>();
                        var result=new Result {room=sample.room,divisor=divisor,fineComponents=fineCount,coarseComponents=coarseCount};
                        for(int py=0;py<h;py++)for(int px=0;px<w;px++)
                        {
                            int label=fineLabels[py*w+px];if(label==0) continue;
                            if(!sets.TryGetValue(label,out var set)){set=new Dictionary<int,Vector2>();sets.Add(label,set);sizes.Add(label,0);}sizes[label]++;
                            int c=coarseLabels[(py/divisor)*(w/divisor)+px/divisor];
                            if(c==0){result.expandedOpaqueTexels++;continue;}
                            if(!set.ContainsKey(c))set.Add(c,new Vector2(fine.WorldRect.x+(x+px+.5f)/fine.FineResolution,fine.WorldRect.y+(y+py+.5f)/fine.FineResolution));
                        }
                        foreach(var pair in sets)
                        {
                            if(pair.Value.Count==0){result.lostFineComponents++;result.lostOpenTexels+=sizes[pair.Key];}
                            if(pair.Value.Count>1){result.splitFineComponents++;if(pair.Value.Count>result.largestSplitPieces){result.largestSplitPieces=pair.Value.Count;result.splitA=pair.Value.Values.First();result.splitB=pair.Value.Values.Skip(1).First();}}
                        }
                        results.Add(result);
                    }
                    Release(coarse);Release(seed);coarse=seed=null;
                }
                var report=new Report {results=results.ToArray()};File.WriteAllText(Path.Combine(output,"geometry.json"),JsonUtility.ToJson(report,true));
                var md=new StringBuilder("# Консервативная сетка: узкие проходы\n\n"+report.method+"\n\n| Комната | Сетка | Fine/coarse областей | Разделённых областей | Полностью потеряно | Закрытых fine texel |\n|---|---|---:|---:|---:|---:|\n");
                foreach(var r in results)md.AppendLine($"| {r.room} | 1/{r.divisor} | {r.fineComponents}/{r.coarseComponents} | {r.splitFineComponents} | {r.lostFineComponents} | {r.expandedOpaqueTexels} |");
                File.WriteAllText(Path.Combine(output,"geometry.md"),md.ToString());Debug.Log("Гибрид, топология: измерены 5 комнат × 2 сетки, без повторного расчёта освещения.");EditorApplication.Exit(0);
            }
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
            finally{fine?.Dispose();Release(zero);Release(coarse);Release(seed);if(visual!=null)Object.DestroyImmediate(visual);FactoryContentSources.Clear();RoomRadianceField.ClearSpareTextures();}
        }
        private static RenderTexture Target(int w,int h)
        {var t=new RenderTexture(w,h,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear){enableRandomWrite=true};if(!t.Create())throw new InvalidOperationException("Не создан RT аудита.");return t;}
        private static void Release(RenderTexture t){if(t!=null){t.Release();Object.DestroyImmediate(t);}}
        private static bool[] ReadOpaque(RenderTexture texture)
        {var image=new Texture2D(texture.width,texture.height,TextureFormat.RGBAHalf,false,true);var old=RenderTexture.active;try{RenderTexture.active=texture;image.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);return image.GetPixels().Select(c=>c.a>=.99f).ToArray();}finally{RenderTexture.active=old;Object.DestroyImmediate(image);}}
        private static int[] Label(bool[] opaque,int stride,int x,int y,int w,int h,out int count)
        {
            var labels=new int[w*h];var queue=new int[w*h];count=0;
            for(int py=0;py<h;py++)for(int px=0;px<w;px++)
            {
                int start=py*w+px;if(labels[start]!=0 || opaque[(y+py)*stride+x+px])continue;
                count++;int head=0,tail=0;labels[start]=count;queue[tail++]=start;
                while(head<tail)
                {
                    int p=queue[head++],cx=p%w,cy=p/w;
                    Visit(cx-1,cy);Visit(cx+1,cy);Visit(cx,cy-1);Visit(cx,cy+1);
                    void Visit(int nx,int ny){if(nx<0 || ny<0 || nx>=w || ny>=h)return;int n=ny*w+nx;if(labels[n]!=0 || opaque[(y+ny)*stride+x+nx])return;labels[n]=labels[p];queue[tail++]=n;}
                }
            }
            return labels;
        }
    }
}
