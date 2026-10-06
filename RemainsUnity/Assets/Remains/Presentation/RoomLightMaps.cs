using System;
using System.Collections.Generic;
using System.Threading;
using Remains.Domain;
using UnityEngine;

namespace Remains.Presentation
{
    // One visible-room binding. PrepareBuild alone captures Unity assets;
    // single-use Build and CreatePatch consume only frozen CPU data on a worker.
    public sealed class RoomLightMaps
    {
        private readonly object gate=new();
        private readonly RoomLightMap.PreparedProfile foregroundProfile,backgroundProfile;
        private readonly Color[] foreground,background;
        private Occlusion published;
        private long version;
        public int Width {get;}
        public int Height {get;}
        public Rect Bounds {get;}
        public int SourceCount {get;}
        public int UniqueDecodedTextures {get;}
        public long DecodedPixels {get;}
        public double PrepareMilliseconds {get;}
        public double GetPixelsMilliseconds {get;}
        public double DecodeMilliseconds {get;}
        public double EvaluateMilliseconds {get;}
        public long PublishedVersion {get {lock(gate) return version;}}
        public IReadOnlyList<Color> Foreground {get;}
        public IReadOnlyList<Color> Background {get;}

        public RoomLightMaps(RoomLayout room,Rect bounds,RoomLightProfile profile):this(PrepareBuild(room,bounds,profile).CreateData(default)) { }
        private RoomLightMaps(BuildData data)
        {
            var plan=data.Plan;
            Bounds=plan.Bounds;Width=plan.Width;Height=plan.Height;
            SourceCount=plan.SourceCount;UniqueDecodedTextures=plan.UniqueDecodedTextures;DecodedPixels=plan.DecodedPixels;
            PrepareMilliseconds=plan.PrepareMilliseconds;GetPixelsMilliseconds=plan.GetPixelsMilliseconds;
            DecodeMilliseconds=data.DecodeMilliseconds;EvaluateMilliseconds=data.EvaluateMilliseconds;
            foregroundProfile=data.ForegroundProfile;backgroundProfile=data.BackgroundProfile;published=plan.InitialOcclusion;
            foreground=data.Foreground;background=data.Background;
            Foreground=Array.AsReadOnly(foreground);Background=Array.AsReadOnly(background);
        }
        // Explicit main-thread boundary: no decode or full-map sampling is performed here.
        public static BuildPlan PrepareBuild(RoomLayout room,Rect bounds,RoomLightProfile profile)
        {
            if(room==null) throw new ArgumentNullException(nameof(room));
            var timer=System.Diagnostics.Stopwatch.StartNew();
            RoomLightMap.ValidateBounds(room.id,bounds);
            var cache=new RoomLightMap.MaskCache();
            var first=RoomLightMap.Capture(room.id,profile,cache);
            var second=RoomLightMap.Capture(room.id,profile.ForBackground(),cache);
            var occlusion=CaptureOcclusion(room.platforms);var raw=cache.RawPixels();
            return new BuildPlan(room.id,bounds,first,second,occlusion,raw,cache.PixelCount,
                timer.Elapsed.TotalMilliseconds,cache.GetPixelsMilliseconds);
        }

        public static Occlusion CaptureOcclusion(RoomSurface[] surfaces)
        {
            if(surfaces==null) throw new InvalidOperationException("Нет поверхностей для снимка препятствий света.");
            var solids=new List<Rect>();
            for(int i=0;i<surfaces.Length;i++)
            {
                var surface=surfaces[i];
                if(surface==null) throw new InvalidOperationException($"Пустая поверхность для света: index={i}.");
                if(surface.oneWay) continue;
                if(!float.IsFinite(surface.x) || !float.IsFinite(surface.y) || !float.IsFinite(surface.width) || !float.IsFinite(surface.height) ||
                    surface.width<=0 || surface.height<=0 || !float.IsFinite(surface.x+surface.width) || !float.IsFinite(surface.y+surface.height))
                    throw new InvalidOperationException($"Некорректное препятствие света: index={i}, texture={surface.texture}, rect=({surface.x},{surface.y},{surface.width},{surface.height}).");
                solids.Add(new Rect(surface.x,surface.y,surface.width,surface.height));
            }
            solids.Sort(CompareRects);
            // IDs and duplicate/coincident volumes do not change a boolean occlusion rule.
            int count=0;
            for(int i=0;i<solids.Count;i++) if(count==0 || CompareRects(solids[i],solids[count-1])!=0) solids[count++]=solids[i];
            if(count<solids.Count) solids.RemoveRange(count,solids.Count-count);
            return new Occlusion(solids.ToArray());
        }

