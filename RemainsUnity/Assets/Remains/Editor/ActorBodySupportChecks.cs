using System;
using System.Globalization;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class ActorBodySupportChecks
    {
        private const double Tolerance=.00001;
        public static void Run()
        {
            Anatomy.Load();FootSupportCatalog.Load();SlopePoseCatalog.Load();ActorBodySupport.Clear();
            CheckResourceFrames();CheckFiniteAndStoreys();CheckLifecycle();CheckDynamic();CheckMalformed();CheckCeiling();CheckRoomSound();CheckNativeSeam();CheckPhysicalHoldTarget();
            foreach(int fps in new[]{30,60,144}) foreach(int direction in new[]{-1,1}) foreach(int travel in new[]{-1,1})
                Traverse(fps,direction,travel);
            CheckAuthoredRooms();ActorBodySupport.Clear();
            Debug.Log("Опора тела: PASS — actual sampled-centre oracle, конечные поверхности/пустой интервал/другой этаж, flat↔slope, shuffle, physical controller 30/60/144, cache/phase/facing/lifetime/copy, pause/drop/air/ladder/held/passive, moving support и реальные authored комнаты. Native local/remote renderer, GPU filtering и continuous sole эта Editor-проверка не доказывает.");
        }
        private static RoomSurface Floor(float x,float y,float width,float height,int direction=0,bool oneWay=false)=>new()
        {x=x,y=y,width=width,height=height,slopeDirection=direction,oneWay=oneWay,texture="TrainingRoom/steel",movementSound="metal"};
        private static RoomLayout Room(params RoomSurface[] surfaces)=>new()
        {id="actor-body-support-check",left=-20,right=20,bottom=-10,top=20,platforms=surfaces,ladders=Array.Empty<RoomLadder>()};
        private static ActorState Actor(float x,float y,string pose="idle",int facing=1)=>new()
        {id="support-actor",player=true,connected=true,owner="support-owner",level="factory:0",x=x,y=y,pose=pose,facing=facing};
        private static void CheckResourceFrames()
        {
            foreach(double gradient in new[]{0d,1d,-1d,.5d,-.5d}) FootSupportCatalog.PrepareGradient(gradient);
            foreach(var clip in PonyAnimationData.Library.clips) for(int index=0;index<clip.frames.Length;index++)
            {
                var feet=FootSupportCatalog.Get("pony",clip.id,index);
                foreach(int facing in new[]{-1,1}) foreach(int direction in new[]{-1,0,1})
                {
                    var surface=direction==0?Floor(-6,-.4f,12,.4f):Floor(-6,0,12,12,direction,true);
                    float anchor=RoomSupport.Top(surface,0,Anatomy.Movement("pony").width);
                    var actor=Actor(0,anchor,clip.id,facing);
                    // Use exact frame phases through the existing production sampler, never a
                    // synthetic contour or private resolver entry point.
                    actor.poseTime=FrameTime(clip,index);int sampled=PonyMotionFrames.Sample(actor,actor.poseTime);
                    if(sampled!=index) continue; // velocity-driven clips are separately controller-tested.
                    var room=Room(surface);room.top=40;ActorBodySupport.Bind(actor,room);
                    var result=ActorBodySupport.Sample(actor);var before=actor.Position;
                    Require(result.Grounded && result.HasContact && result.Frame==index,"Нет actual contact: "+clip.id+"/"+index);
                    double expected=Oracle(feet,surface,actor.x,facing,out bool found);
                    Require(found && Math.Abs(result.BodyPosition.y-expected)<Tolerance,"RMQ/body sample расходится с перечислением centres: "+clip.id+"/"+index);
                    Contact(result,surface);Require(actor.Position==before && actor.vx==0 && actor.vy==0,"Derived body изменил movement anchor/velocity.");
                    Require(ActorBodySupport.Position(actor)==result.BodyPosition && actor.BodyPosition==result.BodyPosition,"Потребители получили разные roots.");
                    ActorBodySupport.Unbind(actor);
                }
            }
        }
        private static float FrameTime(PonyClip clip,int frame)
            =>(frame+.1f)/clip.fps;
        private static double Oracle(FootSupportFrame feet,RoomSurface surface,float x,int facing,out bool found)
        {
            double gradient=surface.slopeDirection==0?0:(double)surface.slopeDirection*surface.height/surface.width;
            double intercept=surface.y+(surface.slopeDirection<=0?surface.height:0)-gradient*surface.x;
            double right=BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(surface.x+surface.width));
            double best=double.NegativeInfinity;found=false;
            foreach(var group in feet.Groups) foreach(var point in group.Points)
            {
                double worldX=x+point.X*facing;
                if(worldX<surface.x || worldX>right) continue;
                found=true;best=Math.Max(best,gradient*worldX+intercept-point.Y);
            }
            return best;
        }
        private static void CheckFiniteAndStoreys()
        {
            var slope=Floor(0,0,6,3,1,true);var flat=Floor(6,2.6f,4,.4f);var lower=Floor(-4,-.4f,4,.4f);
            var room=Room(lower,slope,flat);var actor=Actor(5.8f,RoomSupport.Top(slope,5.8f,1),"slopeStandUp");
            ActorBodySupport.Bind(actor,room);var baseline=ActorBodySupport.Sample(actor);
            double expected=Math.Max(Oracle(FootSupportCatalog.Get("pony",baseline.Pose,baseline.Frame),slope,actor.x,actor.facing,out _),
                Oracle(FootSupportCatalog.Get("pony",baseline.Pose,baseline.Frame),flat,actor.x,actor.facing,out _));
            Require(baseline.HasContact && Math.Abs(baseline.BodyPosition.y-expected)<Tolerance,"Конечный slope-flat neighborhood не использует действительные centres.");
            var overhead=Floor(4,7,5,.3f,0,true);var ceiling=Floor(4,8,5,.3f);
            foreach(var surfaces in new[]{new[]{overhead,flat,slope,lower,ceiling},new[]{ceiling,lower,slope,flat,overhead}})
            {
                room.platforms=surfaces;ActorBodySupport.InvalidateRoom(room);var result=ActorBodySupport.Sample(actor);
                Require(result.BodyPosition==baseline.BodyPosition && result.ContactPoint==baseline.ContactPoint && result.Texture==baseline.Texture,"Другой этаж/ceiling либо порядок массива изменил root.");
            }
            actor.x=6.8f;actor.y=3;actor.pose="idle";actor.poseTime=0;
            Require(ActorBodySupport.Sample(actor).HasContact,"Обычная flat часть перестала давать actual contact.");
            // Choose an actual gap between sorted centre X values under the hull. An empty
            // finite interval cannot gain a fictitious interpolated/endpoint contact.
            var feet=FootSupportCatalog.Get("pony","idle",0);
            var xs=feet.Groups.SelectMany(g=>g.Points).Select(p=>p.X).Where(x=>x>-.49 && x<.49).Distinct().OrderBy(x=>x).ToArray();
            Require(xs.Length>1,"Нет exact-X данных для finite interval counterexample.");
            int gap=0;for(int i=1;i<xs.Length-1;i++) if(xs[i+1]-xs[i]>xs[gap+1]-xs[gap]) gap=i;
            double center=(xs[gap]+xs[gap+1])/2,half=(xs[gap+1]-xs[gap])*.1;
            var cap=Floor((float)(center-half),-.2f,(float)(half*2),.2f);var narrow=Room(cap);actor=Actor(0,0);
            ActorBodySupport.Bind(actor,narrow);var empty=ActorBodySupport.Sample(actor);
            Require(empty.Grounded && !empty.HasContact && empty.Outcome==ActorBodySupportOutcome.NoSampledContact && empty.BodyPosition==actor.Position,"Пустой finite interval получил contact/fallback.");
            // An isolated higher floor is not connected to a supported lower one.
            var bottom=Floor(-3,-.4f,6,.4f);var high=Floor(-3,3,6,.4f,0,true);actor=Actor(0,0);room=Room(bottom,high);
            ActorBodySupport.Bind(actor,room);var low=ActorBodySupport.Sample(actor);
            Contact(low,bottom);Require(low.BodyPosition.y<1,"One-way сверху поднял тело на другой этаж.");
            actor.y=high.y+.1f;actor.vy=1;Require(!ActorBodySupport.Sample(actor).HasContact,"Проход снизу стал foot landing.");
        }
        private static void CheckLifecycle()
        {
            var room=Room(Floor(-5,-.4f,10,.4f));var actor=Actor(0,0,"walk");ActorBodySupport.Bind(actor,room);
            var first=ActorBodySupport.Sample(actor);Require(first.HasContact,"Нет flat walking contact.");
            for(int i=0;i<20;i++) Require(ActorBodySupport.Sample(actor).BodyPosition==first.BodyPosition,"Повтор sample/пауза меняет body.");
            actor.poseTime=.2f;var advanced=ActorBodySupport.Sample(actor);
            Require(advanced.Frame==PonyMotionFrames.Sample(actor,actor.poseTime),"Кэш использовал прежнюю итоговую фазу.");
            actor.facing=-1;Require(ActorBodySupport.Sample(actor).Facing==-1,"Кэш потерял новый facing.");
            var copied=StateJson.Copy(actor);Require(ActorBodySupport.Sample(copied).Outcome==ActorBodySupportOutcome.Unbound && copied.BodyPosition==copied.Position,"Load/copy унаследовал transient binding.");
            ActorBodySupport.Bind(copied,room);Require(copied.BodyPosition==actor.BodyPosition,"Повторная привязка save sample изменила root.");
            string before=StateJson.ToJson(actor);_ = ActorBodySupport.Sample(actor);Require(before==StateJson.ToJson(actor),"Support сохранил derived поля в actor.");
            foreach(Action suspend in new Action[]{()=>actor.climbing=true,()=>actor.vy=1,()=>actor.down=true,()=>actor.hold=new ActorHoldState {token="hold",holder="other"},()=>ActorBodySupport.SetDropping(actor,true)})
            {
                actor.climbing=false;actor.vy=0;actor.down=false;actor.hold=null;ActorBodySupport.SetDropping(actor,false);suspend();
                var result=ActorBodySupport.Sample(actor);Require(!result.HasContact && result.BodyPosition==actor.Position && result.Outcome==ActorBodySupportOutcome.Suspended,"Suspended body получил floor projection.");
            }
            actor.climbing=true;actor.pose="climb";actor.poseTime=-2.4767f;actor.down=false;actor.hold=null;ActorBodySupport.SetDropping(actor,false);
            Require(ActorBodySupport.Sample(actor).Outcome==ActorBodySupportOutcome.Suspended,"Штатная отрицательная фаза descent отвергнута.");
            actor.climbing=false;actor.pose="idle";actor.poseTime=0;actor.player=true;actor.connected=false;
            Require(!ActorBodySupport.Sample(actor).HasContact,"Disconnected body получил alive floor projection.");
            actor.connected=true;actor.anatomy="raider";actor.actorType="raider";
            var atlas=ActorBodySupport.Sample(actor);Require(!atlas.HasFootProfile && atlas.Grounded && atlas.BodyPosition==actor.Position && atlas.Outcome==ActorBodySupportOutcome.UnsupportedAnatomy,"Атласная анатомия получила pony sole.");
            ActorBodySupport.Unbind(actor);Require(!ActorBodySupport.Sample(actor).IsBound,"Unbind сохранил cache.");
        }
        private static void CheckDynamic()
        {
            var room=Room();room.dynamicSurfaces=new DynamicSurfaceFrame();
            room.dynamicSurfaces.ReconcileIds(new[]{new DynamicSurfaceSpec("actual-moving-support",false,false,"floor","metal")});
            var a=new Rect(-2,0,4,.5f);room.dynamicSurfaces.SetPose(0,a,a,0,0,1,false);
            var actor=Actor(0,.5f);ActorBodySupport.Bind(actor,room);var before=ActorBodySupport.Sample(actor);Require(before.HasContact,"Нет published moving support.");
            var b=new Rect(-1,.2f,4,.5f);room.dynamicSurfaces.SetPose(0,a,b,0,.1,2,true);actor.y=.7f;
            var after=ActorBodySupport.Sample(actor);Require(after.HasContact && after.SurfaceBounds==b && Math.Abs(after.BodyPosition.y-before.BodyPosition.y-.2)<Tolerance,"Dynamic Sequence оставил старый контакт.");
            actor.lifecycleSequence++;actor.generation++;Require(ActorBodySupport.Sample(actor).HasContact,"Lifetime rebind при той же актуальной опоре потерял contact.");
            room.dynamicSurfaces.ReconcileIds(Array.Empty<DynamicSurfaceSpec>());
            Reject(()=>ActorBodySupport.Sample(actor),"без InvalidateRoom");ActorBodySupport.InvalidateRoom(room);
            Require(!ActorBodySupport.Sample(actor).Grounded,"Удалённая dynamic support воскресла из cache.");
        }
        private static void CheckCeiling()
        {
            // Pick a genuine resource frame whose flat constraint raises root. The obstacle
            // is a real solid strip above the movement hull; projection may not cross it.
            ActorState selected=null;
            foreach(var clip in PonyAnimationData.Library.clips)
            {
                var trial=Actor(0,0,clip.id);trial.poseTime=0;
                var frame=FootSupportCatalog.Get("pony",clip.id,PonyMotionFrames.Sample(trial,0));
                if(frame.Bounds.MinimumY<-.005) {selected=trial;break;}
            }
            Require(selected!=null,"Нет resource frame для реального ceiling counterexample.");
            float height=Anatomy.Movement(selected).height;
            var room=Room(Floor(-4,-.4f,8,.4f),Floor(-2,height+.001f,4,.2f));ActorBodySupport.Bind(selected,room);
            var result=ActorBodySupport.Sample(selected);Require(result.Outcome==ActorBodySupportOutcome.BlockedByCeiling && !result.HasContact && result.BodyPosition==selected.Position,"Floor projection прошёл через solid ceiling.");
        }
        private static void Traverse(int fps,int direction,int travel)
        {
            var room=Room(Floor(-8,(direction>0?0:3)-.4f,8,.4f),Floor(0,0,6,3,direction,true),Floor(6,(direction>0?3:0)-.4f,8,.4f));
            float start=travel>0?-2:9;var actor=Actor(start,direction>0?(travel>0?0:3):(travel>0?3:0));
            var motion=new MovementController(room);ActorBodySupport.Bind(actor,room);int contacts=0;
            for(int i=0;i<fps*6 && (travel>0?actor.x<9:actor.x>-2);i++)
            {
                float dt=1f/fps;ActorPose.Advance(actor,dt);motion.Step(actor,new Vector2(travel,0),false,dt,lookAtX:travel>0?-30:30);
                var anchor=actor.Position;var velocity=actor.Velocity;var sequence=actor.poseSequence;var sample=ActorBodySupport.Sample(actor);
                Require(anchor==actor.Position && velocity==actor.Velocity && sequence==actor.poseSequence,"Contact resolver изменил controller truth.");
                if(sample.HasContact) {contacts++;Require(sample.Frame==PonyMotionFrames.Sample(actor,actor.poseTime),"Final controller phase расходится с body sample.");Contact(sample,null);}
                Require(ActorTerrainMotion.Supported(room,Anatomy.Movement(actor),actor.Position),"Body support изменил маршрут движения.");
                var repeat=ActorBodySupport.Sample(actor);Require(repeat.BodyPosition==sample.BodyPosition && repeat.ContactPoint==sample.ContactPoint,"Все consumers одного кадра не получили один результат.");
            }
            Require(contacts>fps && (travel>0?actor.x>=9:actor.x<=-2),"Реальный controller не прошёл оба seams: "+fps+"/"+direction+"/"+travel);
        }
        private static void CheckAuthoredRooms()
        {
            var catalog=RoomCatalogAsset.Load();var hub=catalog.Resolve("base:hub").CreateLayout(false);
            var factory=catalog.Resolve("base:factory-connected-46-18").CreateLayout(false);int slopeWitnesses=0;
            foreach(var room in new[]{hub,factory}) foreach(var surface in room.platforms.Where(s=>s.slopeDirection!=0))
            {
                float x=surface.x+surface.width*.5f;var actor=Actor(x,RoomSupport.Top(surface,x,1),surface.slopeDirection>0?"slopeStandUp":"slopeStandDown");
                if(!ActorTerrainMotion.IsClear(room,Anatomy.Movement(actor),actor.Position)) continue;
                ActorBodySupport.Bind(actor,room);var sample=ActorBodySupport.Sample(actor);
                Require(sample.IsBound && sample.Grounded && sample.HasContact && sample.Outcome!=ActorBodySupportOutcome.BlockedByCeiling,
                    $"Реальная опора отвергнута: room={room.id}, surface=({surface.x},{surface.y},{surface.width},{surface.height}), outcome={sample.Outcome}.");
                Contact(sample,null);slopeWitnesses++;
            }
            Require(slopeWitnesses>0,"Нет real authored clear slope witnesses.");
        }
        private static void CheckMalformed()
        {
            var actor=Actor(0,0);var room=Room(Floor(-5,-.4f,10,.4f));ActorBodySupport.Bind(actor,room);
            actor.facing=0;Reject(()=>ActorBodySupport.Sample(actor),"facing");actor.facing=1;
            actor.poseTime=float.NaN;Reject(()=>ActorBodySupport.Sample(actor),"фаза");actor.poseTime=0;
            room.platforms=new[]{Floor(-5,-.4f,10,.4f)};Reject(()=>ActorBodySupport.Sample(actor),"без InvalidateRoom");
            ActorBodySupport.InvalidateRoom(room);Require(ActorBodySupport.Sample(actor).HasContact,"Явная новая geometry не восстановила sample.");
            room.platforms[0].width=float.NaN;Reject(()=>ActorBodySupport.InvalidateRoom(room),"поверхность");
        }
        private static void CheckRoomSound()
        {
            var floor=Floor(-4,-.4f,8,.4f);floor.movementSound=null;var room=Room(floor);room.movementSound="metal";
            var actor=Actor(0,0);ActorBodySupport.Bind(actor,room);
            Require(ActorBodySupport.Sample(actor).MovementSound=="metal","Пустой surface bank потерял явную настройку комнаты.");
            floor.movementSound="normal";ActorBodySupport.InvalidateRoom(room);
            Require(ActorBodySupport.Sample(actor).MovementSound=="normal","Явный surface bank подменён настройкой комнаты.");
        }
        private static void CheckNativeSeam()
        {
            var room=RoomCatalogAsset.Load().Resolve("base:factory-connected-46-18").CreateLayout(false);
            var shelf=room.platforms.Single(s=>s.x==71.2f && s.y==3.2f && s.width==.8f && s.height==.8f && s.slopeDirection==0);
            var stair=room.platforms.Single(s=>s.x==72 && s.y==3.2f && s.width==.8f && s.height==.8f && s.slopeDirection==-1);
            int rightBits=BitConverter.SingleToInt32Bits(shelf.x+shelf.width);
            double nativeRight=BitConverter.Int32BitsToSingle(rightBits),wideRight=(double)shelf.x+shelf.width;
            string diagnostic=$"shelfX={Exact(shelf.x)}/0x{BitConverter.SingleToInt32Bits(shelf.x):X8}, width={Exact(shelf.width)}/0x{BitConverter.SingleToInt32Bits(shelf.width):X8}, stairX={Exact(stair.x)}/0x{BitConverter.SingleToInt32Bits(stair.x):X8}, doubleRight={Exact(wideRight)}, storedSingleRight={Exact(nativeRight)}/0x{rightBits:X8}";
            Debug.Log("Нативные binary32 границы shelf↔stair: "+diagnostic);
            Require(nativeRight==stair.x && wideRight<stair.x,
                "Сохранённый стык shelf↔stair больше не содержит прежнего float→double topology расхождения: "+diagnostic);
            float x=stair.x+stair.width*.5f;
            int shelfTopBits=BitConverter.SingleToInt32Bits(RoomSupport.Top(shelf,x,1));
            int stairTopBits=BitConverter.SingleToInt32Bits(RoomSupport.Top(stair,x,1));
            Debug.Log($"Нативная RoomSupport.Top у shelf↔stair: x={Exact(x)}, shelfTop={Exact(BitConverter.Int32BitsToSingle(shelfTopBits))}/0x{shelfTopBits:X8}, stairTop={Exact(BitConverter.Int32BitsToSingle(stairTopBits))}/0x{stairTopBits:X8}");
            Require(shelfTopBits==BitConverter.SingleToInt32Bits(4f) && stairTopBits==shelfTopBits,
                "Штатная RoomSupport.Top не подтверждает одну physical плоскость saved shelf↔stair.");
            var actor=Actor(x,BitConverter.Int32BitsToSingle(stairTopBits),"slopeStandDown");
            var feet=FootSupportCatalog.Get("pony","slopeStandDown",0);
            Oracle(feet,shelf,x,1,out bool shelfPoint);double expected=Oracle(feet,stair,x,1,out bool stairPoint);
            Require(!shelfPoint && stairPoint,"Regression не отличает пустую physical shelf от actual finite stair centres.");
            ActorBodySupport.Bind(actor,room);var result=ActorBodySupport.Sample(actor);
            Require(result.HasContact && Math.Abs(result.BodyPosition.y-expected)<Tolerance,"Canonical native endpoint потерял действительный connected stair contact.");
            Contact(result,stair);
            Array.Reverse(room.platforms);ActorBodySupport.InvalidateRoom(room);
            Require(ActorBodySupport.Sample(actor).BodyPosition==result.BodyPosition,"Native seam зависит от порядка surfaces.");
            // Repair the representation, never invent a bridge across a real finite gap.
            var separate=StateJson.Copy(stair);separate.x+=.001f;
            var gap=Room(StateJson.Copy(shelf),separate);gap.left=60;gap.right=80;
            ActorBodySupport.Bind(actor,gap);var missing=ActorBodySupport.Sample(actor);
            Require(missing.Grounded && !missing.HasContact && missing.Outcome==ActorBodySupportOutcome.NoSampledContact,
                "Canonical endpoint fix расширил finite bounds/скрыл настоящую раздельную опору.");
        }
        private static void CheckPhysicalHoldTarget()
        {
            var room=Room(Floor(-5,-.4f,10,.4f));var holder=Actor(-2,0);holder.id="hold-holder";
            foreach(bool pending in new[]{false,true})
            {
                var body=Actor(0,0);body.id="hold-foot-body";body.attackBusy=pending;
                ActorBodySupport.Bind(body,room);var supported=ActorBodySupport.Sample(body);
                Require(supported.HasContact && supported.BodyPosition!=body.Position,"Hold regression не содержит действительный nonzero foot fit.");
                var copy=StateJson.Copy(body);var visualTarget=new TelekinesisTarget(body);var canonicalTarget=new TelekinesisTarget(copy);
                Require(visualTarget.PhysicalCenter==canonicalTarget.PhysicalCenter && visualTarget.Center!=canonicalTarget.Center,
                    "Физический hold центр зависит от transient binding либо regression не отличает его от рисунка.");
                var anchor=body.Position;var expected=visualTarget.PhysicalCenter;
                ActorHoldRules.Begin(holder,body,"physical-hold");ActorHoldRules.Begin(holder,copy,"physical-hold");
                Require(body.hold.Target==expected && copy.hold.Target==expected && body.Position==anchor && copy.Position==anchor &&
                    body.hold.pendingAttack==pending && copy.hold.pendingAttack==pending,
                    "Начальная цель hold сдвинула anchor/зависит от canonical copy/потеряла pending attack.");
                if(pending)
                {
                    Require(!ActorHoldRules.Active(body) && ActorBodySupport.Sample(body).BodyPosition==supported.BodyPosition,
                        "Pending attack преждевременно отключил реальный foot fit.");
                    var world=new WorldState();world.actors.Add(holder);world.actors.Add(body);body.attackBusy=false;
                    Require(ActorHoldRules.Reconcile(world,new[]{holder.owner}) && ActorHoldRules.Active(body) && body.hold.Target==expected,
                        "Завершение pending attack изменило физическую цель вместо штатного начала hold.");
                }
                var held=ActorBodySupport.Sample(body);
                Require(held.Outcome==ActorBodySupportOutcome.Suspended && held.BodyPosition==anchor && !held.HasContact,
                    "Active hold продолжает применять grounded foot fit.");
                ActorHoldMotion.Step(body,room,1f/60);
                Require(body.Position==anchor && body.Velocity==Vector2.zero && body.hold.Target==expected,
                    "Первый production hold step превратил foot fit в незапрошенное физическое движение.");
                ActorBodySupport.Unbind(body);
            }
        }
        private static void Contact(ActorBodySupportSample sample,RoomSurface source)
        {
            Require(sample.HasContact && sample.ContactPoint.x>=sample.SurfaceBounds.xMin-.00001f && sample.ContactPoint.x<=sample.SurfaceBounds.xMax+.00001f,"Contact вышел за finite surface.");
            double gradient=sample.SlopeDirection==0?0:(double)sample.SlopeDirection*sample.SurfaceBounds.height/sample.SurfaceBounds.width;
            double height=sample.SurfaceBounds.y+(sample.SlopeDirection<=0?sample.SurfaceBounds.height:0)+gradient*(sample.ContactPoint.x-sample.SurfaceBounds.x);
            Require(Math.Abs(sample.ContactPoint.y-height)<Tolerance,"Actual sampled point не лежит на опоре.");
            if(source!=null) Require(sample.SurfaceBounds==new Rect(source.x,source.y,source.width,source.height),"Contact provenance выбрал другой floor.");
        }
        private static void Reject(Action action,string diagnostic)
        {
            try {action();} catch(InvalidOperationException error) {Require(error.Message.Contains(diagnostic),"Нет contextual RU diagnostic: "+error.Message);return;}
            throw new InvalidOperationException("Ожидался явный отказ опоры: "+diagnostic);
        }
        private static void Require(bool condition,string message)
        {if(!condition) throw new InvalidOperationException("ActorBodySupportChecks: "+message);}
        private static string Exact(double value)=>value.ToString("R",CultureInfo.InvariantCulture);
    }
}
