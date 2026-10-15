using System;
using System.Collections.Generic;
using System.IO;
using Mirror;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Networking
{
    public struct DoorDamageRequest : NetworkMessage {public string json;}
    public struct DoorDamageReply : NetworkMessage
    {
        public string door,attack,reason;
        public bool accepted,changed,destroyed;
        public long revision;
        public float health;
    }
    public sealed class SessionDoorDamage
    {
        private readonly CoopSession session;
        private readonly DoorDamageAttackLedger ledger=new();
        private readonly HashSet<string> reported=new();
        private readonly Queue<string> order=new();
        private string clientWorld;
        public event Action<DoorDamageReply> Finished;
        public event Action<DoorDamageRequestData> Reported;
        public SessionDoorDamage(CoopSession session)=>this.session=session;
        public void RegisterServer() {ResetServer();NetworkServer.RegisterHandler<DoorDamageRequest>(Remains.Networking.MessageTiming.Server<DoorDamageRequest>(Receive));}
        public void RegisterClient()
        {
            ResetClient();
            NetworkClient.RegisterHandler<DoorDamageReply>(Remains.Networking.MessageTiming.Client<DoorDamageReply>(reply=>
            {
                if(!reply.accepted)
                {reported.Remove(Key(reply.door,reply.attack));session.ShowNotice(reply.reason);}
                Finished?.Invoke(reply);
            }));
        }
        public void ResetClient() {reported.Clear();order.Clear();clientWorld=null;}
        public void ResetServer()=>ledger.Clear();
        internal string RecordAccepted(AttackEvent attack,ActorState source)
        {
            if(NpcNaturalAttack.IsNatural(attack) || WeaponCatalog.Get(attack.weapon).projectileSpeed>0 || Remains.Content.WeaponCatalog.IsExplosive(attack.weapon)) return null;
            return ledger.Record(session.ServerWorld.worldId,source,attack);
        }
        public bool Report(AttackEvent attack,string sourceDoorId,Vector2 contact,int projectileGeneration=0)
        {
            if(string.IsNullOrEmpty(sourceDoorId) || attack==null || !NetworkClient.isConnected || session.World==null) return false;
            var door=session.World.doors.Find(value=>value.id==sourceDoorId);
            // Legacy and ordinary terrain intentionally have no damage contract.
            if(door?.integrity==null) return false;
            var participant=session.LocalActor;
            if(participant==null || session.Pause.IsPaused(attack.level)) return false;
            var source=session.World.Find(attack.attacker);
            var weapon=WeaponCatalog.Get(attack.weapon);
            if(weapon.projectileSpeed>0)
            {
                var flight=session.World.projectiles.Find(value=>value.launch?.id==attack.id);
                if(flight==null || flight.owner!=session.LocalOwner || flight.generation!=projectileGeneration) return false;
            }
            else if(source==null || source.owner!=session.LocalOwner || attack.sourceOwner!=session.LocalOwner) return false;
            if(clientWorld!=session.World.worldId) {ResetClient();clientWorld=session.World.worldId;}
            string key=Key(door.id,attack.id);
            if(!reported.Add(key)) return false;
            if(order.Count>=1024) reported.Remove(order.Dequeue());
            order.Enqueue(key);
            var request=new DoorDamageRequestData {world=session.World.worldId,door=door.id,decisionActor=participant.id,
                decisionGeneration=participant.generation,decisionLifecycle=participant.lifecycleSequence,
                sourceGeneration=source?.generation??0,sourceLifecycle=source?.lifecycleSequence??0,
                projectileGeneration=projectileGeneration,attack=StateJson.Copy(attack),contact=contact};
            NetworkClient.Send(new DoorDamageRequest {json=StateJson.ToJson(request)});
            Reported?.Invoke(request);return true;
        }
        private static string Key(string door,string attack)=>(door?.Length??0)+":"+door+attack;
        private void Receive(NetworkConnectionToClient connection,DoorDamageRequest message)
        {
            DoorDamageRequestData request=null;string reason=null;DoorChange outcome=DoorChange.Rejected;
            try
            {
                request=StateJson.FromJson<DoorDamageRequestData>(message.json);
                outcome=DoorDamageRequestRules.Apply(session.ServerWorld,session.ConnectedActor(connection),request,
                    request?.attack!=null && session.Pause.IsServerPaused(request.attack.level),ledger,session.Rules.DoorDamage,
                    WeaponCatalog.Get,out reason);
                // A duplicate also re-establishes a recoverable save if an earlier write failed.
                // No accepted reply precedes the canonical world write and recovery publication.
                if(outcome!=DoorChange.Rejected) session.CommitWorld();
            }
            catch(Exception failure) when(failure is ArgumentException || failure is InvalidOperationException || failure is IOException)
            {outcome=DoorChange.Rejected;reason="Не удалось подтвердить урон закреплённому объекту: "+failure.Message;}
            var door=session.ServerWorld?.doors.Find(value=>value.id==request?.door);
            if(outcome==DoorChange.Rejected)
                Debug.LogWarning($"Контакт закреплённого объекта отклонён: world={request?.world}, door={request?.door}, attack={request?.attack?.id}, connection={connection.connectionId}, reason={reason}.");
            connection.Send(new DoorDamageReply {door=request?.door,attack=request?.attack?.id,accepted=outcome!=DoorChange.Rejected,
                changed=outcome==DoorChange.Changed,reason=reason,revision=door?.revision??0,
                health=door?.integrity?.health??0,destroyed=door?.Destroyed??false});
        }
    }
}
