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
        private int sent,received,messagesOut,messagesIn;
        private float next;
        public void Initialize(Transport value)
        {
            transport=value;
            transport.OnClientDataSent+=Sent;transport.OnServerDataReceived+=Received;
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
            if(transport!=null) {transport.OnClientDataSent-=Sent;transport.OnServerDataReceived-=Received;}
            NetworkDiagnostics.OutMessageEvent-=Outgoing;NetworkDiagnostics.InMessageEvent-=Incoming;
        }
    }
}
