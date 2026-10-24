using kcp2k;

namespace Remains.Networking
{
    // KcpTransport with each server connection's reliable backlog visible (kcp2k keeps its server protected). The
    // session pauses world ticks to a peer whose link cannot keep up instead of letting kcp2k disconnect it at
    // 10000 queued segments (KcpPeer.QueueDisconnectThreshold).
    public sealed class RemainsKcpTransport : KcpTransport
    {
        // Segments queued and in flight to a connection (0 for an unknown one, such as the host's local client).
        public int ServerBacklog(int connectionId)=>
            server!=null && server.connections.TryGetValue(connectionId,out var connection)?connection.SendQueueCount+connection.SendBufferCount:0;
    }
}
