using System;
using System.Collections.Generic;
using Mirror;
using Remains.Domain;
using UnityEngine;

namespace Remains.Networking
{
    // Retain an attacker's contact decision until an owner's published state includes its receipt.
    public sealed class NpcHitDelivery : MonoBehaviour
    {
        private sealed class Pending {public NpcHitMessage message;public string attack,level;}
        private readonly Dictionary<string,Pending> pending=new(StringComparer.Ordinal);
        private readonly List<string> completed=new();
        private CoopSession session;
        private Func<string,NetworkConnectionToClient> resolve;
        public void Initialize(CoopSession value,Func<string,NetworkConnectionToClient> recipient)
        {session=value;resolve=recipient;InvokeRepeating(nameof(Flush),.15f,.15f);}
        public void Reset() {pending.Clear();completed.Clear();}
        public void Accept(NpcHitMessage message,AttackEvent attack)
        {
            var target=session.ServerWorld.Find(message.target);
            if(target.resolvedAttacks.Contains(attack.id))
            {
                message.generation=target.generation;resolve(target.owner)?.Send(message);return;
            }
            string key=message.target+":"+attack.id;
            if(!pending.ContainsKey(key)) pending.Add(key,new Pending {message=message,attack=attack.id,level=attack.level});
            Flush();
        }
        private void Flush()
        {
            if(!NetworkServer.active || session.ServerWorld==null) return;
            completed.Clear();
            foreach(var pair in pending)
            {
                var item=pair.Value;var target=session.ServerWorld.Find(item.message.target);
                if(target==null || target.player || target.level!=item.level || target.health<=0 || target.resolvedAttacks.Contains(item.attack))
                {completed.Add(pair.Key);continue;}
                if(session.Pause.IsPaused(target.level)) continue;
                var recipient=resolve(target.owner);if(recipient==null) continue;
                var message=item.message;message.generation=target.generation;recipient.Send(message);
            }
            foreach(string key in completed) pending.Remove(key);
        }
        private void OnDestroy() {CancelInvoke();pending.Clear();}
    }
}
