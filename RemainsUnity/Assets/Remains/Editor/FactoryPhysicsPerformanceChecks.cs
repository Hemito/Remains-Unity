using System;
using System.Diagnostics;
using System.IO;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;
using Debug=UnityEngine.Debug;

namespace Remains.Editor
{
    // Deliberately measures unchanged public terrain queries on real authored prop positions.
    // It is a CPU microbenchmark, not a claim about complete rendered frame time.
    public static class FactoryPhysicsPerformanceChecks
    {
        [Serializable] private sealed class Report
        {
            public bool success;
            public string detail,workload="IsClear + Move + Supported + CanDrop for every factory prop and a player hull";
            public int seed=104,surfaces,props,containers,repeats=3,samples=5,queriesPerSample;
            public double medianMilliseconds;
            public double[] sampleMilliseconds;
            public long checksum;
            public double medianFreePropTickMilliseconds;
            public double[] freePropTickMilliseconds;
            // Steady state with body sleep: after 120 settling ticks, the mean of 60 ticks and the bodies still stepped.
            public double steadyFreePropTickMilliseconds;public int steadySteppedPerTick,steadySleepingPerTick;
            public long freePropChecksum;
            public int steppedPerTick;
        }
        public static void RunBatch()
        {
            var args=Environment.GetCommandLineArgs();string path=null;
            for(int i=0;i+1<args.Length;i++) if(args[i]=="-factoryPhysicsReport") path=args[i+1];
            var report=new Report();FactoryLevelRoot root=null;
            try
            {
                if(string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Нужен -factoryPhysicsReport с путём отчёта.");
                CheckSupportIndex();
                CheckPoseCadence();
                Anatomy.Load();WeaponCatalog.Load();
                var catalog=GameAssets.Load<FactoryCatalogAsset>(FactoryCatalogAsset.ResourceKey);
                if(catalog==null) throw new InvalidOperationException("Нет каталога завода.");
                var world=new WorldState {worldId="factory-physics-benchmark",seed=104};
                FactoryPreparation.Create(world,catalog,0);
                root=FactoryLevelRoot.Create(world,"factory:0");root.gameObject.SetActive(false);
                root.InstantiateRooms();root.GetComponent<FactoryPropAdapter>().Install(root);
                root.GetComponent<FactoryContainerAdapter>().Install(root,world);
                var room=root.Prepared.Physics;
                // Production publishes the index at RoomLayout.Load / RoomDoorGeometry;
                // this fixture intentionally reads the prepared grid directly.
                RoomSurfaces.Prepare(room);
                var layout=RoomPhysicalContent.Read(root.Authoring,"factory:0");
                var props=layout.props;
                report.containers=layout.containers.Length;
                if(props.Length==0 || room.platforms.Length<1000) throw new InvalidOperationException("Не загружены полная геометрия и физические объекты завода.");
                report.props=props.Length;report.surfaces=room.platforms.Length;
                report.queriesPerSample=report.repeats*props.Length*2*4;
                Work(room,props,1); // warm up JIT and geometry caches outside timing
                report.sampleMilliseconds=new double[report.samples];long checksum=0;
                for(int sample=0;sample<report.samples;sample++)
                {
                    var timer=Stopwatch.StartNew();long current=Work(room,props,report.repeats);timer.Stop();
                    if(sample>0 && current!=checksum) throw new InvalidOperationException("Результат физики меняется между неизменными выборками.");
                    checksum=current;report.sampleMilliseconds[sample]=timer.Elapsed.TotalMilliseconds;
                }
                report.checksum=checksum;var sorted=(double[])report.sampleMilliseconds.Clone();Array.Sort(sorted);
                report.medianMilliseconds=sorted[sorted.Length/2];report.success=true;
                world.actors.Add(new ActorState {id="benchmark-player",owner="benchmark-owner",player=true,connected=true,level="factory:0",x=room.spawnX,y=room.spawnY});
                foreach(var prop in props) {prop.owner="benchmark-owner";world.props.Add(prop);}
                foreach(var container in layout.containers) {container.owner="benchmark-owner";world.containers.Add(container);}
                CheckFullSupportIndex(world,room,report.repeats);
                report.freePropTickMilliseconds=new double[report.samples];
                for(int sample=-1;sample<report.samples;sample++)
                {
                    // Copy before timing: every sample starts with identical authoritative state.
                    var copy=StateJson.Copy(world);var context=new FreePropMotionContext();
                    var timer=Stopwatch.StartNew();int stepped=0;
                    for(int tick=0;tick<report.repeats;tick++) stepped+=FreePropMotion.Step(copy,"benchmark-owner",1f/60,_=>room,_=>false,null,null,context);
                    timer.Stop();if(sample<0) continue;
                    report.freePropTickMilliseconds[sample]=timer.Elapsed.TotalMilliseconds/report.repeats;
                    report.steppedPerTick=stepped/report.repeats;
                    long state=17;foreach(var prop in WorldProps.All(copy)) unchecked {state=state*31+Mathf.RoundToInt(prop.x*10000);state=state*31+Mathf.RoundToInt(prop.y*10000);state=state*31+Mathf.RoundToInt(prop.vx*10000);state=state*31+Mathf.RoundToInt(prop.vy*10000);}
                    if(sample>0 && state!=report.freePropChecksum) throw new InvalidOperationException("Свободные объекты изменяют результат между одинаковыми выборками.");
                    report.freePropChecksum=state;
                }
                {
                    var copy=StateJson.Copy(world);var context=new FreePropMotionContext();
                    for(int tick=0;tick<120;tick++) FreePropMotion.Step(copy,"benchmark-owner",1f/60,_=>room,_=>false,null,null,context);
                    var timer=Stopwatch.StartNew();int stepped=0,sleeping=0;
                    for(int tick=0;tick<60;tick++) {stepped+=FreePropMotion.Step(copy,"benchmark-owner",1f/60,_=>room,_=>false,null,null,context);sleeping+=context.LastSleeping;}
                    timer.Stop();
                    report.steadyFreePropTickMilliseconds=timer.Elapsed.TotalMilliseconds/60;report.steadySteppedPerTick=stepped/60;report.steadySleepingPerTick=sleeping/60;
                }
                var propSorted=(double[])report.freePropTickMilliseconds.Clone();Array.Sort(propSorted);
                report.medianFreePropTickMilliseconds=propSorted[propSorted.Length/2];
                int eligible=0;foreach(var prop in WorldProps.All(world)) if(prop.movable && prop.hasPhysicalBody && !prop.held) eligible++;
                if(report.steppedPerTick!=eligible) throw new InvalidOperationException("Не все физические объекты прошли реальный тик.");
                report.detail="PASS: реальная сетка seed 104, реальные положения и габариты объектов; CPU terrain microbenchmark.";
            }
            catch(Exception error) {report.success=false;report.detail=error.ToString();Debug.LogError(report.detail);}
            finally
            {
                if(root!=null) UnityEngine.Object.DestroyImmediate(root.gameObject);
                if(!string.IsNullOrWhiteSpace(path)) {Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));File.WriteAllText(path,JsonUtility.ToJson(report,true));}
                Debug.Log($"Физика завода: {report.detail}, surfaces={report.surfaces}, props={report.props}, median={report.medianMilliseconds:F3} ms, checksum={report.checksum}.");
                EditorApplication.Exit(report.success?0:1);
            }
        }
        private static long Work(RoomLayout room,PropState[] props,int repeats)
        {
            long checksum=17;var hull=new MovementHull();
            for(int iteration=0;iteration<repeats;iteration++) foreach(var prop in props) for(int mode=0;mode<2;mode++)
            {
                hull.width=mode==0?prop.width:1.2f;hull.height=mode==0?prop.height:1.6f;
                var start=new Vector2(prop.x,prop.y-prop.anchorHeight);
                var delta=new Vector2((iteration%2==0?1:-1)*.08f,-.01f);
                bool clear=ActorTerrainMotion.IsClear(room,hull,start);
                var moved=ActorTerrainMotion.Move(room,hull,start,delta,false);
                bool supported=ActorTerrainMotion.Supported(room,hull,moved);
                bool drop=ActorTerrainMotion.CanDrop(room,hull,moved);
                unchecked {checksum=checksum*31+(clear?1:0)+(supported?2:0)+(drop?4:0);checksum=checksum*31+Mathf.RoundToInt(moved.x*10000);checksum=checksum*31+Mathf.RoundToInt(moved.y*10000);}
            }
            return checksum;
        }
        private static void CheckSupportIndex()
        {
            var world=new WorldState();
            var lower=new PropState {id="index-lower",level="a",hasPhysicalBody=true,x=3.9f,y=0,width=12,height=1};
            var upper=new PropState {id="index-upper",level="a",hasPhysicalBody=true,x=4.1f,y=.5f,width=2,height=1};
            var foreign=new PropState {id="index-foreign",level="b",hasPhysicalBody=true,x=4,y=1,width=12,height=1};
            world.props.Add(lower);world.props.Add(upper);world.props.Add(foreign);
            var source=new WorldPropView(world);var index=new PropSupportIndex();index.Prepare(source);
            var moving=new PropState {id="index-moving",level="a",hasPhysicalBody=true,width=1,height=1};
            void Compare(float x)
            {
                var start=new Vector2(x,3);var exhaustive=new Vector2(x,-1);var indexed=exhaustive;
                bool expected=PropSupport.Resolve(moving,start,ref exhaustive,source);
                bool actual=PropSupport.Resolve(moving,start,ref indexed,index);
                if(expected!=actual || exhaustive!=indexed) throw new InvalidOperationException("Индекс опоры расходится с полным обходом: x="+x);
            }
            foreach(float x in new[]{-3f,0f,3.99f,4.01f,9f,15f}) Compare(x);
            float previous=upper.x;upper.x=15;index.Moved(upper,previous);
            Compare(4);Compare(15);
            world.props.Remove(lower);index.Prepare(source);Compare(0);Compare(15);
            var replacement=new WorldState();replacement.props.Add(foreign);source=new WorldPropView(replacement);index.Prepare(source);
            Compare(4);Compare(15);
        }
        private static void CheckFullSupportIndex(WorldState world,RoomLayout room,int ticks)
        {
            var indexed=StateJson.Copy(world);var linear=StateJson.Copy(world);
            var context=new FreePropMotionContext();var all=new WorldPropView(linear);var hull=new MovementHull();
            for(int tick=0;tick<ticks;tick++)
            {
                FreePropMotion.Step(indexed,"benchmark-owner",1f/60,_=>room,_=>false,null,null,context);
                foreach(var prop in all)
                    if(prop.owner=="benchmark-owner" && prop.movable && prop.hasPhysicalBody && !prop.held)
                        PropMotion.Step(prop,room,1f/60,null,all,null,hull);
                var actual=new WorldPropView(indexed);
                for(int i=0;i<all.Count;i++)
                    if(actual[i].Position!=all[i].Position || actual[i].vx!=all[i].vx || actual[i].vy!=all[i].vy)
                        throw new InvalidOperationException($"Индекс изменил физику: tick={tick}, id={all[i].id}, indexed={actual[i].Position}, linear={all[i].Position}.");
            }
        }
        private static void CheckPoseCadence()
        {
            var policy=new FreePropPoseCadence();
            var prop=new PropState {id="pose-rest",owner="a",level="a",generation=1};
            void Require(bool value,string reason) {if(!value) throw new InvalidOperationException("Публикация позы: "+reason);}
            Require(policy.ShouldPublish(prop,0),"нет первой позы");
            Require(!policy.ShouldPublish(prop,.001),"неизменная поза публикуется снова");
            prop.x=1;prop.vx=2;Require(policy.ShouldPublish(prop,.002),"движение не опубликовано");
            prop.vx=0;Require(policy.ShouldPublish(prop,.003),"финальная остановка не опубликована");
            Require(policy.ShouldPublish(prop,1.003),"потерянная финальная поза не восстановилась heartbeat");
            Require(!policy.ShouldPublish(prop,1.004),"повтор heartbeat без ожидания");
            prop.generation++;Require(policy.ShouldPublish(prop,1.005),"новая generation скрыта");
            prop.owner="b";Require(policy.ShouldPublish(prop,1.006),"новый владелец скрыт");
            // A new graph of the same body (every received snapshot replaces World objects) with the same pose is not
            // republished: that republished every body with a new sequence each snapshot (TD-15). A changed pose is.
            Require(!policy.ShouldPublish(new PropState {id=prop.id,owner=prop.owner,level=prop.level,generation=prop.generation,x=prop.x},1.007),"тот же ID и поза в новом объекте публикуются снова");
            Require(policy.ShouldPublish(new PropState {id=prop.id,owner=prop.owner,level=prop.level,generation=prop.generation,x=prop.x+1},1.0075),"новый объект с другой позой скрыт");
            policy.Reset();Require(policy.ShouldPublish(prop,1.008),"новая эпоха мира скрыта");
            var spread=new FreePropPoseCadence();var bodies=new PropState[1000];
            for(int i=0;i<bodies.Length;i++) {bodies[i]=new PropState {id="pose-distribution-"+i};spread.ShouldPublish(bodies[i],0);}
            int maximum=0,total=0;
            for(int tick=1;tick<=20;tick++)
            {
                int emitted=0;foreach(var body in bodies) if(spread.ShouldPublish(body,tick*.05+.00001)) emitted++;
                maximum=Math.Max(maximum,emitted);total+=emitted;
            }
            Require(total==1000 && maximum<100,"неподвижные heartbeat собрались в один сетевой всплеск");
        }
    }
}
