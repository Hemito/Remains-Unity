using System;
using Mirror;
using Remains.Networking;
using UnityEngine;

namespace Remains.Runtime
{
    // Only attached by the isolated journey probe. Does not alter traffic or callbacks.
    public sealed class JourneyTransportTrace : MonoBehaviour
    {
        private Transport transport;
        private CoopSession session;
        private int sent,received,messagesOut,messagesIn;
        private float next;
        public int MessagesOut=>messagesOut;
        public int MessagesIn=>messagesIn;
        public void Initialize(CoopSession value)
        {
            if(value==null) throw new ArgumentNullException(nameof(value));
            if(session!=null) throw new InvalidOperationException("Диагностика маршрута уже подключена.");
            session=value;transport=value.transport;
            if(transport==null) throw new InvalidOperationException("Нет транспорта диагностики маршрута.");
            transport.OnClientDataSent+=Sent;transport.OnServerDataReceived+=Received;
            // Mirror clears its static diagnostic events after scene load.
            // Session readiness runs after startup, and runs again on reconnect.
            session.ClientReady+=BindMessages;session.ServerReady+=BindMessages;
            if(NetworkClient.active || NetworkServer.active) BindMessages();
        }
        private void BindMessages()
        {
            // Host startup invokes both ready callbacks: replace our own subscription only.
            NetworkDiagnostics.OutMessageEvent-=Outgoing;NetworkDiagnostics.InMessageEvent-=Incoming;
            NetworkDiagnostics.OutMessageEvent+=Outgoing;NetworkDiagnostics.InMessageEvent+=Incoming;
        }
        private void Sent(ArraySegment<byte> data,int channel) {sent++;}
        private void Received(int connection,ArraySegment<byte> data,int channel) {received++;}
        private void Outgoing(NetworkDiagnostics.MessageInfo info) {messagesOut++;}
        private void Incoming(NetworkDiagnostics.MessageInfo info) {messagesIn++;}
        public void Sample(int phase)
        {
            if(phase<6 || phase>8 || Time.unscaledTime<next) return;
            next=Time.unscaledTime+3;
            Debug.Log($"Диагностика маршрута: time={Time.unscaledTime}, phase={phase}, client={NetworkClient.active}/{NetworkClient.isConnected}/{NetworkClient.ready}, loading={NetworkClient.isLoadingScene}/{NetworkServer.isLoadingScene}, packets={sent}/{received}, messages={messagesOut}/{messagesIn}, transport={transport.ClientConnected()}, serverConnections={NetworkServer.connections.Count}.");
        }
        private void OnDestroy()
        {
            if(session!=null) {session.ClientReady-=BindMessages;session.ServerReady-=BindMessages;}
            if(transport!=null) {transport.OnClientDataSent-=Sent;transport.OnServerDataReceived-=Received;}
            NetworkDiagnostics.OutMessageEvent-=Outgoing;NetworkDiagnostics.InMessageEvent-=Incoming;
        }
    }
}
