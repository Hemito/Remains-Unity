using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class BodySupportReplicaChecks
    {
        private const string Local="replica-observer",Remote="replica-source";
        // Float world coordinates around140m lose about15 micrometres per ULP.
        // This is a pair-arithmetic tolerance, not a widened floor/contact rule.
        private const float PairTolerance=.00005f,ContactTolerance=.0002f;
        [Serializable] private sealed class Evidence
        {
            public string limitation="Native Editor проверяет настоящую геометрию/renderer и самостоятельную очередь20Гц; KCP, обычный игровой маршрут с NPC и original runtime не проверяются.";
            public List<Case> cases=new();
        }
        [Serializable] private sealed class Case
        {
            public int fps;public bool sprint;
            public int sent,lost,accepted,rejected,interpolated,rendered,slopeFrames,physicalSupportedFrames,contacts,unsupported;
            public long terrainPairSnaps;public int exactTerrainPairSnaps;
            public float maximumPairError,maximumRootError,maximumContactError;
            public List<string> unsupportedExamples=new();
        }
        private readonly struct Packet
        {
            public readonly double At;public readonly ActorState State;
            public Packet(double at,ActorState state) {At=at;State=state;}
        }
        private sealed class Route
        {public RoomLayout room;public RoomSurface[] slopes;public Vector2 start;public float lower,upper;}

        public static void Run()
        {
            Anatomy.Load();ActorTypes.Load();WeaponCatalog.Load();PonyVisual.Prepare();FootSupportCatalog.Load();
            var route=ReadRoute();string before=StateJson.ToJson(route.room);var evidence=new Evidence();
            foreach(int fps in new[]{30,60,144}) foreach(bool sprint in new[]{false,true})
            {var result=new Case {fps=fps,sprint=sprint};CheckRoute(route,result);CheckContexts(route,fps);evidence.cases.Add(result);}
            CheckTerrainPairContexts(route);CheckEmptyConfirmedContact();
            Require(before==StateJson.ToJson(route.room),"Replica checks изменили сохранённую геометрию.");
            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/body-support-replica"));
            Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"verification.json"),StateJson.ToJson(evidence,true));
            Debug.Log("Реплика опоры тела: PASS — saved Warehouse40/controller,30/60/144, walk/Shift,20Гц с задержкой/потерей/повтором/перестановкой, целые confirmed пары на изломах, actual pony root/contact, pause/loss и смена lifetime; подробные contact/unsupported/terrainPairSnaps counts: "+directory+".");
        }
        private static Route ReadRoute()
        {
            var module=AssetDatabase.LoadAssetAtPath<GameObject>(FactoryWarehouseDraft.Path);
            Require(module!=null,"Нет saved Warehouse40: "+FactoryWarehouseDraft.Path);
            var source=module.GetComponent<RoomAuthoring>().CreateLayout(false);
            // The existing connected contour supports the lower bank; the standalone
            // module intentionally has source bottom gaps, never filled by this check.
            var room=RoomCatalogAsset.Load().Resolve("base:factory-connected-46-18").CreateLayout(false);
            float offset=FactoryWarehouseConnectionAuthoring.Offset;
            var slopes=room.platforms.Where(p=>p.slopeDirection==1 && p.x>=offset).OrderBy(p=>p.x).ToArray();
            var local=source.platforms.Where(p=>p.slopeDirection==1).OrderBy(p=>p.x).ToArray();
            Require(slopes.Length==4 && local.Length==4,"Нет четырёх actual Warehouse40 ступеней в связном заводе.");
            for(int i=0;i<4;i++)
            {
                Require(Mathf.Abs(slopes[i].x-local[i].x-offset)<.00005f && slopes[i].y==local[i].y &&
                    slopes[i].width==local[i].width && slopes[i].height==local[i].height && slopes[i].oneWay &&
                    slopes[i].shotsPassThrough && slopes[i].movementSound=="metal","Warehouse40 source/connected surface расходятся: "+i);
                if(i>0) Require(Mathf.Abs(slopes[i-1].x+slopes[i-1].width-slopes[i].x)<.00005f &&
                    Mathf.Abs(slopes[i-1].y+slopes[i-1].height-slopes[i].y)<.00001f,"Ступени не образуют declared finite chain.");
            }
            float lower=slopes[0].x,upper=slopes[^1].x+slopes[^1].width;
            var start=new Vector2(lower-1,slopes[0].y);var hull=Anatomy.Movement("pony");
            Require(ActorTerrainMotion.IsClear(room,hull,start) && ActorTerrainMotion.Supported(room,hull,start),
                "Настоящая нижняя опора связного склада отсутствует/занята: "+start);
            return new Route {room=room,slopes=slopes,start=start,lower=lower,upper=upper};
        }
        private static ActorState Actor(Route route)=>new()
        {id="body-replica",owner=Remote,player=true,connected=true,generation=1,level="factory:0",Position=route.start};
        private static void Stamp(ActorState actor,double now)
        {actor.motionSequence++;actor.motionClockGeneration=actor.generation;actor.motionSentAt=604800+now;}
        private static List<Packet> Produce(Route route,Case result,out ActorState first)
        {
            var actor=Actor(route);ActorBodySupport.Bind(actor,route.room);Stamp(actor,0);first=actor.Copy();
            var packets=new List<Packet>();var controller=new MovementController(route.room);
            var cadence=new Remains.Networking.ReplicationCadence(.05);float dt=1f/result.fps,held=0;int stage=0;
            try
            {
                for(int tick=0;tick<result.fps*10;tick++)
                {
                    double now=(double)tick/result.fps;
                    if(stage==0 && actor.x>=route.upper+.7f) {stage=1;held=0;}
                    if(stage==1 && held>=.35f) stage=2;
                    if(stage==2 && actor.x<=route.lower-.7f) {stage=3;held=0;}
                    float input=stage==0?1:stage==2?-1:0;
                    ActorPose.Advance(actor,dt);controller.Step(actor,Vector2.right*input,false,dt,sprint:result.sprint);
                    if(input==0) held+=dt;
                    Require(ActorTerrainMotion.Supported(route.room,Anatomy.Movement(actor),actor.Position),"Owner controller потерял actual physical support.");
                    if(cadence.TryAdvance(now))
                    {
                        Stamp(actor,now);result.sent++;
                        if(actor.motionSequence%7==0) result.lost++;
                        else
                        {
                            double delay=actor.motionSequence%3==0?.145:.025;
                            packets.Add(new Packet(now+delay,actor.Copy()));
                            if(actor.motionSequence%11==0) packets.Add(new Packet(now+delay+.08,actor.Copy()));
                        }
                    }
                    if(stage==3 && held>=.4f) break;
                    Require(tick<result.fps*10-1,"Owner controller не закончил обычный ascent/descent.");
                }
                Require(stage==3 && actor.x<=route.lower-.7f && Mathf.Abs(actor.vx)<.001f,"Не подтверждено обычное возвращение на lower bank.");
                packets.Sort((a,b)=>a.At.CompareTo(b.At));return packets;
            }
            finally {ActorBodySupport.Unbind(actor);}
        }
        private static void Step(RemoteActorMotion motion,WorldState world,ActorState actor,float dt,double now,bool paused=false)
        {
            motion.Step(world,Local,dt,_=>paused,1000+now,true);
            // Match the production caller: only the owner of a phase advances it.
            if(!paused && !motion.ControlsPosePhase(actor)) ActorPose.Advance(actor,dt);
        }
        private static void CheckRoute(Route route,Case result)
        {
            var packets=Produce(route,result,out var initial);var actor=initial.Copy();ActorBodySupport.Bind(actor,route.room);
            var world=new WorldState();world.actors.Add(actor);var motion=new RemoteActorMotion();
            var root=new GameObject("Проверка bound replica");var visual=root.AddComponent<PonyVisual>();visual.Bind(actor);
            var confirmed=initial;var frames=new Dictionary<string,HashSet<int>>();float dt=1f/result.fps;int next=0;
            try
            {
                for(int tick=0;tick<Math.Ceiling((packets[^1].At+1)*result.fps);tick++)
                {
                    double now=(double)tick/result.fps;var previous=actor.Position;float phase=actor.poseTime;
                    string previousPose=actor.pose;uint previousSequence=actor.poseSequence;
                    while(next<packets.Count && packets[next].At<=now)
                    {
                        var incoming=packets[next++].State;
                        if(!ActorMotionReplication.IsNewer(incoming.motionSequence,actor.motionSequence))
                        {
                            string unchanged=StateJson.ToJson(actor);var stale=incoming.Copy();ActorMotionReplication.PreserveNewerMotion(actor,stale);
                            Require(unchanged==StateJson.ToJson(actor) && stale.motionSequence==actor.motionSequence &&
                                stale.Position==actor.Position && stale.poseTime==actor.poseTime,"Отказ старого packet потерял highwater motion.");result.rejected++;continue;
                        }
                        ActorPose.CopyMotion(incoming,actor);confirmed=incoming;result.accepted++;
                    }
                    long snaps=motion.TerrainPairSnaps;var expectedFields=actor.Copy();
                    Step(motion,world,actor,dt,now);
                    var actualFields=actor.Copy();actualFields.Position=expectedFields.Position;actualFields.poseTime=expectedFields.poseTime;
                    Require(StateJson.ToJson(actualFields)==StateJson.ToJson(expectedFields),"Remote body pairing изменило поля вне position/phase.");
                    if(motion.TerrainPairSnaps!=snaps)
                    {
                        Require(motion.TerrainPairSnaps==snaps+1 && actor.Position==confirmed.Position && actor.poseTime==confirmed.poseTime,
                            "TerrainPairSnap не выбрал целую последнюю принятую пару.");
                        result.exactTerrainPairSnaps++;
                    }
                    Require(motion.ControlsPosePhase(actor),"Bound active pony phase имеет два clocks.");
                    if(actor.pose==previousPose && actor.poseSequence==previousSequence && Math.Abs(confirmed.poseTime-phase)>.00001f)
                    {
                        float fraction=(actor.poseTime-phase)/(confirmed.poseTime-phase);
                        Require(fraction>=-.0001f && fraction<=1.0001f,"Replica phase вышла за confirmed pair interval.");
                        float error=Vector2.Distance(actor.Position,Vector2.LerpUnclamped(previous,confirmed.Position,fraction));
                        result.maximumPairError=Mathf.Max(result.maximumPairError,error);
                        Require(error<=PairTolerance,$"Body/phase используют разные weights: fps={result.fps}, sprint={result.sprint}, tick={tick}, error={error:R}.");
                        if(next>0 && next<packets.Count && packets[next-1].At<now-dt && Vector2.Distance(actor.Position,previous)>.00001f) result.interpolated++;
                    }
                    Observe(actor,route,visual,result,frames);
                }
                Require(result.lost>0 && result.rejected>0 && result.accepted>0 && result.interpolated>0,"Нет действительной loss/reorder/duplicate/intermediate проверки.");
                Require(frames.TryGetValue("slopeWalkUp",out var up) && up.Count>1 &&
                    frames.TryGetValue("slopeWalkDown",out var down) && down.Count>1,"Реплика не показала разные actual moving frames вверх и вниз.");
                Require(actor.x<=route.lower-.69f && actor.pose=="idle" && result.contacts>0 && result.physicalSupportedFrames>0,
                    "Отсутствует actual return/contact evidence.");
                result.terrainPairSnaps=motion.TerrainPairSnaps;
                Require(result.terrainPairSnaps>0 && result.terrainPairSnaps==result.exactTerrainPairSnaps && result.unsupported==0,
                    "Actual route потеряла строгий contact либо не проверила целый confirmed snap на изломе.");
                CheckPauseAndLoss(route,motion,world,actor,confirmed,dt,result);
            }
            finally {ActorBodySupport.Unbind(actor);UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void Observe(ActorState actor,Route route,PonyVisual visual,Case result,Dictionary<string,HashSet<int>> frames)
        {
            var sample=ActorBodySupport.Sample(actor);var anchor=actor.Position;var body=actor.BodyPosition;
            visual.DrawPose(actor.poseTime);result.rendered++;
            float rootError=Vector2.Distance(visual.transform.position,body);result.maximumRootError=Mathf.Max(result.maximumRootError,rootError);
            Require(rootError<.00001f && visual.DrawnFrame==sample.Frame && visual.DrawnClip==sample.Pose,
                "Actual replica renderer имеет другой root/frame/clip.");
            bool supported=ActorTerrainMotion.Supported(route.room,Anatomy.Movement(actor),anchor);
            if(supported) result.physicalSupportedFrames++;
            Require(!sample.Grounded || supported,"Prepared grounding не подтверждается actual physical hull.");
            if(supported && actor.vy==0)
                Require(sample.Grounded && sample.HasContact,"Actual supported active replica без vertical motion потеряла foot contact.");
            if(sample.Grounded)
            {
                Require(sample.HasContact,"Физически grounded replica потеряла actual contact: "+actor.pose+"/"+sample.Frame+"/"+sample.Outcome);
                result.contacts++;var bounds=sample.SurfaceBounds;
                double gradient=sample.SlopeDirection==0?0:(double)sample.SlopeDirection*bounds.height/bounds.width;
                double top=bounds.y+(sample.SlopeDirection<=0?bounds.height:0)+gradient*(sample.ContactPoint.x-bounds.x);
                float error=(float)Math.Abs(sample.ContactPoint.y-top);result.maximumContactError=Mathf.Max(result.maximumContactError,error);
                Require(sample.ContactPoint.x>=bounds.xMin-PairTolerance && sample.ContactPoint.x<=bounds.xMax+PairTolerance && error<=ContactTolerance,
                    "Replica contact вышел за actual finite surface.");
                var feet=FootSupportCatalog.Get(actor.anatomy,sample.Pose,sample.Frame);bool found=false;
                foreach(var group in feet.Groups) if(group.PartIndex==sample.PartIndex) foreach(var point in group.Points)
                    if(point.PixelX==sample.PixelX && point.PixelY==sample.PixelY)
                    {
                        found=true;Require(Math.Abs(sample.ContactPoint.y-body.y-point.Y)<=ContactTolerance &&
                            Math.Abs(sample.ContactPoint.x-anchor.x-actor.facing*point.X)<=PairTolerance,"Contact provenance не принадлежит фактически sampled foot.");
                    }
                Require(found,"Нет pixel provenance фактического контакта реплики.");
            }
            else
            {
                result.unsupported++;
                if(result.unsupportedExamples.Count<16) result.unsupportedExamples.Add($"pose={actor.pose}, frame={sample.Frame}, anchor={anchor}, phase={actor.poseTime:R}, outcome={sample.Outcome}");
                Require(!sample.HasContact && body==anchor,"Unsupported intermediate anchor получил скрытый fitted-body fallback.");
            }
            if(actor.pose is "slopeWalkUp" or "slopeWalkDown" && Mathf.Abs(actor.vx)>.1f)
            {
                result.slopeFrames++;if(!frames.TryGetValue(actor.pose,out var set)) frames.Add(actor.pose,set=new());
                if(sample.Frame>0) set.Add(sample.Frame);
            }
            Require(actor.Position==anchor,"Renderer/contact изменили движение реплики.");
        }
        private static void CheckPauseAndLoss(Route route,RemoteActorMotion motion,WorldState world,ActorState actor,ActorState confirmed,float dt,Case result)
        {
            // Reach steady motion using the real controller, then publish two
            // same-path samples so pause actually has unfinished interpolation.
            var source=confirmed.Copy();var controller=new MovementController(route.room);ActorBodySupport.Bind(source,route.room);
            try
            {
                int advances=0;
                while(source.x<route.lower+1)
                {
                    ActorPose.Advance(source,.05f);controller.Step(source,Vector2.right,false,.05f);
                    Require(++advances<100,"Не создана ordinary steady source пара для pause/loss.");
                }
                Stamp(source,20);ActorPose.CopyMotion(source,actor);Step(motion,world,actor,dt,20);
                ActorPose.Advance(source,.05f);controller.Step(source,Vector2.right,false,.05f);Stamp(source,20.05);
            }
            finally {ActorBodySupport.Unbind(source);}
            ActorPose.CopyMotion(source,actor);Step(motion,world,actor,dt,20.05);
            Require(Vector2.Distance(actor.Position,source.Position)>.0001f && Math.Abs(actor.poseTime-source.poseTime)>.00001f,
                "Pause fixture не создала реальную unfinished anchor/phase correction.");
            var frozen=actor.Position;float phase=actor.poseTime;var body=actor.BodyPosition;
            long snaps=motion.TerrainPairSnaps;
            for(int i=0;i<result.fps;i++) Step(motion,world,actor,dt,20.05+(double)i/result.fps,true);
            Require(actor.Position==frozen && actor.poseTime==phase && actor.BodyPosition==body,"Пауза продолжает body/phase residual interpolation.");
            for(int i=0;i<result.fps;i++)
            {
                Step(motion,world,actor,dt,21+(double)i/result.fps);
                Require(actor.x>=Mathf.Min(frozen.x,source.x)-PairTolerance && actor.x<=Mathf.Max(frozen.x,source.x)+PairTolerance &&
                    actor.poseTime>=Mathf.Min(phase,source.poseTime)-.00001f && actor.poseTime<=Mathf.Max(phase,source.poseTime)+.00001f,
                    "Потеря packets придумала новую position/phase после последней confirmed пары.");
            }
            Require(Vector2.Distance(actor.Position,source.Position)<.0002f && Math.Abs(actor.poseTime-source.poseTime)<.00001f,"Replica не дошла до последней confirmed пары.");
            var settled=actor.Position;float settledPhase=actor.poseTime;
            for(int i=0;i<result.fps;i++) Step(motion,world,actor,dt,22+(double)i/result.fps);
            Require(actor.Position==settled && actor.poseTime==settledPhase,"Packetless confirmed phase продолжает жить самостоятельным clock.");
            Require(motion.TerrainPairSnaps==snaps,"Пауза/потеря пакетов на одной подтверждённой плоскости добавила terrain snap.");
        }
        private static void CheckContexts(Route route,int fps)
        {
            var actor=Actor(route);ActorBodySupport.Bind(actor,route.room);var world=new WorldState();world.actors.Add(actor);
            var motion=new RemoteActorMotion();float dt=1f/fps;
            try
            {
                Stamp(actor,0);Step(motion,world,actor,dt,0);actor.owner=Local;actor.poseTime=.4f;
                var owned=actor.Position;motion.Step(world,Local,dt,_=>false,1001,true);
                Require(actor.Position==owned && actor.poseTime==.4f && !motion.ControlsPosePhase(actor),"Remote pairing вмешалось после локального ownership.");
                foreach(Action reset in new Action[]{()=>{actor.owner=Remote;actor.generation++;},()=>actor.lifecycleSequence++,
                    ()=>actor.level="factory:6",()=>world.worldId=Guid.NewGuid().ToString("N")})
                {
                    reset();actor.pose="idle";actor.poseSequence++;actor.poseTime=.7f;actor.motionClockGeneration=-1;
                    var anchor=actor.Position;motion.Step(world,Local,dt,_=>false,1002,true);
                    Require(actor.Position==anchor && actor.poseTime==.7f,"Новый context унаследовал старую position/phase correction.");
                    Stamp(actor,2);motion.Step(world,Local,dt,_=>false,1002,true);
                    Require(actor.Position==anchor && actor.poseTime==.7f && motion.ControlsPosePhase(actor),"Новая confirmed clock не восстановила pairing.");
                }
                actor.down=true;actor.pose="down";actor.poseSequence++;Stamp(actor,3);motion.Step(world,Local,dt,_=>false,1003,true);
                var down=ActorBodySupport.Sample(actor);
                Require(!motion.ControlsPosePhase(actor) && !down.HasContact && down.BodyPosition==actor.Position,"Down сохранил live foot fit/phase authority.");
                actor.down=false;actor.pose="idle";actor.poseSequence++;actor.hold=new ActorHoldState {holder="actual-holder",token="check-hold"};
                Stamp(actor,4);motion.Step(world,Local,dt,_=>false,1004,true);var held=ActorBodySupport.Sample(actor);
                Require(!held.HasContact && held.BodyPosition==actor.Position && held.Outcome==ActorBodySupportOutcome.Suspended,
                    "Поднятое тело получило floor fit вместо explicit hold anchor.");
                actor.hold=null;actor.lifecycleSequence++;Stamp(actor,5);motion.Step(world,Local,dt,_=>false,1005,true);
                Require(ActorBodySupport.Sample(actor).HasContact && motion.ControlsPosePhase(actor),"Release не вернул новую актуальную foot/phase пару.");
                ActorBodySupport.Unbind(actor);motion.Step(world,Local,dt,_=>false,1006,true);
                Require(!motion.ControlsPosePhase(actor) && actor.BodyPosition==actor.Position,"Unbound actor сохраняет opt-in foot pairing.");
            }
            finally {ActorBodySupport.Unbind(actor);}
        }
        private readonly struct Chord
        {
            public readonly ActorState First,Last;public readonly float Delta,Fraction;
            public Chord(ActorState first,ActorState last,float delta)
            {First=first;Last=last;Delta=delta;Fraction=1-Mathf.Exp(-delta/RemoteActorMotion.CorrectionSeconds);}
        }
        private static Chord ReadUnsupportedChord(Route route)
        {
            var packets=Produce(route,new Case {fps=60,sprint=true},out _);
            var states=packets.Select(p=>p.State).GroupBy(s=>s.motionSequence).Select(g=>g.First()).OrderBy(s=>s.motionSequence).ToArray();
            float dt=RemoteActorMotion.CorrectionSeconds*Mathf.Log(2);float fraction=1-Mathf.Exp(-dt/RemoteActorMotion.CorrectionSeconds);
            for(int i=1;i<states.Length;i++)
            {
                var a=states[i-1];var b=states[i];
                if(a.pose!=b.pose || a.poseSequence!=b.poseSequence || a.climbing || b.climbing || a.vy!=0 || b.vy!=0 ||
                    (a.Velocity-b.Velocity).sqrMagnitude>.000001f) continue;
                var probe=a.Copy();ActorBodySupport.Bind(probe,route.room);
                try
                {
                    if(!ActorBodySupport.Sample(probe).HasContact) continue;
                    ActorPose.CopyMotion(b,probe);if(!ActorBodySupport.Sample(probe).HasContact) continue;
                    probe.Position=Vector2.LerpUnclamped(a.Position,b.Position,fraction);probe.poseTime=Mathf.LerpUnclamped(a.poseTime,b.poseTime,fraction);
                    if(ActorBodySupport.Sample(probe).Outcome==ActorBodySupportOutcome.Unsupported)
                        return new Chord(a,b,dt);
                }
                finally {ActorBodySupport.Unbind(probe);}
            }
            throw new InvalidOperationException("Нет actual owner Warehouse пары с неподдержанным промежуточным chord.");
        }
        private static void CheckTerrainPairContexts(Route route)
        {
            var chord=ReadUnsupportedChord(route);
            foreach(string context in new[]{"valid","paused","missing-clock","inactive-level","hold","drop","down","climb","air","unbound","invalid-endpoint","changed-geometry","new-owner","new-generation","new-lifecycle","new-level","new-world","new-pose"})
            {
                var actor=chord.First.Copy();var world=new WorldState();world.actors.Add(actor);var motion=new RemoteActorMotion();
                ActorBodySupport.Bind(actor,route.room);
                try
                {
                    Step(motion,world,actor,chord.Delta,0);var origin=actor.Position;float originPhase=actor.poseTime;
                    ActorPose.CopyMotion(chord.Last,actor);
                    switch(context)
                    {
                        case "missing-clock":actor.motionClockGeneration=-1;break;
                        case "inactive-level":actor.player=false;break;
                        case "hold":actor.hold=new ActorHoldState {holder="test-holder",token="test-hold"};break;
                        case "drop":ActorBodySupport.SetDropping(actor,true);break;
                        case "down":actor.down=true;break;
                        case "climb":actor.climbing=true;break;
                        case "air":actor.vy=1;break;
                        case "unbound":ActorBodySupport.Unbind(actor);break;
                        case "invalid-endpoint":actor.y+=1;break;
                        case "changed-geometry":
                            var emptyRoom=new RoomLayout {id="changed-confirmed-room",left=route.room.left,right=route.room.right,
                                bottom=route.room.bottom,top=route.room.top,platforms=Array.Empty<RoomSurface>(),ladders=Array.Empty<RoomLadder>()};
                            ActorBodySupport.Bind(actor,emptyRoom);break;
                        case "new-owner":actor.owner="replacement-owner";actor.motionClockGeneration=-1;break;
                        case "new-generation":actor.generation++;actor.motionClockGeneration=-1;break;
                        case "new-lifecycle":actor.lifecycleSequence++;actor.motionClockGeneration=-1;break;
                        case "new-level":actor.level="factory:6";actor.motionClockGeneration=-1;break;
                        case "new-world":world.worldId=Guid.NewGuid().ToString("N");break;
                        case "new-pose":actor.pose="idle";actor.poseSequence++;break;
                    }
                    var received=actor.Position;float receivedPhase=actor.poseTime;
                    Step(motion,world,actor,chord.Delta,.05,context=="paused");
                    if(context=="valid")
                    {
                        Require(motion.TerrainPairSnaps==1 && actor.Position==received && actor.poseTime==receivedPhase && ActorBodySupport.Sample(actor).HasContact,
                            "Неподдержанный actual chord не заменён целой валидной confirmed парой.");
                        for(int i=0;i<20;i++) Step(motion,world,actor,chord.Delta,.1+i*chord.Delta);
                        Require(motion.TerrainPairSnaps==1 && actor.Position==received && actor.poseTime==receivedPhase,
                            "Повторное чтение confirmed пары добавило новый snap/движение.");
                        motion.Clear();Require(motion.TerrainPairSnaps==1,"Clear сбросил lifetime diagnostic counter.");
                    }
                    else
                    {
                        Require(motion.TerrainPairSnaps==0,"Недопустимый context получил terrain snap: "+context);
                        if(context is "paused" or "missing-clock" or "inactive-level")
                            Require(actor.Position==origin && actor.poseTime==originPhase,"Остановленный clock продолжает correction: "+context);
                        if(context is "invalid-endpoint" or "changed-geometry")
                        {
                            var intermediate=Vector2.LerpUnclamped(origin,received,chord.Fraction);
                            float intermediatePhase=Mathf.LerpUnclamped(originPhase,receivedPhase,chord.Fraction);
                            Require(Vector2.Distance(actor.Position,intermediate)<=PairTolerance && Math.Abs(actor.poseTime-intermediatePhase)<.00001f,
                                "Отказ candidate не восстановил prospective Position/phase: "+context);
                        }
                    }
                }
                finally {ActorBodySupport.Unbind(actor);}
            }
        }
        private static void CheckEmptyConfirmedContact()
        {
            var feet=FootSupportCatalog.Get("pony","idle",0);
            var xs=feet.Groups.SelectMany(g=>g.Points).Select(p=>p.X).Where(x=>x>-.49 && x<.49).Distinct().OrderBy(x=>x).ToArray();
            Require(xs.Length>1,"Нет native exact-X данных для empty confirmed contact.");
            int gap=0;for(int i=1;i<xs.Length-1;i++) if(xs[i+1]-xs[i]>xs[gap+1]-xs[gap]) gap=i;
            double center=(xs[gap]+xs[gap+1])/2,half=(xs[gap+1]-xs[gap])*.1;
            var room=new RoomLayout {id="empty-confirmed-contact",left=-10,right=10,bottom=-5,top=10,ladders=Array.Empty<RoomLadder>(),platforms=new[]{
                new RoomSurface {x=-2.5f,y=-.2f,width=1,height=.2f,movementSound="metal"},
                new RoomSurface {x=(float)(center-half),y=-.2f,width=(float)(half*2),height=.2f,movementSound="metal"}}};
            var actor=new ActorState {id="empty-confirmed-actor",owner=Remote,player=true,connected=true,generation=1,level="factory:0",x=-2};
            var world=new WorldState();world.actors.Add(actor);var motion=new RemoteActorMotion();ActorBodySupport.Bind(actor,room);
            try
            {
                Stamp(actor,0);Step(motion,world,actor,.01f,0);actor.x=0;actor.poseTime=.1f;Stamp(actor,.05);
                Require(ActorBodySupport.Sample(actor).Outcome==ActorBodySupportOutcome.NoSampledContact,"Endpoint fixture не является actual finite empty.");
                float dt=RemoteActorMotion.CorrectionSeconds*Mathf.Log(2);Step(motion,world,actor,dt,.05);
                Require(motion.TerrainPairSnaps==0 && Math.Abs(actor.x+1)<PairTolerance && Math.Abs(actor.poseTime-.05f)<.00001f &&
                    ActorBodySupport.Sample(actor).Outcome==ActorBodySupportOutcome.Unsupported,
                    "Finite empty confirmed endpoint придумал contact либо candidate отказ потерял промежуточную пару.");
            }
            finally {ActorBodySupport.Unbind(actor);}
        }
        private static void Require(bool condition,string reason)
        {if(!condition) throw new InvalidOperationException(reason);}
    }
}
