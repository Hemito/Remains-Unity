using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;

namespace Remains.Networking
{
    // Keep these envelopes stable when versioning gameplay messages.
    public struct ProtocolHello : NetworkMessage {public int version;}
    public struct ProtocolReply : NetworkMessage {public int version;public bool accepted;public string reason;}
    public static class SessionProtocol
    {
        public const int Version=82;   // 82: snapshot-delta transport removed (no base snapshot in world parts). 81: packed (deflate) ticks and world parts. 80: world ticks (transport v2). 79: the join profile as UTF-8 bytes. 78: bounded delta messages.
        public static string Rejection(int offered)=>offered==Version?null:
            $"Несовместимый сетевой протокол: у клиента {offered}, у хоста {Version}. Используйте совместимые сборки. Сохранения не загружены.";
    }
    public sealed class ProtocolAdmission
    {
        private readonly CoopSession session;
        private readonly HashSet<int> approved=new();
        private readonly Dictionary<int,float> waiting=new();
        private readonly Dictionary<int,float> closing=new();
        private bool pending;
        private float deadline;
        internal int OfferedVersion {get;set;}=SessionProtocol.Version;
        public int RejectedCount {get;private set;}
        public bool ClientApproved {get;private set;}
        public string LastError {get;private set;}
        public bool IsApproved(int connection)=>approved.Contains(connection);
        public ProtocolAdmission(CoopSession session) {this.session=session;}
        public void RegisterServer()
        {
            approved.Clear();waiting.Clear();closing.Clear();RejectedCount=0;
            NetworkServer.RegisterHandler<ProtocolHello>(Remains.Networking.MessageTiming.Server<ProtocolHello>((connection,message)=>
            {
                if(closing.ContainsKey(connection.connectionId)) return;
                string reason=SessionProtocol.Rejection(message.version);
                if(reason!=null) {Reject(connection,reason);return;}
                approved.Add(connection.connectionId);waiting.Remove(connection.connectionId);
                connection.Send(new ProtocolReply {version=SessionProtocol.Version,accepted=true});
            }));
        }
        public void RegisterClient()
        {
            ResetClient();
            NetworkClient.RegisterHandler<ProtocolReply>(Remains.Networking.MessageTiming.Client<ProtocolReply>(message=>
            {
                // Admission may still fail after the protocol handshake (for example, a taken character name).
                if(!pending && (!ClientApproved || message.accepted)) return;
                pending=false;
                if(!message.accepted || message.version!=SessionProtocol.Version)
                {LastError=string.IsNullOrEmpty(message.reason)?$"Хост использует несовместимый сетевой протокол {message.version}.":message.reason;session.ShowNotice(LastError);session.StopClient();return;}
                ClientApproved=true;session.ProtocolAccepted();
            }));
        }
        // Startup can spend seconds loading a room within one frame. The cached frame time
        // must not make a newly opened connection appear older than it actually is.
        public void Connected(NetworkConnectionToClient connection)=>waiting[connection.connectionId]=Time.realtimeSinceStartup+10;
        public void Disconnected(int id) {approved.Remove(id);waiting.Remove(id);closing.Remove(id);}
        public void Begin()
        {
            pending=true;ClientApproved=false;LastError=null;deadline=Time.realtimeSinceStartup+10;
            NetworkClient.Send(new ProtocolHello {version=OfferedVersion});
        }
        public void ResetClient() {pending=false;ClientApproved=false;}
        public void EndClient()
        {
            if(pending)
            {LastError="Соединение закрыто до согласования протокола. Проверьте совместимость сборок и сеть.";session.ShowNotice(LastError);}
            ResetClient();
        }
        public void Reject(NetworkConnectionToClient connection,string reason)
        {
            if(closing.ContainsKey(connection.connectionId)) return;
            approved.Remove(connection.connectionId);waiting.Remove(connection.connectionId);RejectedCount++;
            connection.Send(new ProtocolReply {version=SessionProtocol.Version,accepted=false,reason=reason});
            closing[connection.connectionId]=Time.realtimeSinceStartup+1;
            Debug.LogWarning($"Подключение отклонено: connection={connection.connectionId}, причина={reason}");
        }
        public void RejectDuplicateCharacter(NetworkConnectionToClient connection,string character)=>
            Reject(connection,$"Персонаж с именем «{character}» уже находится в сессии. Выберите другое имя в главном меню и подключитесь снова.");
        public void Step()
        {
            if(pending && Time.realtimeSinceStartup>deadline)
            {pending=false;LastError="Хост не подтвердил версию протокола. Проверьте соединение и совместимость сборок.";session.ShowNotice(LastError);session.StopClient();}
            if(!NetworkServer.active) return;
            foreach(var pair in waiting.ToArray())
                if(Time.realtimeSinceStartup>pair.Value && NetworkServer.connections.TryGetValue(pair.Key,out var connection))
                    Reject(connection,"Клиент не выполнил согласование сетевого протокола.");
            foreach(var pair in closing.ToArray())
                if(Time.realtimeSinceStartup>pair.Value)
                {closing.Remove(pair.Key);if(NetworkServer.connections.TryGetValue(pair.Key,out var connection)) connection.Disconnect();}
        }
    }
}
