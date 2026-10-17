using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Remains.Content;
using Remains.Domain;
using Remains.Diagnostics;
using Remains.Persistence;
using Remains.Runtime;
using Remains.Serialization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.Profiling;

namespace Remains.Editor
{
    // Runs the saved production scene in a hidden batch Editor. Each invocation creates a fresh
    // seed104 fixture; repeated runs never continue a world modified by an earlier measurement.
    [InitializeOnLoad]
    public static class Seed104CpuBaselineChecks
    {
        private const string Key="Remains.Seed104CpuBaseline";
        static Seed104CpuBaselineChecks()
        {if(SessionState.GetBool(Key,false)) EditorApplication.update+=Watch;}

        public static void RunBatch()
        {
            try
            {
                var options=LaunchOptions.Parse();
                if(!Application.isBatchMode || options.Role!="host" || options.Smoke!="seed104-baseline" ||
                    string.IsNullOrEmpty(options.SaveDirectory) || Directory.Exists(options.SaveDirectory) || string.IsNullOrEmpty(options.Report))
                    throw new InvalidOperationException("CPU-замер требует batchmode, host, seed104-baseline, нового save-dir и report.");
                CheckStatistics();Anatomy.Load();WeaponCatalog.Load();
                PrepareInput(options);
                SessionState.SetString(Key+".report",options.Report);
                SessionState.SetFloat(Key+".deadline",(float)EditorApplication.timeSinceStartup+600);
                SessionState.SetBool(Key,true);
                EditorApplication.update+=Watch;
                EditorSceneManager.OpenScene("Assets/Remains/Scenes/Remains.unity");
                EditorApplication.EnterPlaymode();
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        private static void Watch()
        {
            string output=SessionState.GetString(Key+".report","");
            if(File.Exists(output))
            {
                var result=JsonUtility.FromJson<Result>(File.ReadAllText(output));
                Finish(result.success?0:1);return;
            }
            if(EditorApplication.timeSinceStartup>SessionState.GetFloat(Key+".deadline",0))
            {Debug.LogError("Таймаут CPU-замера seed104.");Finish(1);}
        }
        private static void Finish(int code)
        {SessionState.SetBool(Key,false);EditorApplication.update-=Watch;EditorApplication.Exit(code);}
        private static void PrepareInput(LaunchOptions options)
        {
            string directory=Seed104CpuBaselineProbe.Argument("--baseline-fixture",null);
            if(string.IsNullOrEmpty(directory)) throw new ArgumentException("Нужен --baseline-fixture: один неизменяемый исходный мир для всех повторов.");
            string worldPath=Path.Combine(directory,"world.json"),actorPath=Path.Combine(directory,"character.json");
            if(File.Exists(worldPath)!=File.Exists(actorPath)) throw new InvalidOperationException("Неполный исходный набор CPU-замера: "+directory);
            if(!File.Exists(worldPath))
            {
                // Generate once. Even fixed world/seed is insufficient for all content GUIDs;
                // repeat invocations restore the exact two JSON inputs, never regenerate them.
                var world=new WorldState {seed=104,worldId="64ab04d6f71844dba014ce97fa6f347b",branchId="1b17fa9845504183a40af0e0151fbb7a",
                    weaponContent=WeaponCatalog.ManifestCopy()};
                FactoryPreparation.Create(world,Resources.Load<FactoryCatalogAsset>(FactoryCatalogAsset.ResourceKey),0);
                try
                {
                    FactoryContentSources.Prepare(world,new[]{"hub","factory:0"});
                    foreach(string level in new[]{"hub","factory:0"})
                        RoomContentInstaller.Apply(world,RoomContentBatch.Read(RoomAuthoring.Load(level,world),level));
                    var actor=new ActorState {id="player:"+options.Character,character=options.Character,level="hub",player=true,
                        weaponContent=WeaponCatalog.ManifestCopy()};
                    PlayerStarterInventory.Give(actor);
                    InventoryStacks.Add(actor,SharedInventory.Create("p10",300));
                    Directory.CreateDirectory(directory);
                    File.WriteAllText(worldPath,StateJson.ToJson(world));File.WriteAllText(actorPath,StateJson.ToJson(actor));
                }
                finally {FactoryContentSources.Clear();}
            }
            string worldText=File.ReadAllText(worldPath),actorText=File.ReadAllText(actorPath);
            var restored=StateJson.FromJson<WorldState>(worldText);var player=StateJson.FromJson<ActorState>(actorText);
            if(restored.seed!=104 || player.character!=options.Character) throw new InvalidOperationException("Исходный набор CPU-замера имеет другой seed или персонажа.");
            var saves=new SaveStore(options.SaveDirectory);
            using(var hash=SHA256.Create())
            {
                string digest=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(worldText+"\n"+actorText))).Replace("-","").ToLowerInvariant();
                File.WriteAllText(Path.Combine(options.SaveDirectory,"baseline-input.sha256"),digest);
            }
            saves.SaveWorld(restored);saves.SaveCharacter(player);
        }
        private static void CheckStatistics()
        {
            CpuBaselineCapture.Reset();CpuBaselineCapture.FrameLimit=3;CpuBaselineCapture.Enabled=true;
            CpuBaselineCapture.Section("проверка",1,10);CpuBaselineCapture.Frame(3,1,100);
            CpuBaselineCapture.Frame(4,1,200);
            CpuBaselineCapture.Section("проверка",2,20);CpuBaselineCapture.Frame(100,5,300);
            var result=CpuBaselineCapture.Summarize();
            if(CpuBaselineCapture.Enabled || result.frameCount!=3 || result.total.p95!=100 || result.work.p50!=3 ||
                result.allocationsPerFrame.mean!=200 || result.workFramesOverBudget!=1 || result.metrics[0].perFrame.p50!=1 ||
                result.metrics[0].perCall.count!=2 || result.metrics[0].allocatedBytes!=30)
                throw new InvalidOperationException("Сборщик CPU-замера неверно считает квантили, пустые кадры или аллокации.");
            CpuBaselineCapture.Enabled=false;CpuBaselineCapture.Reset();
        }
        public static void AllocationCounters()
        {
            foreach(var spec in new[]{(ProfilerCategory.Internal,"GC.Alloc"),(ProfilerCategory.Memory,"GC Allocated In Frame")})
            {
                using var recorder=ProfilerRecorder.StartNew(spec.Item1,spec.Item2,1024,
                    ProfilerRecorderOptions.StartImmediately|ProfilerRecorderOptions.CollectOnlyOnCurrentThread|ProfilerRecorderOptions.SumAllSamplesInFrame);
                long before=recorder.CurrentValue;
                var allocation=new byte[4096];allocation[0]=1;
                long after=recorder.CurrentValue;
                var worker=new System.Threading.Thread(()=>{var bytes=new byte[8*1024*1024];bytes[0]=1;GC.KeepAlive(bytes);});
                long threadBefore=recorder.CurrentValue;worker.Start();worker.Join();long threadAfter=recorder.CurrentValue;
                recorder.Stop();
                Debug.Log($"Счётчик {spec.Item2}: valid={recorder.Valid}, unit={recorder.UnitType}, before={before}, after={after}, workerDelta={threadAfter-threadBefore}, count={recorder.Count}, last={recorder.LastValue}, sampleCount={(recorder.Count==0?0:recorder.GetSample(0).Count)}.");
                GC.KeepAlive(allocation);
            }
            EditorApplication.Exit(0);
        }
        [Serializable] private sealed class Result {public bool success;}
    }
}
