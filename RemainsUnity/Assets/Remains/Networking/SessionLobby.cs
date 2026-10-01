using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mirror;
using Remains.Persistence;
using UnityEngine;

namespace Remains.Networking
{
    [Serializable]
    public sealed class LobbyCopy
    {
        public string key,holder;
        public int connection;
        public WorldSummary summary;
    }

    public sealed class SessionLobby
    {
        private readonly CoopSession session;
        private readonly Dictionary<int,JoinMessage> pending=new();
        private readonly Dictionary<int,Dictionary<string,WorldSummary>> offers=new();
        private readonly Dictionary<string,RecoverySnapshot> clientCopies=new();
        private string initialHash,selected="",request,error="";
        private int source;
        private SnapshotTransfer transfer;
        private float deadline;
        private bool published;
        private byte[] outgoing;
        private string outgoingRequest;
        public int SentBytes {get;private set;}
        public bool Waiting {get;private set;}
        public bool Busy=>transfer!=null;
        public int ReceivedBytes=>transfer?.ReceivedBytes ?? 0;
        public bool ClientWaiting {get;private set;}
        public bool ClientBusy {get;private set;}
        public string ClientSelected {get;private set;}
        public string ClientError {get;private set;}
        public int ClientPlayers {get;private set;}
        public List<LobbyCopy> Copies {get;}=new();
        public event Action Changed;
        public SessionLobby(CoopSession session) {this.session=session;}

        public void RegisterServer(bool wait)
        {
            pending.Clear();offers.Clear();selected="";error="";transfer=null;Waiting=wait;
            initialHash=session.Saves.SelectedSnapshot()?.Id;
            NetworkServer.RegisterHandler<SaveOfferMessage>(ReceiveOffer);
            NetworkServer.RegisterHandler<SaveChunkMessage>(ReceiveChunk);
            NetworkServer.RegisterHandler<SaveTransferErrorMessage>((connection,message)=>
            {if(Busy && connection.connectionId==source && message.request==request) Fail(message.error);});
        }
        public void RegisterClient()
        {
            ResetClient();
            NetworkClient.RegisterHandler<LobbyStateMessage>(message=>
            {
                ClientWaiting=message.waiting;ClientBusy=message.busy;ClientSelected=message.selected;
                ClientError=message.error;ClientPlayers=message.players;Copies.Clear();
            });
            NetworkClient.RegisterHandler<LobbyCopyMessage>(message=>Copies.Add(JsonUtility.FromJson<LobbyCopy>(message.json)));
            NetworkClient.RegisterHandler<LobbyStateEndMessage>(_=>Changed?.Invoke());
            NetworkClient.RegisterHandler<SaveRequestMessage>(SendCopy);
        }
        public void ResetClient()
        {
            outgoing=null;SentBytes=0;published=false;clientCopies.Clear();Copies.Clear();ClientWaiting=false;ClientBusy=false;
            ClientSelected="";ClientError="";ClientPlayers=0;Changed?.Invoke();
        }
        public void PublishOffers()
        {
            if(published) return;
            var copies=session.Saves.Continuations();
            var active=session.Saves.SelectedSnapshot();
            if(active!=null && copies.All(c=>c.Id!=active.Id)) copies.Add(active);
            if(copies.Count>256) throw new InvalidDataException("В профиле больше 256 продолжений: сетевой список требует отдельного индексирования. Исходные файлы сохранены.");
            foreach(var copy in copies)
            {
                clientCopies[copy.Id]=copy;
                NetworkClient.Send(new SaveOfferMessage {json=JsonUtility.ToJson(WorldSummary.From(copy))});
            }
            published=true;
        }
        private void ReceiveOffer(NetworkConnectionToClient connection,SaveOfferMessage message)
        {
            if(!Waiting || !session.Admission.IsApproved(connection.connectionId)) return;
            var summary=JsonUtility.FromJson<WorldSummary>(message.json);
            if(summary==null || !Guid.TryParseExact(summary.worldId,"N",out _) ||
                summary.snapshot==null || summary.snapshot.Length!=64 || summary.snapshot.Any(c=>!Uri.IsHexDigit(c)))
                throw new InvalidDataException($"Некорректное описание сохранения от клиента {connection.connectionId}.");
            if(!offers.TryGetValue(connection.connectionId,out var copies)) offers[connection.connectionId]=copies=new();
            if(copies.Count>=256 && !copies.ContainsKey(summary.snapshot)) throw new InvalidDataException("Превышен размер списка продолжений клиента.");
            copies[summary.snapshot]=summary;
        }
        public bool HoldJoin(NetworkConnectionToClient connection,JoinMessage join)
        {
            if(!Waiting) return false;
            pending[connection.connectionId]=join;
            if(connection==NetworkServer.localConnection && initialHash!=null)
                selected=connection.connectionId+":"+initialHash;
            Broadcast();return true;
        }
        private List<LobbyCopy> ServerCopies() => pending.SelectMany(peer=>
            offers.TryGetValue(peer.Key,out var copies)?copies.Values.Select(summary=>new LobbyCopy {
                key=peer.Key+":"+summary.snapshot,holder=peer.Value.displayName,connection=peer.Key,summary=summary}):Enumerable.Empty<LobbyCopy>()).ToList();

