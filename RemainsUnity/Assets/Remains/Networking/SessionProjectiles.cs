using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Remains.Domain;
using UnityEngine;

namespace Remains.Networking
{
    public struct ProjectileLaunchMessage : NetworkMessage {public string json;}
    public struct ProjectileRejectedMessage : NetworkMessage {public string id,reason;}
    public struct ProjectileProgressMessage : NetworkMessage
    {public string id;public int generation;public uint sequence;public float traveled;public bool finished;}
    public sealed class SessionProjectiles
    {
        private readonly CoopSession session;
        private readonly HashSet<string> pending=new(),localEffects=new();
        public SessionProjectiles(CoopSession session) {this.session=session;}
        public void RegisterServer()=>NetworkServer.RegisterHandler<ProjectileProgressMessage>(Progress);
        public event Action Launched;
        public event Action<string> Confirmed;
        public event Action<string> Rejected;
        public bool IsPending(string id)=>pending.Contains(id);
        public bool AcceptEcho(string id)=>!localEffects.Remove(id);
        public void ResetClient() {pending.Clear();localEffects.Clear();}
        public void BeginLocal(AttackEvent attack)
        {
            var state=ProjectileLaunch.Create(attack,Remains.Content.WeaponCatalog.Get(attack.weapon));
            if(!pending.Add(attack.id)) throw new InvalidOperationException("Повтор локального запуска: "+attack.id);
            localEffects.Add(attack.id);session.World.projectiles.Add(state);Launched?.Invoke();
        }
        public void RegisterClient()
        {
            NetworkClient.RegisterHandler<ProjectileLaunchMessage>(message=>
                ReceiveLaunch(Remains.Serialization.StateJson.FromJson<ProjectileState>(message.json)));
            NetworkClient.RegisterHandler<ProjectileRejectedMessage>(ReceiveRejection);
            NetworkClient.RegisterHandler<ProjectileProgressMessage>(message=>
        {
            var flight=session.World?.projectiles.Find(p=>p.launch.id==message.id);
            if(flight!=null) ProjectileLifecycle.AcceptProgress(flight,flight.owner,message.generation,message.sequence,message.traveled,message.finished);
        });
        }
        private void ReceiveLaunch(ProjectileState state)
        {
            if(session.World==null) return;
            state.Path();
            bool wasPending=pending.Remove(state.launch.id);
            if(wasPending) Confirmed?.Invoke(state.launch.id);
            var existing=session.World.projectiles.Find(p=>p.launch.id==state.launch.id);
            if(existing!=null)
            {
                if(!wasPending) return;
                // A recovery snapshot may have arrived with newer confirmed progress.
                if(existing.generation>state.generation || existing.generation==state.generation && existing.sequence>state.sequence)
                {Launched?.Invoke();return;}
                session.World.projectiles.Remove(existing);
            }
            session.World.projectiles.Add(state);Launched?.Invoke();
        }
        private void ReceiveRejection(ProjectileRejectedMessage message)
        {
            pending.Remove(message.id);localEffects.Remove(message.id);
            session.World?.projectiles.RemoveAll(p=>p.launch.id==message.id);
            Rejected?.Invoke(message.id);session.ShowNotice(message.reason);
        }
        public bool Launch(AttackEvent attack)
        {
            var world=session.ServerWorld;
            string key="projectile:"+attack.id;
            if(world.completedTransactions.Contains(key) || world.projectiles.Any(p=>p.launch.id==attack.id)) return false;
            var definition=Remains.Content.WeaponCatalog.Get(attack.weapon);
            var state=ProjectileLaunch.Create(attack,definition);
            world.projectiles.Add(state);world.completedTransactions.Add(key);
            // Gameplay deltas are live state. Existing 3-second recovery checkpoints persist
            // them together with actor damage; a shot does not write/send the whole save.
            world.revision++;
            NetworkServer.SendToAll(new ProjectileLaunchMessage {json=Remains.Serialization.StateJson.ToJson(state)});
            return true;
        }
        public bool IsDecisionOwner(string owner,AttackEvent attack)
        {
            var flight=session.ServerWorld?.projectiles.Find(p=>p.launch.id==attack.id);
            return flight!=null && flight.owner==owner && flight.launch.attacker==attack.attacker &&
                flight.launch.weapon==attack.weapon && flight.launch.level==attack.level && flight.launch.sourcePlayer==attack.sourcePlayer &&
                flight.launch.friendlyFireDecision==attack.friendlyFireDecision;
        }
        private void Progress(NetworkConnectionToClient connection,ProjectileProgressMessage message)
        {
            var actor=session.ConnectedActor(connection);
            var flight=session.ServerWorld?.projectiles.Find(p=>p.launch.id==message.id);
            if(actor==null || flight==null || session.Pause.IsServerPaused(flight.launch.level) ||
                !ProjectileLifecycle.AcceptProgress(flight,actor.owner,message.generation,message.sequence,message.traveled,message.finished)) return;
            NetworkServer.SendToAll(message);
            if(message.finished)
            {
                // Completed receipts remain long enough for in-flight reliable hit messages.
                var old=session.ServerWorld.projectiles.Where(p=>p.finished).Take(Math.Max(0,session.ServerWorld.projectiles.Count(p=>p.finished)-256)).ToArray();
                foreach(var item in old) session.ServerWorld.projectiles.Remove(item);
                session.ServerWorld.revision++;
            }
        }
        public void Publish(ProjectileState state,float traveled,bool finished)
        {
            state.sequence++;
            NetworkClient.Send(new ProjectileProgressMessage {id=state.launch.id,generation=state.generation,sequence=state.sequence,traveled=traveled,finished=finished});
        }
    }
}
