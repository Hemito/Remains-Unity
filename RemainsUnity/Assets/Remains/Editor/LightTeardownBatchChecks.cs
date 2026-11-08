using System;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class LightTeardownBatchChecks
    {
        public static void RunBatch()
        {
            try
            {
                for(int i=0;i<3;i++) AnimationBufferLifetimeChecks.Run();
                ActorShadowTeardownChecks.Run();
                Debug.Log("LIGHT_TEARDOWN_PASS: six fallback lifetimes and nine active render-target teardown cases.");
                EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
    }
}
