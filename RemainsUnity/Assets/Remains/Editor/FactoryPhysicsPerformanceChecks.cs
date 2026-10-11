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
            public int seed=104,surfaces,props,repeats=3,samples=5,queriesPerSample;
            public double medianMilliseconds;
            public double[] sampleMilliseconds;
            public long checksum;
            public double medianFreePropTickMilliseconds;
            public double[] freePropTickMilliseconds;
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
                Anatomy.Load();WeaponCatalog.Load();
                var catalog=Resources.Load<FactoryCatalogAsset>(FactoryCatalogAsset.ResourceKey);
                if(catalog==null) throw new InvalidOperationException("Нет каталога завода.");
                var world=new WorldState {worldId="factory-physics-benchmark",seed=104};
                FactoryPreparation.Create(world,catalog,0);
                root=FactoryLevelRoot.Create(world,"factory:0");root.gameObject.SetActive(false);
                root.InstantiateRooms();root.GetComponent<FactoryPropAdapter>().Install(root);
                var room=root.Prepared.Physics;
                // Production publishes the index at RoomLayout.Load / RoomDoorGeometry;
                // this fixture intentionally reads the prepared grid directly.
                RoomSurfaces.Prepare(room);
                var props=RoomPhysicalContent.Read(root.Authoring,"factory:0").props;
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
                    long state=17;foreach(var prop in copy.props) unchecked {state=state*31+Mathf.RoundToInt(prop.x*10000);state=state*31+Mathf.RoundToInt(prop.y*10000);state=state*31+Mathf.RoundToInt(prop.vx*10000);state=state*31+Mathf.RoundToInt(prop.vy*10000);}
                    if(sample>0 && state!=report.freePropChecksum) throw new InvalidOperationException("Свободные объекты изменяют результат между одинаковыми выборками.");
                    report.freePropChecksum=state;
                }
                var propSorted=(double[])report.freePropTickMilliseconds.Clone();Array.Sort(propSorted);
                report.medianFreePropTickMilliseconds=propSorted[propSorted.Length/2];
                if(report.steppedPerTick!=props.Length) throw new InvalidOperationException("Не все физические объекты прошли реальный тик.");
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
    }
}
