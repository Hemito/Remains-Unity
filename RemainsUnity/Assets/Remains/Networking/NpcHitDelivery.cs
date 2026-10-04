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
        internal sealed class Pending
        {
            public NpcHitMessage message;
            public string attack,level;
            private string sentOwner;
            private int sentGeneration;
            private double retryAt;
            private bool sent;
            public bool IsDue(string owner,int generation,double now)=>
                !sent || owner!=sentOwner || generation!=sentGeneration || now>=retryAt;
            public void MarkSent(string owner,int generation,double now)
            {sent=true;sentOwner=owner;sentGeneration=generation;retryAt=now+.15;}
        }
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
        public void Flush()
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
                double now=Time.realtimeSinceStartupAsDouble;
                if(!item.IsDue(target.owner,target.generation,now)) continue;
                var message=item.message;message.generation=target.generation;
                session.TraceNpcHit("npc-hit-forwarded",message,true);recipient.Send(message);
                item.MarkSent(target.owner,target.generation,now);
            }
            foreach(string key in completed) pending.Remove(key);
        }
        private void OnDestroy() {CancelInvoke();pending.Clear();}
    }
}