        public Patch CreatePatch(Occlusion target,CancellationToken cancellation=default)
        {
            if(target==null) throw new ArgumentNullException(nameof(target));
            cancellation.ThrowIfCancellationRequested();
            Occlusion baseline;long baseVersion;
            lock(gate) {baseline=published;baseVersion=version;}
            var changed=ChangedRects(baseline,target,cancellation);
            if(changed.Count==0) return new Patch(this,baseVersion,target,Array.Empty<int>(),Array.Empty<Color>(),Array.Empty<Color>(),0);

            // 0=not visited, 1=conservative candidate, 2=affected by a changed ray.
            var marks=new byte[foreground.Length];var indices=new List<int>();int candidates=0;
            foreach(var light in foregroundProfile.Lights)
            {
                cancellation.ThrowIfCancellationRequested();
                if(light.Shadow) continue; // Overlay alpha has no geometry dependency.
                bool relevant=false;
                for(int i=0;i<changed.Count;i++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if(MayOcclude(changed[i],light)) {relevant=true;break;}
                }
                if(!relevant || !PixelRange(light.Footprint,out int left,out int bottom,out int right,out int top)) continue;
                for(int y=bottom;y<=top;y++) for(int x=left;x<=right;x++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    int index=y*Width+x;
                    if(marks[index]==2) continue;
                    if(marks[index]==0) {marks[index]=1;candidates++;}
                    var point=RoomLightMap.Position(Bounds,Width,Height,x,y);
                    // The range includes conservative edge cells; test the actual source support.
                    if(!light.Footprint.Contains(point) || light.Mask==null && Vector2.Distance(point,light.Origin)>=light.Radius) continue;
                    for(int i=0;i<changed.Count;i++)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if(!RoomLightMap.BlocksSegment(changed[i],light.Origin,point)) continue;
                        marks[index]=2;indices.Add(index);break;
                    }
                }
            }
            indices.Sort();
            var first=new Color[indices.Count];var second=new Color[indices.Count];
            for(int i=0;i<indices.Count;i++)
            {
                cancellation.ThrowIfCancellationRequested();
                int index=indices[i];var point=RoomLightMap.Position(Bounds,Width,Height,index%Width,index/Width);
                first[i]=RoomLightMap.Evaluate(target,foregroundProfile,point,cancellation);
                second[i]=RoomLightMap.Evaluate(target,backgroundProfile,point,cancellation);
            }
            cancellation.ThrowIfCancellationRequested();
            return new Patch(this,baseVersion,target,indices.ToArray(),first,second,candidates);
        }

        // The adapter also checks its visible-room binding and current requested revision.
        // Engine commits reject a foreign context or a baseline superseded by another commit.
        public bool Commit(Patch patch)
        {
            if(patch==null) throw new ArgumentNullException(nameof(patch));
            lock(gate)
            {
                if(!ReferenceEquals(patch.Context,this) || patch.BaseVersion!=version || patch.Committed) return false;
                for(int i=0;i<patch.PixelIndices.Length;i++)
                {int index=patch.PixelIndices[i];foreground[index]=patch.First[i];background[index]=patch.Second[i];}
                published=patch.TargetOcclusion;version++;patch.Committed=true;return true;
            }
        }
        public void CopyPixels(Color[] destinationForeground,Color[] destinationBackground)
        {
            if(destinationForeground==null || destinationBackground==null || destinationForeground.Length!=foreground.Length ||
                destinationBackground.Length!=background.Length || ReferenceEquals(destinationForeground,destinationBackground))
                throw new ArgumentException($"Некорректные буферы карт света: требуется два отдельных массива по {foreground.Length} пикселей.");
            lock(gate) {Array.Copy(foreground,destinationForeground,foreground.Length);Array.Copy(background,destinationBackground,background.Length);}
        }

