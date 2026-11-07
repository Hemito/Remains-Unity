using System;
using System.Reflection;
using Remains.Presentation;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    public static class ActorShadowTeardownChecks
    {
        public static void Run()
        {
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)
                throw new InvalidOperationException("Проверке освобождения масок нужен графический контекст.");
            var previous=RenderTexture.active;
            long bytes=LightTextureBudget.Bytes;int count=LightTextureBudget.Count;
            try
            {
                for(int iteration=0;iteration<3;iteration++)
                    for(int active=0;active<3;active++) Check(active);
                if(LightTextureBudget.Bytes!=bytes || LightTextureBudget.Count!=count)
                    throw new InvalidOperationException("Освобождение масок изменило бюджет чужих текстур.");
            }
            finally {RenderTexture.active=previous;}
            Debug.Log("Освобождение масок теней: PASS — активная маска позы/проекции, чужая цель, повторное создание и уничтожение.");
        }
        private static void Check(int active)
        {
            GameObject root=null;RenderTexture pose=null,projection=null,unrelated=null;
            string releaseError=null;
            void Observe(string message,string stack,LogType type)
            {
                if(type is LogType.Error or LogType.Exception or LogType.Assert ||
                    type==LogType.Warning && message.IndexOf("render texture",StringComparison.OrdinalIgnoreCase)>=0)
                    releaseError=message;
            }
            try
            {
                root=new GameObject("Проверка освобождения масок");var view=root.AddComponent<ActorCastShadowView>();
                // This batch check runs in Edit Mode; opt this fixture into Unity callbacks,
                // otherwise DestroyImmediate skips OnDestroy on the runtime-only behaviour.
                view.runInEditMode=true;
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                object slot=typeof(ActorCastShadowView).GetMethod("GetSlot",flags).Invoke(view,new object[]{0});
                projection=(RenderTexture)slot.GetType().GetField("mask").GetValue(slot);
                pose=new RenderTexture(16,16,0);unrelated=new RenderTexture(16,16,0);
                if(!pose.Create() || !unrelated.Create() || !projection.IsCreated())
                    throw new InvalidOperationException("Не созданы маски проверки освобождения.");
                typeof(ActorCastShadowView).GetField("poseMask",flags).SetValue(view,pose);
                RenderTexture.active=active==0?pose:active==1?projection:unrelated;
                Application.logMessageReceived+=Observe;
                Object.DestroyImmediate(root);root=null;
                Application.logMessageReceived-=Observe;
                if(releaseError!=null) throw new InvalidOperationException("Освобождение масок: "+releaseError);
                if(pose!=null || projection!=null) throw new InvalidOperationException("Маски пережили владельца.");
                if(RenderTexture.active!=(active==2?unrelated:null))
                    throw new InvalidOperationException("После освобождения осталась неверная активная цель.");
            }
            finally
            {
                Application.logMessageReceived-=Observe;
                if(root!=null) Object.DestroyImmediate(root);
                LightTextureBudget.Release(pose);LightTextureBudget.Release(projection);LightTextureBudget.Release(unrelated);
            }
        }
    }
}
