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
                // The 2D Animation fallback-buffer release is a package patch not taken here (LIGHT-TEARDOWN in tech-debt).
                ActorShadowTeardownChecks.Run();
                Debug.Log("LIGHT_TEARDOWN_PASS: nine active render-target teardown cases.");
                EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
    }
}