        private bool PixelRange(Rect footprint,out int left,out int bottom,out int right,out int top)
        {
            float minX=Mathf.Max(Bounds.xMin,footprint.xMin),maxX=Mathf.Min(Bounds.xMax,footprint.xMax);
            float minY=Mathf.Max(Bounds.yMin,footprint.yMin),maxY=Mathf.Min(Bounds.yMax,footprint.yMax);
            left=bottom=0;right=top=-1;
            if(minX>=maxX || minY>=maxY) return false;
            // Include adjacent centres conservatively, then test their actual support above.
            left=Mathf.Max(0,Mathf.FloorToInt((minX-Bounds.x)*Width/Bounds.width)-1);
            right=Mathf.Min(Width-1,Mathf.CeilToInt((maxX-Bounds.x)*Width/Bounds.width)+1);
            bottom=Mathf.Max(0,Mathf.FloorToInt((minY-Bounds.y)*Height/Bounds.height)-1);
            top=Mathf.Min(Height-1,Mathf.CeilToInt((maxY-Bounds.y)*Height/Bounds.height)+1);return true;
        }
        private static bool MayOcclude(Rect wall,RoomLightMap.PreparedLight light)
        {
            if(wall.Contains(light.Origin)) return false;
            var support=light.Footprint;
            float minX=Mathf.Min(support.xMin,light.Origin.x),maxX=Mathf.Max(support.xMax,light.Origin.x);
            float minY=Mathf.Min(support.yMin,light.Origin.y),maxY=Mathf.Max(support.yMax,light.Origin.y);
            return wall.xMin<=maxX && wall.xMax>=minX && wall.yMin<=maxY && wall.yMax>=minY;
        }
        private static List<Rect> ChangedRects(Occlusion before,Occlusion after,CancellationToken cancellation)
        {
            var changed=new List<Rect>();int a=0,b=0;
            while(a<before.Count || b<after.Count)
            {
                cancellation.ThrowIfCancellationRequested();
                if(a==before.Count) {changed.Add(after[b++]);continue;}
                if(b==after.Count) {changed.Add(before[a++]);continue;}
                int comparison=CompareRects(before[a],after[b]);
                if(comparison==0) {a++;b++;} else if(comparison<0) changed.Add(before[a++]);else changed.Add(after[b++]);
            }
            return changed;
        }
        private static int CompareRects(Rect a,Rect b)
        {
            int result=a.x.CompareTo(b.x);if(result!=0) return result;
            result=a.y.CompareTo(b.y);if(result!=0) return result;
            result=a.width.CompareTo(b.width);return result!=0?result:a.height.CompareTo(b.height);
        }

