using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor.Build.Reporting;
using UnityEditor;
using UnityEngine;
using Debug=UnityEngine.Debug;
namespace Remains.Editor
{
    // Runs the combined branch with real Unity objects and keeps every targeted result.
    public static class CloudGoalAcceptanceChecks
    {
        [Serializable] public sealed class Case {public string name,status,detail;public long milliseconds;}
        [Serializable] public sealed class Report {public string revision,utc,version,renderer;public List<Case> cases=new();}
        public static void BuildWindowsBatch()=>BuildStandalone(BuildTarget.StandaloneWindows64,true,"Windows","Remains.exe");
        public static void BuildWindowsReleaseBatch()=>BuildStandalone(BuildTarget.StandaloneWindows64,false,"WindowsRelease","Remains.exe");
        public static void BuildLinuxBatch()=>BuildStandalone(BuildTarget.StandaloneLinux64,true,"Linux","Remains");
        private static void BuildStandalone(BuildTarget target,bool development,string folder,string executable)
        {
            try
            {
                if(!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone,target))
                    throw new InvalidOperationException("Requested build module is not recognized: "+target);
                ActorShadowSourceChecks.Run();
                var scenes=EditorBuildSettings.scenes.Where(scene=>scene.enabled).Select(scene=>scene.path).ToArray();
                if(scenes.Length!=1) throw new InvalidOperationException("Expected one saved startup scene.");
                string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../Builds",folder,executable));
                var build=BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=scenes,locationPathName=output,target=target,options=development?BuildOptions.Development:BuildOptions.None});
                if(build.summary.result!=BuildResult.Succeeded) throw new InvalidOperationException("Player build failed: "+build.summary.result);
                Debug.Log("CLOUD_GOAL_PLAYER_BUILD_PASS target="+target+" development="+development+" output="+output+" bytes="+build.summary.totalSize);
                EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void RunBatch()
        {
            var report=new Report {revision=Environment.GetEnvironmentVariable("REMAINS_TEST_REVISION"),utc=DateTime.UtcNow.ToString("O"),version=Application.unityVersion,renderer=SystemInfo.graphicsDeviceName};
            int failed=0;
            void Check(string name,Action action)
            {
                var result=new Case{name=name,status="PASS",detail=""};var timer=Stopwatch.StartNew();
                try {action();}
                catch(Exception error) {failed++;result.status="FAIL";result.detail=error.GetType().Name+": "+error.Message;}
                result.milliseconds=timer.ElapsedMilliseconds;report.cases.Add(result);
                Debug.Log("CLOUD_GOAL_CASE "+name+" "+result.status+" "+result.detail);
            }
            Check("weapon grip and GPU capture",WeaponGripGlowChecks.Run);
            Check("animation fallback buffer",AnimationBufferLifetimeChecks.Run);
            Check("shadow mask teardown",ActorShadowTeardownChecks.Run);
            Check("Mirror diagnostics reconnect",JourneyTransportTraceChecks.Run);
            Check("firearm content",FirearmChecks.Run);
            Check("ladder entry",LadderEntryPoseChecks.Run);
            Check("foot declaration",LadderPoseContactChecks.RunDeclarationChecks);
            Check("jump pose",JumpPoseChecks.Run);
            Check("standing edge",StandingEdgePoseChecks.Run);
            Check("standing support",StandingSupportPoseChecks.Run);
            Check("factory loot",FactoryLootChecks.Run);
            Check("factory encounters and danger",FactoryRandomEncounterChecks.Run);
            Check("grate projectile",GrateProjectileChecks.Run);
            Check("horizontal motion",HorizontalMotionChecks.Run);
            Check("variable jump",VariableJumpChecks.Run);
            Check("platform drop",PlatformDropChecks.Run);
            Check("host shared graph and authority",HostWorldChecks.Run);
            Check("shared ladder controller",RoomEditorLadderChecks.Run);
            Check("empty room layer geometry",RoomEmptyLayerGeometryChecks.Run);
            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Verification/cloud-goal"));Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory,"results.json"),JsonUtility.ToJson(report,true));
            Debug.Log("CLOUD_GOAL_SUMMARY total="+report.cases.Count+" failed="+failed);
            EditorApplication.Exit(failed==0?0:1);
        }
    }
}
