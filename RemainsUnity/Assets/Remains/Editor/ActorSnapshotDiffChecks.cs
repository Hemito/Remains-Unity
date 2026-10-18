using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remains.Diagnostics;
using Remains.Domain;
using Remains.Networking;
using Remains.Persistence;
using Remains.Runtime;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Opt-in diagnostic only: deliberately expensive JSON comparisons, separate from performance runs.
    [InitializeOnLoad]
    public static class ActorSnapshotDiffChecks
    {
        private static readonly string output=Seed104CpuBaselineProbe.Argument("--actor-diff-report",null);
        private static CoopSession session;
        private static JObject previous;
        private static string previousId;
        private static readonly List<Sample> samples=new();
        static ActorSnapshotDiffChecks()
        {
            if(output==null) return;
            EditorApplication.update+=Attach;
        }
        public static void RunBatch()=>Seed104CpuBaselineChecks.RunBatch();
        private static void Attach()
        {
            if(!EditorApplication.isPlaying || session!=null) return;
            var runtime=UnityEngine.Object.FindFirstObjectByType<PrototypeRuntime>();
            if(runtime?.Session==null) return;
            session=runtime.Session;session.RecoveryPersisted+=Snapshot;
        }
        private static void Snapshot(RecoverySnapshot snapshot)
        {
            var next=JObject.Parse(snapshot.Json);
            if(previous!=null && CpuBaselineCapture.Enabled)
            {
                var world=StateJson.FromTrustedJson<WorldState>(snapshot.Json);
                var byId=((JArray)previous["actors"]).OfType<JObject>().ToDictionary(actor=>(string)actor["id"],StringComparer.Ordinal);
                var changes=new List<Change>();
                foreach(var actor in ((JArray)next["actors"]).OfType<JObject>())
                {
                    string id=(string)actor["id"];
                    if(!byId.TryGetValue(id,out var before)) throw new InvalidOperationException("Диагностика ожидает неизменный состав актёров: "+id);
                    if(JToken.DeepEquals(before,actor)) continue;
                    var paths=new List<string>();Diff(before,actor,"$",paths);
                    var state=world.Find(id);
                    // Exact spatial room rectangle, independent of the attackBusy exception in Awake.
                    bool inZone=world.actors.Any(player=>player.player && player.connected && player.level==state.level &&
                        Math.Abs(player.x-state.x)<=NpcActivity.HalfWidth && Math.Abs(player.y-state.y)<=NpcActivity.HalfHeight);
                    changes.Add(new Change {id=id,level=state.level,player=state.player,inZone=inZone,
                        awake=NpcActivity.Awake(world,state),paths=paths.ToArray()});
                }
                var clock=System.Diagnostics.Stopwatch.StartNew();
                foreach(var actor in world.actors) GC.KeepAlive(ValidatedStateCopy.Copy(actor));
                double copyMs=clock.Elapsed.TotalMilliseconds;
                samples.Add(new Sample {snapshot=snapshot.Id,baseSnapshot=previousId,frame=Time.frameCount,changes=changes.ToArray(),
                    copyAllActorsMs=copyMs,actorCount=world.actors.Count});
                Write();
            }
            previous=next;previousId=snapshot.Id;
        }
        private static void Diff(JToken before,JToken next,string path,List<string> paths)
        {
            if(JToken.DeepEquals(before,next)) return;
            if(before is JObject a && next is JObject b)
            {
                foreach(string key in a.Properties().Select(p=>p.Name).Union(b.Properties().Select(p=>p.Name),StringComparer.Ordinal))
                    Diff(a[key],b[key],path+"."+key,paths);
            }
            else if(before is JArray aa && next is JArray bb && aa.Count==bb.Count)
                for(int i=0;i<aa.Count;i++) Diff(aa[i],bb[i],path+"["+i+"]",paths);
            else paths.Add(path);
        }
        private static void Write()
        {
            var changes=samples.SelectMany(sample=>sample.changes).ToArray();
            var report=new Report {scenario=Seed104CpuBaselineProbe.Argument("--baseline-scenario","idle"),samples=samples.ToArray(),
                histogram=changes.SelectMany(change=>change.paths).GroupBy(path=>path,StringComparer.Ordinal)
                    .OrderByDescending(group=>group.Count()).ThenBy(group=>group.Key,StringComparer.Ordinal)
                    .Select(group=>new Count {path=group.Key,count=group.Count()}).ToArray(),
                inZone=changes.Count(change=>change.inZone),outOfZone=changes.Count(change=>!change.inZone),
                uniqueInZone=changes.Where(change=>change.inZone).Select(change=>change.id).Distinct().Count(),
                uniqueOutOfZone=changes.Where(change=>!change.inZone).Select(change=>change.id).Distinct().Count()};
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            File.WriteAllText(output,JsonUtility.ToJson(report,true));
        }
        [Serializable] private sealed class Change {public string id,level;public bool player,inZone,awake;public string[] paths;}
        [Serializable] private sealed class Sample {public string snapshot,baseSnapshot;public int frame,actorCount;public double copyAllActorsMs;public Change[] changes;}
        [Serializable] private sealed class Count {public string path;public int count;}
        [Serializable] private sealed class Report {public string scenario;public Sample[] samples;public Count[] histogram;public int inZone,outOfZone,uniqueInZone,uniqueOutOfZone;}
    }
}
