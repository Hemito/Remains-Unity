using System;
using System.Linq;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Remains.Editor
{
    public static class LightingOwnershipChecks
    {
        public static void RunBatch()
        {
            GameObject live=null,staged=null;
            bool duplicate=false;
            void Observe(string message,string trace,LogType type)
            {if(message.Contains("More than one global light")) duplicate=true;}
            Application.logMessageReceived+=Observe;
            try
            {
                var room=RoomLayout.Load("hub");
                live=AuthoredRoomVisual.Create("hub",room);
                staged=AuthoredRoomVisual.Create("hub",room,staged:true);
                int stagedLights=staged.GetComponentsInChildren<Light2D>(true).Count(light=>light.isActiveAndEnabled);
                if(duplicate || staged.activeInHierarchy || stagedLights!=0)
                    throw new InvalidOperationException($"Подготовка хаба включила второй глобальный свет: duplicate={duplicate}, active={staged.activeInHierarchy}, lights={stagedLights}.");
                live.GetComponent<FactoryLevelView>().Release();
                staged.SetActive(true);
                if(duplicate || staged.GetComponentsInChildren<Light2D>().Length!=1)
                    throw new InvalidOperationException("Принятие хаба не сохранило единственный глобальный свет.");
                Debug.Log("Владение светом: PASS — подготовка двух настоящих хабов, без дублирующей регистрации, смена владельца.");
                EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
            finally
            {
                Application.logMessageReceived-=Observe;
                if(live!=null) UnityEngine.Object.DestroyImmediate(live);
                if(staged!=null) UnityEngine.Object.DestroyImmediate(staged);
            }
        }
    }
}
