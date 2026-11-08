using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Remains.Runtime;

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
            Debug.Log("HOST_WORLD_OPTIONS_PASS");
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
