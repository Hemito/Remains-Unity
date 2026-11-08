using System;
using UnityEditor;
using UnityEngine;
namespace Remains.Editor
{
    public static class Td16NativeValidationChecks
    {
        public static void RunBatch()
        {
            try
            {
                HostWorldChecks.RunCore();GrenadeTelekinesisChecks.Run();
                Debug.Log("TD16_SHARED_HOST_NATIVE_PASS: original hold/fuse/sequence/copy checks and host core contracts; paired Player probes remain separate.");
                EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
    }
}
