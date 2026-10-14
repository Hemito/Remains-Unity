using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Remains.Content;
using UnityEngine;

namespace Remains.Domain
{
    public enum ActorBodySupportOutcome { Unbound, UnsupportedAnatomy, Suspended, Unsupported, NoSampledContact, Contact, BlockedByCeiling }

    // Scalar evidence belongs to the same sample as BodyPosition; no mutable surface escapes.
    public readonly struct ActorBodySupportSample
    {
        public readonly bool IsBound,HasFootProfile,Grounded,HasContact;
        public readonly Vector2 Anchor,BodyPosition,ContactPoint;
        public readonly string Pose,Texture,MovementSound;
        public readonly int Frame,Facing,SlopeDirection,PartIndex,PixelX,PixelY;
        public readonly long GeometryRevision;
        public readonly Rect SurfaceBounds;
        public readonly ActorBodySupportOutcome Outcome;
        internal ActorBodySupportSample(bool bound,bool profile,bool grounded,bool contact,Vector2 anchor,Vector2 body,
            string pose,int frame,int facing,long revision,ActorBodySupportOutcome outcome,ActorBodySupport.Surface surface=default,
            FootSupportPoint point=default)
        {
            IsBound=bound;HasFootProfile=profile;Grounded=grounded;HasContact=contact;Anchor=anchor;BodyPosition=body;
            Pose=pose;Frame=frame;Facing=facing;GeometryRevision=revision;Outcome=outcome;
            SurfaceBounds=surface.Bounds;SlopeDirection=surface.Direction;Texture=surface.Texture;MovementSound=surface.Sound;
            PartIndex=point.PartIndex;PixelX=point.PixelX;PixelY=point.PixelY;
            ContactPoint=contact?new Vector2((float)(anchor.x+facing*point.X),(float)(body.y+point.Y)):default;
        }
    }