        public void Select(string key)
        {
            if(!NetworkServer.active || !Waiting || Busy) return;
            if(key!="" && ServerCopies().All(copy=>copy.key!=key)) {Fail("Выбранная копия больше недоступна.");return;}
            selected=key;error="";Broadcast();
        }
        public void StartSelected()
        {
            if(!NetworkServer.active || !Waiting || Busy) return;
            error="";
            if(selected=="") {Start(null);return;}
            var copy=ServerCopies().Find(c=>c.key==selected);
            if(copy==null || !NetworkServer.connections.TryGetValue(copy.connection,out var connection))
            {Fail("Игрок с выбранной копией отключился. Выберите доступное продолжение.");return;}
            request=Guid.NewGuid().ToString("N");source=copy.connection;
            transfer=new SnapshotTransfer(copy.summary.snapshot);deadline=Time.unscaledTime+30;
            Broadcast();connection.Send(new SaveRequestMessage {request=request,snapshot=copy.summary.snapshot});
        }
        private void SendCopy(SaveRequestMessage message)
        {
            try
            {
                if(!clientCopies.TryGetValue(message.snapshot,out var copy)) throw new InvalidDataException("Предложенная копия больше недоступна.");
                byte[] bytes=Encoding.UTF8.GetBytes(copy.Json);
                if(bytes.Length>SnapshotTransfer.MaximumBytes) throw new InvalidDataException("Сохранение превышает текущий лимит передачи 32 МиБ.");
                outgoing=bytes;outgoingRequest=message.request;SentBytes=0;
            }
            catch(Exception exception) when(exception is InvalidDataException || exception is IOException)
            {NetworkClient.Send(new SaveTransferErrorMessage {request=message.request,error=exception.Message});}
        }
        private void ReceiveChunk(NetworkConnectionToClient connection,SaveChunkMessage message)
        {
            if(!Waiting || !Busy || message.request!=request || connection.connectionId!=source) return;
            try
            {
                var snapshot=transfer.Append(message.total,message.offset,message.data);
                deadline=Time.unscaledTime+30;
                if(snapshot==null) return;
                var advertised=ServerCopies().Find(c=>c.key==selected)?.summary;
                if(advertised==null || JsonUtility.ToJson(WorldSummary.From(snapshot))!=JsonUtility.ToJson(advertised))
                    throw new InvalidDataException("Описание выбранной копии не совпадает с переданным сохранением.");
                Start(snapshot);
            }
            catch(Exception exception) when(exception is IOException || exception is ArgumentException)
            {Fail("Передача сохранения отменена: "+exception.Message);}
        }
        private void Start(RecoverySnapshot snapshot)
        {
            // World loading happens only after a complete hash-verified transfer.
            var previous=session.Saves.SelectedSnapshot();
            try {session.StartSelectedWorld(snapshot,selected=="");}
            catch(Exception exception)
            {
                session.DiscardUnstartedWorld();
                if(previous!=null) session.Saves.SelectContinuation(previous);
                Fail("Не удалось загрузить выбранный мир: "+exception.Message);
                return;
            }
            transfer=null;Waiting=false;Broadcast();
            var joins=pending.ToArray();pending.Clear();
            foreach(var peer in joins)
                if(NetworkServer.connections.TryGetValue(peer.Key,out var connection)) session.AdmitLobbyPlayer(connection,peer.Value);
        }
        public void Disconnected(int connection)
        {
            pending.Remove(connection);offers.Remove(connection);
            if(!Waiting) return;
            if(Busy && source==connection) {Fail("Передача прервана: владелец сохранения отключился. Исходный мир не заменён.");return;}
            if(selected.StartsWith(connection+":",StringComparison.Ordinal)) error="Владелец выбранной копии отключился. Выберите другое продолжение.";
            Broadcast();
        }
        public void Step()
        {
            if(outgoing!=null && NetworkClient.isConnected)
            {
                var data=new byte[Math.Min(SnapshotTransfer.ChunkSize,outgoing.Length-SentBytes)];
                Buffer.BlockCopy(outgoing,SentBytes,data,0,data.Length);
                NetworkClient.Send(new SaveChunkMessage {request=outgoingRequest,total=outgoing.Length,offset=SentBytes,data=data});
                SentBytes+=data.Length;
                if(SentBytes==outgoing.Length) outgoing=null;
            }
            if(NetworkServer.active && Busy && Time.unscaledTime>deadline) Fail("Истекло время передачи сохранения. Выбор можно повторить.");
        }
        private void Fail(string reason)
        {transfer=null;error=reason;Debug.LogWarning("Лобби сохранений: "+reason);Broadcast();}
        private void Broadcast()
        {
            var copies=ServerCopies();
            foreach(int id in pending.Keys)
            {
                if(!NetworkServer.connections.TryGetValue(id,out var connection)) continue;
                connection.Send(new LobbyStateMessage {waiting=Waiting,busy=Busy,selected=selected,error=error,players=pending.Count});
                foreach(var copy in copies) connection.Send(new LobbyCopyMessage {json=JsonUtility.ToJson(copy)});
                connection.Send(new LobbyStateEndMessage());
            }
        }
    }
}
