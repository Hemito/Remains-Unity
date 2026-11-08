using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Remains.Runtime;
using Remains.Domain;
using Remains.Gameplay;

namespace Remains.Editor
{
    public static class HostWorldChecks
    {
        public static void Run()
        {
            Require(LaunchOptions.Parse(Array.Empty<string>()).HostSelfTicks==null,"Default must follow build type");
            Require(LaunchOptions.Parse(new[]{"--host-self-ticks","on"}).HostSelfTicks==true,"on");
            Require(LaunchOptions.Parse(new[]{"--host-self-ticks","off"}).HostSelfTicks==false,"off");
            foreach(var args in new[]{new[]{"--host-self-ticks"},new[]{"--host-self-ticks","yes"}})
            {
                bool failed=false;try{LaunchOptions.Parse(args);}catch(ArgumentException){failed=true;}
                Require(failed,"Invalid mode must fail");
            }
            CheckPresentationIsolation();
            Debug.Log("HOST_WORLD_OPTIONS_PASS");
        }
        private static void CheckPresentationIsolation()
        {
            Anatomy.Load();Remains.Content.WeaponCatalog.Load();
            foreach(int fps in new[]{30,60,144})
            {
                var actor=new ActorState {id="remote",owner="remote",level="hub",player=true,connected=true,vx=4,pose="run",motionSequence=1,motionClockGeneration=0};
                var local=new ActorState {id="local",owner="local",level="hub",player=true,connected=true};
                var world=new WorldState();world.actors.Add(actor);world.actors.Add(local);
                string canonical=Remains.Serialization.StateJson.ToJson(world);
                var motion=new RemoteActorMotion();
                for(int frame=0;frame<fps;frame++)
                {
                    motion.Step(world,"local",1f/fps,_=>false,(double)frame/fps,false,preserveWorld:true);
                    Require(Remains.Serialization.StateJson.ToJson(world)==canonical,"Render interpolation changed authoritative graph");
                }
                var shown=motion.Presentation(actor);
                Require(!ReferenceEquals(shown,actor) && ReferenceEquals(shown.inventory,actor.inventory),"Presentation must be a shallow read-only shell");
                Require(shown.x>actor.x && shown.poseTime>actor.poseTime,"Detached motion and animation did not advance");
                var delta=shown.BodyPosition-actor.BodyPosition;
                Require((HeldItemPose.Mount(shown)-HeldItemPose.Mount(actor)-delta).sqrMagnitude<1e-8f,"Held item detached from rendered body");
                Require((WeaponPose.SampleRanged(shown).Root-WeaponPose.SampleRanged(actor).Root-delta).sqrMagnitude<1e-8f,"Weapon detached from rendered body");
                Require(ReferenceEquals(motion.Presentation(local),local),"Local simulation was projected");
                actor.owner="local";motion.Step(world,"local",1f/fps,_=>false,2,false,preserveWorld:true);
                Require(ReferenceEquals(motion.Presentation(actor),actor),"Stale remote projection survived ownership transfer");
            }
            Debug.Log("HOST_WORLD_PRESENTATION_PASS");
        }
        public static void RunCore()
        {
            Run();CreatureDeathLootChecks.Run();RemoteActorMotionChecks.Run();ActorMotionReplicationChecks.Run();WorldReplicationChecks.Run();
            Debug.Log("HOST_WORLD_CORE_PASS");
        }
        public static void BuildLinuxDevelopment()=>BuildLinux(true);
        public static void BuildLinuxRelease()=>BuildLinux(false);
        private static void BuildLinux(bool development)
        {
            Run();
            var scenes=EditorBuildSettings.scenes.Where(scene=>scene.enabled).Select(scene=>scene.path).ToArray();
            if(scenes.Length!=1) throw new InvalidOperationException("Expected one saved game startup scene.");
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,development?"../Builds/HostWorldDev/Remains":"../Builds/HostWorldRelease/Remains"));
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=scenes,locationPathName=output,target=BuildTarget.StandaloneLinux64,
                options=development?BuildOptions.Development:BuildOptions.None});
            if(report.summary.result!=BuildResult.Succeeded) throw new InvalidOperationException("Linux diagnostic player build failed: "+report.summary.result);
            Debug.Log("HOST_WORLD_BUILD_PASS: "+output+", Development="+development);
        }
        private static void Require(bool pass,string message){if(!pass)throw new InvalidOperationException(message);}
    }
}