    // Derived placement only. Movement, networking and persistence continue owning the anchor.
    public static class ActorBodySupport
    {
        private const float Inset=.0001f;
        private static ConditionalWeakTable<ActorState,Binding> actors=new();
        private static ConditionalWeakTable<RoomLayout,PreparedRoom> rooms=new();
        public static void Clear() {actors=new();rooms=new();}
        public static void Unbind(ActorState actor) {if(actor!=null) actors.Remove(actor);}
        public static void Bind(ActorState actor,RoomLayout room)
        {
            if(actor==null || room==null) throw new ArgumentException("Привязка опоры тела требует актёра и комнаты.");
            FootSupportCatalog.Load();
            var prepared=rooms.GetValue(room,key=>new PreparedRoom(key));
            if(!prepared.MatchesTopology) prepared.Rebuild();
            if(actors.TryGetValue(actor,out var prior) && ReferenceEquals(prior.Room,prepared)) return;
            actors.Remove(actor);actors.Add(actor,new Binding(prepared));
        }
        // Explicit geometry publication, never a renderer/update rebuild.
        public static void InvalidateRoom(RoomLayout room)
        {
            if(room==null) throw new ArgumentNullException(nameof(room));
            if(rooms.TryGetValue(room,out var prepared)) prepared.Rebuild();
        }
        public static void SetDropping(ActorState actor,bool dropping)
        {
            if(actor!=null && actors.TryGetValue(actor,out var binding) && binding.Dropping!=dropping)
            {binding.Dropping=dropping;binding.HasSample=false;}
        }
        public static Vector2 Position(ActorState actor)=>Sample(actor).BodyPosition;
        public static ActorBodySupportSample Sample(ActorState actor)
        {
            if(actor==null) throw new ArgumentNullException(nameof(actor));
            if(!actors.TryGetValue(actor,out var binding))
                return new ActorBodySupportSample(false,false,false,false,actor.Position,actor.Position,actor.pose,-1,actor.facing,0,ActorBodySupportOutcome.Unbound);
            var room=binding.Room;room.RequireTopology();
            var hull=Anatomy.Movement(actor);
            Validate(actor,hull,room.Layout);
            bool profile=FootSupportCatalog.Supports(actor.anatomy);
            string pose=ActorPose.Clip(actor);
            int frame=profile?PonyMotionFrames.Sample(actor,actor.poseTime):-1;
            var key=new SampleKey(actor,hull,frame,room,binding.Dropping);
            if(binding.HasSample && binding.Key.Equals(key)) return binding.Sample;
            var sample=Resolve(actor,hull,pose,frame,profile,binding);
            binding.Key=key;binding.Sample=sample;binding.HasSample=true;return sample;
        }
        private static ActorBodySupportSample Resolve(ActorState actor,MovementHull hull,string pose,int frame,bool profile,Binding binding)
        {
            var room=binding.Room;var anchor=actor.Position;
            bool suspended=actor.climbing || ActorSessionRules.IsPassive(actor) || ActorHoldRules.Active(actor) || binding.Dropping || actor.vy!=0;
            FootSupportFrame feet=profile?FootSupportCatalog.Get(actor.anatomy,pose,frame):null;
            double reach=feet==null?hull.width*.5:Math.Max(hull.width*.5,Math.Max(Math.Abs(feet.Bounds.MinimumX),Math.Abs(feet.Bounds.MaximumX)));
            double left=anchor.x-reach,right=anchor.x+reach;
            int count=room.Query(left,right);int primary=-1;double primaryGap=0,primaryTop=0,primaryCoverage=0;
            for(int i=0;i<count;i++)
            {
                var surface=room.QuerySurface(i);
                if(anchor.x+hull.width*.5f<=surface.Left+Inset || anchor.x-hull.width*.5f>=surface.Right-Inset) continue;
                double top=surface.PhysicalTop(anchor.x,hull.width),gap=Math.Abs(anchor.y-top);
                if(gap>RoomSupport.ContactTolerance) continue;
                double coverage=Math.Min(anchor.x+hull.width*.5,surface.Right)-Math.Max(anchor.x-hull.width*.5,surface.Left);
                if(primary<0 || Better(surface,gap,top,coverage,room.QuerySurface(primary),primaryGap,primaryTop,primaryCoverage))
                {primary=i;primaryGap=gap;primaryTop=top;primaryCoverage=coverage;}
            }
            bool grounded=primary>=0 && !suspended;
            if(!profile || suspended || !grounded)
            {
                binding.WarnedNoSampled=false;
                return new ActorBodySupportSample(true,profile,grounded,false,anchor,anchor,pose,frame,actor.facing,room.Revision,
                    !profile?ActorBodySupportOutcome.UnsupportedAnatomy:suspended?ActorBodySupportOutcome.Suspended:ActorBodySupportOutcome.Unsupported,
                    primary<0?default:room.QuerySurface(primary));
            }
            // The finite neighborhood is restricted to the current hull/foot X extent. Edges
            // joining floors elsewhere in the room cannot turn an overhead storey into a floor.
            room.CollectNeighborhood(primary,left,right,count);
            bool contact=false;double bodyY=double.NegativeInfinity;Surface selected=default;FootSupportPoint point=default;
            for(int i=0;i<count;i++)
            {
                if(!room.Eligible(i)) continue;
                var surface=room.QuerySurface(i);
                if(!feet.TryLowest(surface.Gradient,actor.facing,surface.Left-anchor.x,surface.Right-anchor.x,out var candidate)) continue;
                double height=surface.Height(anchor.x+candidate.X*actor.facing)-candidate.Y;
                if(!double.IsFinite(height)) throw Error(actor,room.Layout,"получена нечисловая высота sampled contact");
                if(!contact || height>bodyY || height==bodyY && CompareGeometry(surface,selected)<0)
                {contact=true;bodyY=height;selected=surface;point=candidate;}
            }
            if(!contact)
            {
                if(!binding.WarnedNoSampled)
                {
                    binding.WarnedNoSampled=true;
                    Debug.LogWarning($"Нет sampled-centre контакта при физической опоре: actor={actor.id}, room={room.Layout.id}, pose={pose}, frame={frame}, anchor={anchor}, facing={actor.facing}, surface={room.QuerySurface(primary).Bounds}. Сохранён якорь; интерполяция или фиктивный endpoint не применяются.");
                }
                return new ActorBodySupportSample(true,true,true,false,anchor,anchor,pose,frame,actor.facing,room.Revision,ActorBodySupportOutcome.NoSampledContact);
            }
            binding.WarnedNoSampled=false;
            var body=new Vector2(anchor.x,(float)bodyY);
            if(!float.IsFinite(body.y)) throw Error(actor,room.Layout,"высота тела не помещается в float");
            if(room.BlocksDisplacement(anchor,body,hull,count))
            {
                if(!binding.WarnedBlocked)
                {
                    binding.WarnedBlocked=true;
                    Debug.LogWarning($"Опора тела ограничена препятствием: actor={actor.id}, room={room.Layout.id}, pose={pose}, frame={frame}, anchor={anchor}, requestedBody={body}, surface={selected.Bounds}. Сохранён якорь, sampled contact отсутствует.");
                }
                return new ActorBodySupportSample(true,true,true,false,anchor,anchor,pose,frame,actor.facing,room.Revision,ActorBodySupportOutcome.BlockedByCeiling,selected);
            }
            binding.WarnedBlocked=false;
            return new ActorBodySupportSample(true,true,true,true,anchor,body,pose,frame,actor.facing,room.Revision,ActorBodySupportOutcome.Contact,selected,point);
        }
        private static void Validate(ActorState actor,MovementHull hull,RoomLayout room)
        {
            if(!float.IsFinite(actor.x) || !float.IsFinite(actor.y) || !float.IsFinite(actor.vx) || !float.IsFinite(actor.vy) ||
                !float.IsFinite(actor.poseTime) || actor.facing is not (-1 or 1) || hull==null ||
                !float.IsFinite(hull.width) || !float.IsFinite(hull.height) || hull.width<=0 || hull.height<=0)
                throw Error(actor,room,"некорректные координаты, фаза, facing или габарит");
        }
        private static InvalidOperationException Error(ActorState actor,RoomLayout room,string reason)
            =>new($"Некорректная опора тела: actor={actor.id}, anatomy={actor.anatomy}, room={room.id}, pose={actor.pose}, phase={actor.poseTime}: {reason}.");
        private static bool Better(Surface a,double gap,double top,double coverage,Surface b,double bGap,double bTop,double bCoverage)
        {
            int order=gap.CompareTo(bGap);if(order!=0) return order<0;
            order=top.CompareTo(bTop);if(order!=0) return order>0;
            if((a.Direction==0)!=(b.Direction==0)) return a.Direction==0;
            order=coverage.CompareTo(bCoverage);return order!=0?order>0:CompareGeometry(a,b)<0;
        }
        private static int CompareGeometry(Surface a,Surface b)
        {
            int order=Math.Abs(b.Gradient).CompareTo(Math.Abs(a.Gradient));if(order!=0) return order;
            order=b.Direction.CompareTo(a.Direction);if(order!=0) return order;
            order=a.Left.CompareTo(b.Left);if(order!=0) return order;
            order=a.Y.CompareTo(b.Y);if(order!=0) return order;
            order=b.Width.CompareTo(a.Width);if(order!=0) return order;
            order=b.HeightSize.CompareTo(a.HeightSize);if(order!=0) return order;
            order=a.OneWay.CompareTo(b.OneWay);if(order!=0) return order;
            // Only indistinguishable physics uses stable material priority. Preparation
            // warns once if authored duplicates assign different material banks.
            order=StringComparer.Ordinal.Compare(a.Texture,b.Texture);return order!=0?order:StringComparer.Ordinal.Compare(a.Sound,b.Sound);
        }
        internal readonly struct Surface
        {
            private readonly RoomSurface source;
            public readonly double Left,Right,Y,Width,HeightSize,Gradient,Intercept;
            public readonly int Direction;
            public readonly bool OneWay;
            public readonly string Texture,Sound;
            public Rect Bounds=>new((float)Left,(float)Y,(float)Width,(float)HeightSize);
            public Surface(RoomSurface surface,string room,int index,string defaultSound)
            {
                if(surface==null || !float.IsFinite(surface.x) || !float.IsFinite(surface.y) || !float.IsFinite(surface.width) || !float.IsFinite(surface.height) ||
                    surface.width<=0 || surface.height<=0 || surface.slopeDirection is not (-1 or 0 or 1))
                    throw new InvalidOperationException($"Некорректная поверхность опоры тела: room={room}, index={index}.");
                // Materialize the native binary32 endpoint before promotion. Some JITs
                // retain a float addition at extended precision until actual storage.
                source=surface;Left=surface.x;Width=surface.width;
                Right=BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(surface.x+surface.width));
                Y=surface.y;HeightSize=surface.height;Direction=surface.slopeDirection;OneWay=surface.oneWay;
                Gradient=Direction==0?0:Direction*HeightSize/Width;Intercept=Y+(Direction<=0?HeightSize:0)-Gradient*Left;
                // Empty per-surface bank explicitly inherits the authored room bank.
                Texture=surface.texture;Sound=string.IsNullOrEmpty(surface.movementSound)?defaultSound:surface.movementSound;
            }
            public double Height(double x)=>Gradient*x+Intercept;
            // One physical rule: keep its stored binary32 result, never repeat the solver.
            public double PhysicalTop(float x,float width)
                =>BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(RoomSupport.Top(source,x,width)));
        }
        private readonly struct Edge
        {
            public readonly int Target;public readonly double Left,Right;
            public Edge(int target,double left,double right) {Target=target;Left=left;Right=right;}
        }
        private sealed class PreparedRoom
        {
            private const double BinWidth=2;
            public readonly RoomLayout Layout;
            public long Revision {get;private set;}
            private RoomSurface[] source;
            private Surface[] surfaces;
            private Edge[][] edges;
            private readonly Dictionary<int,int[]> bins=new();
            private int[] query,marks,queue;private bool[] eligible;
            private int mark,queryCount,staticCount,dynamicCount;
            private DynamicSurfaceFrame dynamicFrame;private long dynamicTopology;
            public PreparedRoom(RoomLayout layout) {Layout=layout;Rebuild();}
            public void Rebuild()
            {
                if(!float.IsFinite(Layout.left) || !float.IsFinite(Layout.right) || !float.IsFinite(Layout.bottom) || !float.IsFinite(Layout.top) ||
                    Layout.left>=Layout.right || Layout.bottom>=Layout.top || Layout.platforms==null)
                    throw new InvalidOperationException("Некорректные границы подготовки опоры тела: room="+Layout.id);
                source=Layout.platforms;staticCount=source.Length;dynamicFrame=Layout.dynamicSurfaces;
                dynamicCount=dynamicFrame?.Count??0;dynamicTopology=dynamicFrame?.TopologyVersion??0;
                surfaces=new Surface[staticCount+dynamicCount];edges=new Edge[staticCount][];
                query=new int[surfaces.Length];marks=new int[surfaces.Length];queue=new int[surfaces.Length];eligible=new bool[surfaces.Length];
                var buildingBins=new Dictionary<int,List<int>>();var gradients=new HashSet<double>();bins.Clear();mark=0;
                for(int i=0;i<staticCount;i++)
                {
                    surfaces[i]=new Surface(source[i],Layout.id,i,Layout.movementSound);gradients.Add(surfaces[i].Gradient);
                    int first=Bin(surfaces[i].Left),last=Bin(surfaces[i].Right);
                    if((long)last-first>65536) throw new InvalidOperationException($"Слишком протяжённая опора: room={Layout.id}, index={i}.");
                    for(int bin=first;bin<=last;bin++)
                    {if(!buildingBins.TryGetValue(bin,out var list)) buildingBins.Add(bin,list=new());list.Add(i);}
                }
                foreach(var pair in buildingBins) bins.Add(pair.Key,pair.Value.ToArray());
                // Joined surfaces overlap horizontally and meet in height (Join), so they share a cell of a coarse
                // x/height grid: candidates come from the surface's own cells (ascending, as the former all-pairs
                // scan) instead of all 7884² factory pairs (~2 s per door change, 2026-10-14).
                var cells=new Dictionary<long,List<int>>();
                long Cell(int x,int y)=>((long)x<<32)^(uint)y;
                (int x0,int x1,int y0,int y1) Span(Surface surface)
                {
                    double a=surface.Gradient*surface.Left+surface.Intercept,b=surface.Gradient*surface.Right+surface.Intercept;
                    double tolerance=RoomSupport.ContactTolerance;
                    return (Bin(surface.Left),Bin(surface.Right),Bin(Math.Min(a,b)-tolerance),Bin(Math.Max(a,b)+tolerance));
                }
                for(int i=0;i<staticCount;i++)
                {
                    var span=Span(surfaces[i]);
                    for(int x=span.x0;x<=span.x1;x++) for(int y=span.y0;y<=span.y1;y++)
                    {if(!cells.TryGetValue(Cell(x,y),out var list)) cells.Add(Cell(x,y),list=new List<int>());list.Add(i);}
                }
                var candidates=new List<int>();var seen=new bool[staticCount];
                for(int i=0;i<staticCount;i++)
                {
                    var neighbors=new List<Edge>();
                    candidates.Clear();
                    var span=Span(surfaces[i]);
                    for(int x=span.x0;x<=span.x1;x++) for(int y=span.y0;y<=span.y1;y++)
                        if(cells.TryGetValue(Cell(x,y),out var members))
                            foreach(int member in members) if(!seen[member]) {seen[member]=true;candidates.Add(member);}
                    foreach(int member in candidates) seen[member]=false;
                    candidates.Sort();
                    foreach(int j in candidates)
                    {
                        if(j<i && SameGeometry(surfaces[i],surfaces[j]) && (surfaces[i].Texture!=surfaces[j].Texture || surfaces[i].Sound!=surfaces[j].Sound))
                            Debug.LogWarning($"Неоднозначный материал совпадающих опор: room={Layout.id}, indices={j}/{i}, bounds={surfaces[i].Bounds}, textures={surfaces[j].Texture}/{surfaces[i].Texture}, banks={surfaces[j].Sound}/{surfaces[i].Sound}. Для одинаковой физики выбран ordinal texture, затем bank.");
                        if(i!=j && Join(surfaces[i],surfaces[j],out double left,out double right)) neighbors.Add(new Edge(j,left,right));
                    }
                    edges[i]=neighbors.ToArray();
                }
                // Published dynamic platforms are flat; no per-frame gradient preparation.
                gradients.Add(0);foreach(double gradient in gradients) FootSupportCatalog.PrepareGradient(gradient);Revision++;
            }
            public bool MatchesTopology=>ReferenceEquals(source,Layout.platforms) && ReferenceEquals(dynamicFrame,Layout.dynamicSurfaces) &&
                dynamicCount==(Layout.dynamicSurfaces?.Count??0) && dynamicTopology==(Layout.dynamicSurfaces?.TopologyVersion??0);
            public void RequireTopology()
            {
                if(!MatchesTopology)
                    throw new InvalidOperationException($"Геометрия опоры тела изменилась без InvalidateRoom: room={Layout.id}, revision={Revision}.");
            }
            public int Query(double left,double right)
            {
                queryCount=0;if(++mark==int.MaxValue) {Array.Clear(marks,0,marks.Length);mark=1;}
                Array.Clear(eligible,0,eligible.Length);
                for(int bin=Bin(left);bin<=Bin(right);bin++) if(bins.TryGetValue(bin,out var indices))
                    foreach(int index in indices)
                        if(marks[index]!=mark && surfaces[index].Right>=left && surfaces[index].Left<=right)
                        {marks[index]=mark;query[queryCount++]=index;}
                if(dynamicFrame!=null)
                {
                    dynamicFrame.RequirePublished();
                    for(int i=0;i<dynamicCount;i++)
                    {
                        int index=staticCount+i;surfaces[index]=new Surface(dynamicFrame.Pose(i).Current,Layout.id,index,Layout.movementSound);
                        if(surfaces[index].Direction!=0) throw new InvalidOperationException($"Динамический наклон требует явной подготовки topology: room={Layout.id}, index={i}.");
                        if(surfaces[index].Right>=left && surfaces[index].Left<=right)
                        {marks[index]=mark;query[queryCount++]=index;}
                    }
                }
                return queryCount;
            }
            public Surface QuerySurface(int index)=>surfaces[query[index]];
            public bool Eligible(int index)=>eligible[query[index]];
            public void CollectNeighborhood(int primary,double left,double right,int count)
            {
                int read=0,write=1;queue[0]=query[primary];eligible[queue[0]]=true;
                while(read<write)
                {
                    int at=queue[read++];
                    if(at<staticCount) foreach(var edge in edges[at])
                        if(marks[edge.Target]==mark && !eligible[edge.Target] && edge.Right>=left && edge.Left<=right)
                        {eligible[edge.Target]=true;queue[write++]=edge.Target;}
                    // Only the small published dynamic set needs scalar adjacency at query time.
                    for(int j=0;j<count;j++)
                    {
                        int candidate=query[j];if(eligible[candidate] || at<staticCount && candidate<staticCount) continue;
                        if(Join(surfaces[at],surfaces[candidate],out double joinLeft,out double joinRight) && joinRight>=left && joinLeft<=right)
                        {eligible[candidate]=true;queue[write++]=candidate;}
                    }
                }
            }
            public bool BlocksDisplacement(Vector2 anchor,Vector2 body,MovementHull hull,int count)
            {
                if(body.y+hull.height>Layout.top+Inset) return true;
                double lower=Math.Min(anchor.y,body.y),upper=Math.Max(anchor.y,body.y)+hull.height;
                for(int i=0;i<count;i++)
                {
                    int index=query[i];var surface=surfaces[index];
                    if(eligible[index] || surface.OneWay || anchor.x+hull.width*.5<=surface.Left+Inset || anchor.x-hull.width*.5>=surface.Right-Inset) continue;
                    // Existing side penetration is not repaired by this derived placement. Only
                    // newly crossed lower/upper hull strips may veto the vertical displacement.
                    bool newUpper=body.y>anchor.y && anchor.y+hull.height<=surface.Y+Inset && upper>surface.Y+Inset;
                    bool newLower=body.y<anchor.y && anchor.y>=surface.Y+surface.HeightSize-Inset && lower<surface.Y+surface.HeightSize-Inset;
                    if(newUpper || newLower) return true;
                }
                return false;
            }
            private static int Bin(double x)
            {
                double bin=Math.Floor(x/BinWidth);
                if(!double.IsFinite(bin) || bin<int.MinValue || bin>int.MaxValue-1) throw new InvalidOperationException("Координата опоры вне допустимого индекса.");
                return (int)bin;
            }
            private static bool Join(Surface a,Surface b,out double left,out double right)
            {
                left=Math.Max(a.Left,b.Left);right=Math.Min(a.Right,b.Right);if(left>right) return false;
                double m=a.Gradient-b.Gradient,c=a.Intercept-b.Intercept,tolerance=RoomSupport.ContactTolerance;
                if(m==0) return Math.Abs(c)<=tolerance;
                double x1=(-tolerance-c)/m,x2=(tolerance-c)/m;
                left=Math.Max(left,Math.Min(x1,x2));right=Math.Min(right,Math.Max(x1,x2));return left<=right;
            }
            private static bool SameGeometry(Surface a,Surface b)=>a.Left==b.Left && a.Y==b.Y && a.Width==b.Width && a.HeightSize==b.HeightSize && a.Direction==b.Direction && a.OneWay==b.OneWay;
        }
        private sealed class Binding
        {
            public readonly PreparedRoom Room;public bool Dropping,HasSample,WarnedBlocked,WarnedNoSampled;public SampleKey Key;public ActorBodySupportSample Sample;
            public Binding(PreparedRoom room)=>Room=room;
        }
        private readonly struct SampleKey : IEquatable<SampleKey>
        {
            private readonly string id,anatomy,pose,owner,level;
            private readonly float x,y,vx,vy,time,width,height;
            private readonly int frame,facing,generation,life,holdEpoch;
            private readonly uint sequence;
            private readonly long revision,dynamicSequence,dynamicTopology;
            private readonly bool climbing,crouched,passive,held,dropping;
            public SampleKey(ActorState actor,MovementHull hull,int sampled,PreparedRoom room,bool drop)
            {
                id=actor.id;anatomy=actor.anatomy;pose=actor.pose;owner=actor.owner;level=actor.level;
                x=actor.x;y=actor.y;vx=actor.vx;vy=actor.vy;time=actor.poseTime;width=hull.width;height=hull.height;
                frame=sampled;facing=actor.facing;generation=actor.generation;life=actor.lifecycleSequence;sequence=actor.poseSequence;holdEpoch=actor.holdEpoch;
                revision=room.Revision;dynamicSequence=room.Layout.dynamicSurfaces?.Sequence??0;dynamicTopology=room.Layout.dynamicSurfaces?.TopologyVersion??0;
                climbing=actor.climbing;crouched=actor.crouched;passive=ActorSessionRules.IsPassive(actor);held=ActorHoldRules.Active(actor);dropping=drop;
            }
            public bool Equals(SampleKey b)=>id==b.id && anatomy==b.anatomy && pose==b.pose && owner==b.owner && level==b.level &&
                x==b.x && y==b.y && vx==b.vx && vy==b.vy && time==b.time && width==b.width && height==b.height && frame==b.frame && facing==b.facing &&
                generation==b.generation && life==b.life && holdEpoch==b.holdEpoch && sequence==b.sequence && revision==b.revision && dynamicSequence==b.dynamicSequence && dynamicTopology==b.dynamicTopology &&
                climbing==b.climbing && crouched==b.crouched && passive==b.passive && held==b.held && dropping==b.dropping;
        }
    }
}
