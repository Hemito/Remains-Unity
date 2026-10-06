using Mirror;

namespace Remains.Networking
{
    public struct SaveOfferMessage : NetworkMessage {public string json;public bool complete;}
    public struct LobbyStateMessage : NetworkMessage {public bool waiting,busy,choosingCopies,contentPending;public string selected,error;public int players;}
    public struct LobbyCopyMessage : NetworkMessage {public string json;}
    public struct LobbyStateEndMessage : NetworkMessage { }
    public struct SaveRequestMessage : NetworkMessage {public string request,snapshot;}
    public struct SaveChunkMessage : NetworkMessage {public string request;public int total,offset;public byte[] data;}
    public struct SaveTransferErrorMessage : NetworkMessage {public string request,error;}
}