        public sealed class Occlusion
        {
            private readonly Rect[] solids;
            internal Occlusion(Rect[] solids)=>this.solids=solids;
            public int Count=>solids.Length;
            public Rect this[int index]=>solids[index];
        }
        // Raw arrays and scalar inputs are private and frozen after capture. The only mutation
        // is ownership transfer/decode during one Build attempt. Cancellation consumes the plan:
        // partially decoded raw arrays must never be decoded or sampled by a second attempt.
        public sealed class BuildPlan
        {
            private readonly string room;
            private readonly RoomLightMap.CapturedProfile first,second;
            private readonly RoomLightMask.RawPixels[] raw;
            internal readonly Occlusion InitialOcclusion;
            private int consumed;
            public int Width {get;}
            public int Height {get;}
            public Rect Bounds {get;}
            public int SourceCount {get;}
            public int UniqueDecodedTextures=>raw.Length;
            public long DecodedPixels {get;}
            public double PrepareMilliseconds {get;}
            public double GetPixelsMilliseconds {get;}
            public bool IsConsumed=>Volatile.Read(ref consumed)!=0;
            internal BuildPlan(string room,Rect bounds,RoomLightMap.CapturedProfile first,RoomLightMap.CapturedProfile second,
                Occlusion occlusion,RoomLightMask.RawPixels[] raw,long pixelCount,double prepareMilliseconds,double getPixelsMilliseconds)
            {
                this.room=room;Bounds=bounds;Width=Mathf.CeilToInt(bounds.width*8);Height=Mathf.CeilToInt(bounds.height*8);
                this.first=first;this.second=second;InitialOcclusion=occlusion;this.raw=raw;SourceCount=first.Lights.Length;
                DecodedPixels=pixelCount;PrepareMilliseconds=prepareMilliseconds;GetPixelsMilliseconds=getPixelsMilliseconds;
            }
            public RoomLightMaps Build(CancellationToken cancellation=default)=>new(CreateData(cancellation));
            internal BuildData CreateData(CancellationToken cancellation)
            {
                if(Interlocked.CompareExchange(ref consumed,1,0)!=0)
                    throw new InvalidOperationException($"План света уже использован: room={room}, size={Width}×{Height}. После отмены или ошибки подготовьте новый план.");
                cancellation.ThrowIfCancellationRequested();
                var timer=System.Diagnostics.Stopwatch.StartNew();
                var masks=RoomLightMap.DecodeMasks(raw,cancellation);
                var foregroundProfile=first.Bind(masks,cancellation);var backgroundProfile=second.Bind(masks,cancellation);
                double decodeMilliseconds=timer.Elapsed.TotalMilliseconds;timer.Restart();
                var foreground=new Color[Width*Height];var background=new Color[foreground.Length];
                for(int y=0;y<Height;y++) for(int x=0;x<Width;x++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    int index=y*Width+x;var point=RoomLightMap.Position(Bounds,Width,Height,x,y);
                    foreground[index]=RoomLightMap.Evaluate(InitialOcclusion,foregroundProfile,point,cancellation);
                    background[index]=RoomLightMap.Evaluate(InitialOcclusion,backgroundProfile,point,cancellation);
                }
                cancellation.ThrowIfCancellationRequested();
                return new BuildData(this,foregroundProfile,backgroundProfile,foreground,background,decodeMilliseconds,timer.Elapsed.TotalMilliseconds);
            }
        }
        internal readonly struct BuildData
        {
            internal readonly BuildPlan Plan;
            internal readonly RoomLightMap.PreparedProfile ForegroundProfile,BackgroundProfile;
            internal readonly Color[] Foreground,Background;
            internal readonly double DecodeMilliseconds,EvaluateMilliseconds;
            internal BuildData(BuildPlan plan,RoomLightMap.PreparedProfile foregroundProfile,RoomLightMap.PreparedProfile backgroundProfile,
                Color[] foreground,Color[] background,double decodeMilliseconds,double evaluateMilliseconds)
            {
                Plan=plan;ForegroundProfile=foregroundProfile;BackgroundProfile=backgroundProfile;Foreground=foreground;Background=background;
                DecodeMilliseconds=decodeMilliseconds;EvaluateMilliseconds=evaluateMilliseconds;
            }
        }
        public sealed class Patch
        {
            internal readonly RoomLightMaps Context;
            internal readonly int[] PixelIndices;
            internal readonly Color[] First,Second;
            internal bool Committed;
            public long BaseVersion {get;}
            public Occlusion TargetOcclusion {get;}
            public IReadOnlyList<int> Indices {get;}
            public IReadOnlyList<Color> Foreground {get;}
            public IReadOnlyList<Color> Background {get;}
            public int CandidatePixels {get;}
            public int DirtyPixels=>PixelIndices.Length;
            public bool IsEmpty=>PixelIndices.Length==0;
            internal Patch(RoomLightMaps context,long baseVersion,Occlusion target,int[] indices,Color[] first,Color[] second,int candidates)
            {
                Context=context;BaseVersion=baseVersion;TargetOcclusion=target;PixelIndices=indices;First=first;Second=second;CandidatePixels=candidates;
                Indices=Array.AsReadOnly(indices);Foreground=Array.AsReadOnly(first);Background=Array.AsReadOnly(second);
            }
        }
    }
}
