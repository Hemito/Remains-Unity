using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Domain;
using Remains.Persistence;
using UnityEngine;

namespace Remains.Networking
{
    public struct EntryPreparationPartMessage : NetworkMessage
    {
        public string token,snapshot,world,actor,character,level,fingerprint;
        public int total,offset;
        public byte[] data;
    }
    public struct EntryPreparedMessage : NetworkMessage {public string token,fingerprint;}

    // A pending transport connection has no simulation ownership and no recovery receipt.
    // Only the explicit Ready transition may hand it a current playable world.
    public sealed class SessionEntryPreparation
    {
        // The same state machine serves production publication and its pure checks.
        // Taken is not completed: a subsequent write/view callback may still fail.
        public sealed class ClientAdmissionState
        {
            public EntryPreparationContext Current {get;private set;}
            public bool Pending {get;private set;}
            public bool ReadySent {get;private set;}
            public void BeginReceiving()
            {
                if(Current!=null && !Pending) throw new InvalidOperationException("Новая подготовка началась до завершения Welcome.");
                Pending=true;
            }
            public void Begin(EntryPreparationContext context)
            {
                if(Current!=null && !Pending) throw new InvalidOperationException("Подготовка заменила ещё не завершённый Welcome.");
                Current=context??throw new ArgumentNullException(nameof(context));Pending=true;ReadySent=false;
            }
            public bool MarkPrepared(string token)
            {
                if(!Pending || Current==null || token!=Current.Token)
                    throw new InvalidOperationException("Готовность относится к другой подготовке входа: "+token);
                if(ReadySent) return false;
                ReadySent=true;return true;
            }
            public EntryPreparationContext Take(WorldState world,string actorId)
            {
                if(!Pending || !ReadySent || Current==null)
                    throw new InvalidDataException("Сервер подключил персонажа без завершённой подготовки локации.");
                Current.RequireWelcome(world,actorId);Pending=false;return Current;
            }
            public void CompleteAdmission()
            {
                if(Pending || !ReadySent || Current==null)
                    throw new InvalidOperationException("Нельзя завершить вход до успешного Welcome.");
                Current=null;ReadySent=false;
            }
            public bool Reset()
            {
                bool cancel=Pending || Current!=null;Current=null;ReadySent=false;Pending=false;return cancel;
            }
        }
        private sealed class Pending
        {
            public JoinMessage Join;
            public EntryPreparationTicket Ticket;
            public double BeganAt;
        }
        private readonly CoopSession session;
        private readonly Dictionary<int,Pending> pending=new();
        private SnapshotTransfer incoming;
        private EntryPreparationPartMessage header;
        private readonly ClientAdmissionState client=new();
        public bool ClientPending=>client.Pending;
        public EntryPreparationContext Current=>client.Current;
        public int ServerPendingCount=>pending.Count;
        public event Action<EntryPreparationContext> Requested;
        public event Action Cancelled;
        public event Action<EntryPreparationContext,WorldState> ClientAdmitted;
        public SessionEntryPreparation(CoopSession session) {this.session=session??throw new ArgumentNullException(nameof(session));}
        public void RegisterServer()
        {ResetServer();NetworkServer.RegisterHandler<EntryPreparedMessage>(Ready);}
        public void RegisterClient()
        {ResetClient();NetworkClient.RegisterHandler<EntryPreparationPartMessage>(Receive);}
        public void Begin(NetworkConnectionToClient connection,JoinMessage join)
        {
            if(session.ServerWorld?.actors.Any(actor=>actor.character==join.character && actor.connected)==true)
            {session.Admission.RejectDuplicateCharacter(connection,join.character);return;}
            if(pending.ContainsKey(connection.connectionId))
            {RejectReady(connection,"Повторный запрос входа во время подготовки локации.");return;}
            if(pending.Values.Any(value=>value.Join.character==join.character))
            {session.Admission.RejectDuplicateCharacter(connection,join.character);return;}
            try
            {
                var value=new Pending {Join=join,BeganAt=Time.realtimeSinceStartupAsDouble};
                pending.Add(connection.connectionId,value);Send(connection,value);
            }
            catch(Exception error) {RejectEntry(connection,error);}
        }
        private void Send(NetworkConnectionToClient connection,Pending value)
        {
            var view=session.CreateEntryView(value.Join,out var actor);
            var context=new EntryPreparationContext(Guid.NewGuid().ToString("N"),view,actor.id,value.Join.character,actor.level);
            var snapshot=RecoverySnapshot.FromWorld(view);
            value.Ticket=new EntryPreparationTicket(context,value.BeganAt);
            WorldReplication.SendParts(snapshot,part=>connection.Send(new EntryPreparationPartMessage {
                token=context.Token,snapshot=part.snapshot,total=part.total,offset=part.offset,data=part.data,
                world=part.offset==0?context.WorldId:null,actor=part.offset==0?context.ActorId:null,
                character=part.offset==0?context.CharacterId:null,level=part.offset==0?context.Level:null,
                fingerprint=part.offset==0?context.GeometryFingerprint:null}));
            Debug.Log($"Подготовка входа: connection={connection.connectionId}, token={context.Token}, world={context.WorldId}, actor={context.ActorId}, level={context.Level}; тело пока отключено.");
        }
        private void Ready(NetworkConnectionToClient connection,EntryPreparedMessage message)
        {
            if(!session.Admission.IsApproved(connection.connectionId) || !pending.TryGetValue(connection.connectionId,out var value))
            {RejectReady(connection,$"Нет ожидающего входа для token={message.token}.");return;}
            var ticket=value.Ticket;
            try {ticket.RequireReady(message.token,message.fingerprint,Time.realtimeSinceStartupAsDouble);}
            catch(InvalidDataException error)
            {
                if(ticket.Expired(Time.realtimeSinceStartupAsDouble)) RejectEntry(connection,error);
                else RejectReady(connection,error.Message);
                return;
            }
            try
            {
                var world=session.ServerWorld??throw new InvalidDataException("Мир подготовки входа больше не доступен.");
                var saved=world.actors.Find(actor=>actor.character==value.Join.character);
                if(saved?.connected==true) {pending.Remove(connection.connectionId);session.Admission.RejectDuplicateCharacter(connection,value.Join.character);return;}
                string actorId=saved?.id??"player:"+value.Join.character;
                string level=saved?.level??"hub";
                if(!ticket.Context.Matches(world,actorId,level))
                {
                    // Reuse the pending reservation and replace only its view/token.
                    // Damage received by the offline body is not a geometry mismatch.
                    Send(connection,value);return;
                }
                if(session.Pause.IsServerPaused(level))
                    throw new InvalidDataException("Вход в приостановленную локацию запрещён. Дождитесь возобновления: "+level);
                ticket.Consume(message.token,message.fingerprint,Time.realtimeSinceStartupAsDouble);
                pending.Remove(connection.connectionId);
                session.AdmitPreparedEntry(connection,value.Join,ticket.Context);
            }
            catch(Exception error) {RejectEntry(connection,error);}
        }
        private static void RejectReady(NetworkConnectionToClient connection,string reason)
            =>Debug.LogWarning($"Готовность входа отклонена: connection={connection.connectionId}; {reason}");
        private void RejectEntry(NetworkConnectionToClient connection,Exception error)
        {
            pending.Remove(connection.connectionId);
            Debug.LogWarning($"Подготовка входа отменена: connection={connection.connectionId}; {error}");
            session.Admission.Reject(connection,"Не удалось подготовить вход: "+error.Message);
        }
        private void Receive(EntryPreparationPartMessage message)
        {
            try
            {
                if(session.World!=null) throw new InvalidDataException("Контекст подготовки получен после входа в мир.");
                if(incoming==null)
                {
                    if(message.offset!=0 || !Guid.TryParseExact(message.token,"N",out _) ||
                        !Guid.TryParseExact(message.world,"N",out _) || !IsHash(message.snapshot) || !IsHash(message.fingerprint))
                        throw new InvalidDataException("Подготовка входа началась с неполного/неизвестного пакета.");
                    header=message;header.data=null;incoming=new SnapshotTransfer(message.snapshot);
                    client.BeginReceiving();
                }
                if(message.token!=header.token || message.snapshot!=header.snapshot)
                    throw new InvalidDataException("Контекст подготовки заменён до завершения передачи.");
                var snapshot=incoming.Append(message.total,message.offset,message.data);
                if(snapshot==null) return;
                var context=new EntryPreparationContext(header.token,snapshot.CreateLiveWorld(),header.actor,header.character,header.level);
                if(context.WorldId!=header.world || context.CharacterId!=session.CharacterId || context.GeometryFingerprint!=header.fingerprint)
                    throw new InvalidDataException("Переданные данные локации не совпадают с контекстом входа.");
                incoming=null;header=default;client.Begin(context);
                if(Requested==null) throw new InvalidOperationException("Нет обработчика подготовки локации перед входом.");
                Requested.Invoke(context);
            }
            catch(Exception error) {session.ReportGameplayPreparationFailure(error);}
        }
        public void MarkPrepared(string token)
        {
            if(!ClientPending || incoming!=null || Current==null || token!=Current.Token || session.World!=null || !NetworkClient.isConnected)
                throw new InvalidOperationException($"Локация не соответствует ожидающему входу: token={token}, expected={Current?.Token}.");
            if(!client.MarkPrepared(token)) return;
            NetworkClient.Send(new EntryPreparedMessage {token=token,fingerprint=Current.GeometryFingerprint});
        }
        internal void Take(WorldState welcomed,string actorId)
        {
            if(incoming!=null)
                throw new InvalidDataException("Сервер подключил персонажа без завершённой подготовки локации.");
            var admitted=client.Take(welcomed,actorId);
            // Success must not emit Cancelled: the same warm room is about to be adopted.
            ClientAdmitted?.Invoke(admitted,welcomed);
        }
        internal void CompleteAdmission()=>client.CompleteAdmission();
        public void Disconnected(int connection)=>pending.Remove(connection);
        public void Expire(double now)
        {
            foreach(var pair in pending.Where(pair=>pair.Value.Ticket.Expired(now)).ToArray())
            {
                pending.Remove(pair.Key);
                if(NetworkServer.connections.TryGetValue(pair.Key,out var connection))
                    session.Admission.Reject(connection,"Истекло время подготовки локации перед входом. Попробуйте подключиться снова.");
            }
        }
        public void ResetServer()=>pending.Clear();
        private static bool IsHash(string value)=>value!=null && value.Length==64 && value.All(Uri.IsHexDigit);
        public void ResetClient()
        {
            bool cancel=client.Reset() || incoming!=null;
            incoming=null;header=default;
            if(cancel) Cancelled?.Invoke();
        }
    }
}
