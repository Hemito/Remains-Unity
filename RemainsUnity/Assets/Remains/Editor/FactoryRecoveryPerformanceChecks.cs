using System;
using System.Diagnostics;
using System.IO;
using Remains.Content;
using Remains.Domain;
using Remains.Persistence;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;
using Debug=UnityEngine.Debug;

namespace Remains.Editor
{
    public static class FactoryRecoveryPerformanceChecks
    {
        [Serializable] private sealed class Report
        {
            public bool success;
            public string detail;
            public int containers,characters;
            public double serializationMilliseconds,parseMilliseconds,snapshotMilliseconds;
        }
        public static void RunBatch()
        {
            var report=new Report();string output=null;
            try
            {
                string input=null;var args=Environment.GetCommandLineArgs();
                for(int i=0;i+1<args.Length;i++)
                {if(args[i]=="-recoveryPerfWorld") input=args[i+1];if(args[i]=="-recoveryPerfReport") output=args[i+1];}
                if(input==null || output==null) throw new ArgumentException("Нужны -recoveryPerfWorld и -recoveryPerfReport.");
                Anatomy.Load();WeaponCatalog.Load();
                var world=StateJson.FromJson<WorldState>(File.ReadAllText(input));
                world.worldId=Guid.NewGuid().ToString("N");
                StateExtensionChecks.Run();RecoveryCaptureChecks.Run();
                // Warm cached CLR contracts before timing, just as in a running session.
                var expected=StateJson.ToJson(world);var warm=RecoverySnapshot.FromWorld(world);
                if(warm.Json!=expected) throw new InvalidOperationException("Снимок изменил сериализованный payload.");
                const int samples=3;var serialize=new double[samples];var parse=new double[samples];var snapshots=new double[samples];
                for(int sample=0;sample<samples;sample++)
                {
                    var timer=Stopwatch.StartNew();string json=StateJson.ToJson(world);timer.Stop();serialize[sample]=timer.Elapsed.TotalMilliseconds;
                    timer.Restart();var decoded=StateJson.FromJson<WorldState>(json);timer.Stop();parse[sample]=timer.Elapsed.TotalMilliseconds;
                    timer.Restart();var snapshot=RecoverySnapshot.FromWorld(world);timer.Stop();snapshots[sample]=timer.Elapsed.TotalMilliseconds;
                    if(json!=expected || snapshot.Json!=expected || snapshot.Id!=warm.Id || StateJson.ToJson(decoded)!=expected)
                        throw new InvalidOperationException("Payload/ID изменился между одинаковыми выборками.");
                }
                Array.Sort(serialize);Array.Sort(parse);Array.Sort(snapshots);
                report.serializationMilliseconds=serialize[1];report.parseMilliseconds=parse[1];report.snapshotMilliseconds=snapshots[1];
                report.containers=world.containers.Count;report.characters=expected.Length;
                report.success=true;report.detail="PASS: точные bytes/ID, strict extension/number guards и независимость live consumers; CPU checkpoint microbenchmark.";
            }
            catch(Exception error) {report.detail=error.ToString();Debug.LogError(error);}
            finally
            {
                if(output!=null) File.WriteAllText(output,JsonUtility.ToJson(report,true));
                Debug.Log(report.detail);EditorApplication.Exit(report.success?0:1);
            }
        }
    }
}
