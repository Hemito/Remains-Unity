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
    public struct LampBreakRequest : NetworkMessage {public string json;}
    public struct LampBreakRejected : NetworkMessage {public string lamp,reason;}
    public struct LampBrokenMessage : NetworkMessage {public string world,json;}
    public sealed class SessionLamps
    {
        private readonly CoopSession session;
        private readonly DoorDamageAttackLedger ledger=new();
        private readonly HashSet<string> reported=new();
        private string clientWorld;
        private readonly HashSet<string> effects=new();
        public event Action<LampState> Broken;
        public SessionLamps(CoopSession session)=>this.session=session;
        public void RegisterServer(){ResetServer();NetworkServer.RegisterHandler<LampBreakRequest>(Remains.Networking.MessageTiming.Server<LampBreakRequest>(Receive));}
        public void ResetServer()=>ledger.Clear();
        public void ResetClient(){reported.Clear();effects.Clear();clientWorld=null;}
        public void RegisterClient()
        {
            ResetClient();
            NetworkClient.RegisterHandler<LampBreakRejected>(Remains.Networking.MessageTiming.Client<LampBreakRejected>(reply=>{reported.Remove(reply.lamp);Debug.LogWarning($"Контакт лампы отклонён: lamp={reply.lamp}, reason={reply.reason}.");}));
            NetworkClient.RegisterHandler<LampBrokenMessage>(Remains.Networking.MessageTiming.Client<LampBrokenMessage>(message=>
            {
                if(session.World?.worldId!=message.world)return;
                var lamp=StateJson.FromJson<LampState>(message.json);lamp.Validate();if(effects.Add(lamp.id+":"+lamp.revision))Broken?.Invoke(lamp);
            }));
        }
        public bool Install(string level,IReadOnlyList<LampState> lamps)
        {
            if(session.ServerWorld==null)throw new InvalidOperationException("Лампы устанавливаются только в мире координатора.");
            if(!LampOperations.Install(session.ServerWorld,level,lamps))return false;
            session.CommitWorld();return true;
        }
        internal string RecordAccepted(AttackEvent attack,ActorState source)
        {
            if(NpcNaturalAttack.IsNatural(attack)||attack.weapon=="grenade"||WeaponCatalog.Get(attack.weapon).projectileSpeed>0)return null;
            return ledger.Record(session.ServerWorld.worldId,source,attack);
        }
        public void ReportContacts(AttackEvent attack,float limit,int projectileGeneration=0,Func<float,bool> reachable=null)
        {
            if(attack==null||session.World==null||!NetworkClient.isConnected)return;
            var participant=session.LocalActor;var source=session.World.Find(attack.attacker);
            if(participant==null||participant.level!=attack.level||session.Pause.IsPaused(attack.level))return;
            var weapon=WeaponCatalog.Get(attack.weapon);
            if(weapon.projectileSpeed>0)
            {
                var flight=session.World.projectiles.Find(value=>value.launch?.id==attack.id);
                if(flight==null||flight.owner!=session.LocalOwner||flight.generation!=projectileGeneration)return;
            }
            else if(source==null||source.owner!=session.LocalOwner||attack.sourceOwner!=session.LocalOwner)return;
            if(clientWorld!=session.World.worldId){ResetClient();clientWorld=session.World.worldId;}
            foreach(var lamp in session.World.lamps)
            {
                if(lamp.broken||lamp.level!=attack.level)continue;
                else
                {
                    var entry=SurfaceContact.Entry(lamp.Bounds,attack.Origin,attack.End,weapon.projectileSpeed>0?0:attack.radius);
                    if(!entry.HasValue||entry.Value>limit||reachable!=null&&!reachable(entry.Value))continue;
                    if(!reported.Add(lamp.id))continue;
                    NetworkClient.Send(new LampBreakRequest{json=StateJson.ToJson(new LampBreakRequestData{world=session.World.worldId,lamp=lamp.id,
                        decisionActor=participant.id,decisionGeneration=participant.generation,decisionLifecycle=participant.lifecycleSequence,
                        sourceGeneration=source?.generation??0,sourceLifecycle=source?.lifecycleSequence??0,projectileGeneration=projectileGeneration,
                        attack=StateJson.Copy(attack)})});
                }
            }
        }

        private void Receive(NetworkConnectionToClient connection,LampBreakRequest message)
        {
            LampBreakRequestData request=null;
            try
            {
                request=StateJson.FromJson<LampBreakRequestData>(message.json);
                string reason=LampBreakRules.Validate(session.ServerWorld,session.ConnectedActor(connection),request,ledger,out var lamp);
                if(reason!=null)throw new InvalidOperationException(reason);
                if(lamp.broken)
                {
                    session.CommitWorldAtFrameEnd();
                    if(lamp.breakingAttack==request.attack.id) NetworkServer.SendToAll(new LampBrokenMessage{world=session.ServerWorld.worldId,json=StateJson.ToJson(lamp)});
                    return;
                }
                if(session.Pause.IsServerPaused(lamp.level))throw new InvalidOperationException("Локация лампы на паузе.");
                if(!LampOperations.Break(session.ServerWorld,lamp,request.attack.id))return;
                // The broken message below only triggers effects; the world revision goes out once per frame.
                session.CommitWorldAtFrameEnd();
                NetworkServer.SendToAll(new LampBrokenMessage{world=session.ServerWorld.worldId,json=StateJson.ToJson(lamp)});
            }
            catch(Exception error) when(error is ArgumentException||error is InvalidOperationException||error is IOException)
            {connection.Send(new LampBreakRejected{lamp=request?.lamp,reason=error.Message});Debug.LogWarning($"Разрушение лампы отклонено: lamp={request?.lamp}, attack={request?.attack?.id}, connection={connection.connectionId}, reason={error.Message}.");}
        }
    }
}
