using System;
using System.Linq;
using Mirror;
using Remains.Domain;
using UnityEngine;

namespace Remains.Networking
{
    public struct ProjectileLaunchMessage : NetworkMessage {public string json;}
    public struct ProjectileProgressMessage : NetworkMessage
    {public string id;public int generation;public uint sequence;public float traveled;public bool finished;}
    public sealed class SessionProjectiles
    {
        private readonly CoopSession session;
        public SessionProjectiles(CoopSession session) {this.session=session;}
        public void RegisterServer()=>NetworkServer.RegisterHandler<ProjectileProgressMessage>(Progress);
        public event Action Launched;
        public void RegisterClient()
        {
            NetworkClient.RegisterHandler<ProjectileLaunchMessage>(message=>
            {
                if(session.World==null) return;
                var state=Remains.Serialization.StateJson.FromJson<ProjectileState>(message.json);
                if(session.World.projectiles.Any(p=>p.launch.id==state.launch.id)) return;
                state.Path();session.World.projectiles.Add(state);Launched?.Invoke();
            });
            NetworkClient.RegisterHandler<ProjectileProgressMessage>(message=>
        {
            var flight=session.World?.projectiles.Find(p=>p.launch.id==message.id);
            if(flight!=null) ProjectileLifecycle.AcceptProgress(flight,flight.owner,message.generation,message.sequence,message.traveled,message.finished);
        });
        }
        public bool Launch(AttackEvent attack)
        {
            var world=session.ServerWorld;
            string key="projectile:"+attack.id;
            if(world.completedTransactions.Contains(key) || world.projectiles.Any(p=>p.launch.id==attack.id)) return false;
            var speeds=Remains.Content.WeaponCatalog.Get(attack.weapon).ProjectileSpeedRange;
            float speed=UnityEngine.Random.Range(speeds.x,speeds.y);
            var state=new ProjectileState {launch=Remains.Serialization.StateJson.Copy(attack),owner=attack.sourceOwner,generation=1,flightSpeed=speed};
            try {state.Path();} catch(Exception error) when(error is ArgumentException || error is InvalidOperationException) {Debug.LogWarning($"Некорректный запуск снаряда {attack.id}: {error.Message}");return false;}
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
