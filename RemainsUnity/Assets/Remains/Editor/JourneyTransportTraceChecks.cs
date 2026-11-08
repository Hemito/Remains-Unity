using System;
using System.Reflection;
using Mirror;
using Remains.Networking;
using Remains.Runtime;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class JourneyTransportTraceChecks
    {
        private const BindingFlags Hidden=BindingFlags.Static|BindingFlags.Instance|BindingFlags.NonPublic;
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Run()
        {
            if(NetworkServer.active || NetworkClient.active) throw new InvalidOperationException("Проверке нужна отдельная сессия.");
            var root=new GameObject("Trace lifecycle checks");var transport=root.AddComponent<kcp2k.KcpTransport>();
            typeof(kcp2k.KcpTransport).GetMethod("Awake",Hidden).Invoke(transport,null);
            var session=root.AddComponent<CoopSession>();session.transport=transport;Transport.active=transport;
            var trace=root.AddComponent<JourneyTransportTrace>();trace.runInEditMode=true;trace.Initialize(session);
            bool previousDontListen=NetworkServer.dontListen;
            try
            {
                for(int reconnect=0;reconnect<2;reconnect++)
                {
                    // Reproduce the actual static reset which used to erase the early subscription.
                    typeof(NetworkDiagnostics).GetMethod("ResetStatics",Hidden).Invoke(null,null);
                    NetworkServer.dontListen=true;NetworkServer.Listen(2);NetworkClient.ConnectHost();HostMode.InvokeOnConnected();
                    Ready(session,"ServerReady");Ready(session,"ClientReady");Ready(session,"ClientReady");
                    int handled=0;
                    NetworkServer.ReplaceHandler<ReadyMessage>((connection,_)=>{handled++;connection.Send(new NotReadyMessage());},false);
                    NetworkClient.ReplaceHandler<NotReadyMessage>(_=>handled++,false);
                    int sent=trace.MessagesOut,received=trace.MessagesIn;
                    NetworkClient.Send(new ReadyMessage());
                    for(int i=0;i<6;i++)
                    {
                        Pump(NetworkClient.connection);Pump(NetworkServer.localConnection);
                    }
                    Require(handled==2,"Настоящие сообщения Mirror не дошли в обе стороны.");
                    Require(trace.MessagesOut-sent==2 && trace.MessagesIn-received==2,"Переподключение потеряло диагностику или удвоило подписку.");
                    NetworkClient.Disconnect();NetworkClient.Shutdown();NetworkServer.Shutdown();
                }
                var outField=typeof(NetworkDiagnostics).GetField("OutMessageEvent",Hidden);
                var inField=typeof(NetworkDiagnostics).GetField("InMessageEvent",Hidden);
                UnityEngine.Object.DestroyImmediate(trace);
                foreach(var field in new[]{outField,inField})
                    foreach(var callback in ((Delegate)field.GetValue(null))?.GetInvocationList()??Array.Empty<Delegate>())
                        Require(callback.Target!=trace,"Уничтоженная диагностика оставила static подписку.");
                Debug.Log("JOURNEY_TRACE_PASS: real Mirror bidirectional local messages after static reset and reconnect; no duplicate or leaked subscriptions.");
            }
            finally
            {
                NetworkClient.Shutdown();NetworkServer.Shutdown();NetworkServer.dontListen=previousDontListen;
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
        private static void Ready(CoopSession session,string name)=>((Action)typeof(CoopSession).GetField(name,Hidden).GetValue(session))?.Invoke();
        private static void Pump(object connection)=>connection.GetType().GetMethod("Update",Hidden).Invoke(connection,null);
        private static void Require(bool condition,string message) {if(!condition) throw new InvalidOperationException(message);}
    }
}
