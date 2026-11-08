using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Remains.Content;
using Remains.Domain;
using UnityEditor;
using UnityEngine;
using Debug=UnityEngine.Debug;
namespace Remains.Editor
{
    // A bounded target suite. Each result is retained even when another contract fails.
    public static class CombatMotionAcceptanceChecks
    {
        [Serializable] public sealed class Result {public string check,status,detail;public long elapsedMs;}
        [Serializable] public sealed class Report {public string version,utc;public List<Result> results=new();}
        public static void RunBatch()
        {
            var report=new Report {version=Application.unityVersion,utc=DateTime.UtcNow.ToString("O")};
            int failed=0;
            try
            {
                Anatomy.Load();WeaponCatalog.Load();ActorTypes.Load();PonyAppearance.Prepare();
                foreach(string name in new[]{"FirearmChecks","NaturalAttackChecks","NpcAttackChecks","PlayerStrikeAnimationChecks","FireChecks","GrenadeChecks","GrenadeAimChecks","RevivalChecks","LadderHoldChecks","LadderEntryPoseChecks","LadderPoseContactChecks","FactoryLadderLandingChecks","JumpPoseChecks","VariableJumpChecks","StandingEdgePoseChecks","StandingSupportPoseChecks","ActorBodySupportChecks","FootSupportContentChecks","WeaponRecoilMotionChecks","RemoteActorMotionChecks","FreePropMotionChecks","LevelPauseChecks","ProjectilePathChecks","ProjectilePersistenceChecks","ProjectileControllerChecks","LocalProjectileChecks","PonyStrikeChecks","BedRestChecks"})
                {
                    var timer=Stopwatch.StartNew();var result=new Result {check=name,status="PASS",detail=""};
                    try
                    {
                        var method=typeof(CombatMotionAcceptanceChecks).Assembly.GetType("Remains.Editor."+name,true).GetMethod("Run",BindingFlags.Public|BindingFlags.Static);
                        method.Invoke(null,null);
                    }
                    catch(Exception error)
                    {
                        var actual=error is TargetInvocationException invocation?invocation.InnerException:error;
                        result.status="FAIL";result.detail=actual.GetType().Name+": "+actual.Message;failed++;
                    }
                    finally {Remains.Domain.ActorBodySupport.Clear();}
                    result.elapsedMs=timer.ElapsedMilliseconds;report.results.Add(result);
                    Debug.Log("COMBAT_MOTION_CASE "+name+" "+result.status+" "+result.detail);
                }
            }
            catch(Exception error) {failed++;Debug.LogException(error);}
            finally
            {
                string dir=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Verification/combat-motion"));Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir,"results.json"),JsonUtility.ToJson(report,true));
            }
            Debug.Log("COMBAT_MOTION_SUMMARY: total="+report.results.Count+", failed="+failed);
            EditorApplication.Exit(failed==0?0:1);
        }
    }
}
