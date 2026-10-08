using System;
using System.Collections.Generic;
using Mirror;
using Remains.Domain;
using UnityEngine;

namespace Remains.Networking
{
    // Cosmetic confirmation of an already completed shared operation.
    public struct HealingSoundCue : NetworkMessage
    {
        public string world,transaction,actor,definition,level;
        public int generation,lifecycle;
        public float x,y;
        public Vector2 Position=>new(x,y);
        public string Key=>world+"\n"+transaction;
    }

    public sealed class HealingAudioReceipts
    {
        private string world;
        private readonly HashSet<string> seen=new(StringComparer.Ordinal);
        public void Clear() {world=null;seen.Clear();}
        public bool Accept(WorldState state,HealingSoundCue cue)
        {
            if(!SessionHealingAudio.Matches(state,cue)) return false;
            if(world!=state.worldId) {world=state.worldId;seen.Clear();}
            return seen.Add(cue.Key);
        }
    }

    public sealed class SessionHealingAudio
    {
        private readonly CoopSession session;
        private readonly HashSet<string> published=new(StringComparer.Ordinal);
        private readonly HealingAudioReceipts receipts=new();
        public event Action<HealingSoundCue> Received;
        public event Action<HealingSoundCue> Published;
        public SessionHealingAudio(CoopSession session)=>this.session=session??throw new ArgumentNullException(nameof(session));
        public void RegisterServer()=>published.Clear();
        public void ResetServer()=>published.Clear();
        public void RegisterClient()
        {
            receipts.Clear();
            NetworkClient.RegisterHandler<HealingSoundCue>(cue=>
            {
                // An expedited cue may precede its world update. Do not consume
                // its receipt when context is absent: the reliable copy follows
                // BroadcastWorld and can be accepted in the actual life scope.
                if(receipts.Accept(session.World,cue)) Received?.Invoke(cue);
            });
        }
        public void Publish(ActorState healer,string transaction,string definition)
        {
            var cue=Capture(session.ServerWorld,healer,transaction,definition);
            if(!published.Add(cue.Key)) return;
            Published?.Invoke(cue);
            NetworkServer.SendToAll(cue,Channels.Unreliable);
            NetworkServer.SendToAll(cue);
        }
        public static HealingSoundCue Capture(WorldState world,ActorState healer,string transaction,string definition)
        {
            if(world==null || healer==null || !world.actors.Contains(healer) || !healer.player || !healer.connected ||
                string.IsNullOrWhiteSpace(transaction) || !world.completedTransactions.Contains(transaction))
                throw new InvalidOperationException($"Нет подтверждённого лечения для звука: world={world?.worldId}, actor={healer?.id}, transaction={transaction}.");
            var cue=new HealingSoundCue {world=world.worldId,transaction=transaction,actor=healer.id,
                definition=definition,level=healer.level,generation=healer.generation,lifecycle=healer.lifecycleSequence,
                x=healer.BodyPosition.x,y=healer.BodyPosition.y};
            if(!Valid(cue)) throw new InvalidOperationException($"Некорректное подтверждение звука лечения: actor={healer.id}, transaction={transaction}, definition={definition}.");
            return cue;
        }
        public static bool Valid(HealingSoundCue cue)=>!string.IsNullOrWhiteSpace(cue.world) &&
            !string.IsNullOrWhiteSpace(cue.transaction) && !string.IsNullOrWhiteSpace(cue.actor) &&
            !string.IsNullOrWhiteSpace(cue.definition) && !string.IsNullOrWhiteSpace(cue.level) &&
            cue.generation>=0 && cue.lifecycle>=0 && float.IsFinite(cue.x) && float.IsFinite(cue.y);
        public static bool Matches(WorldState world,HealingSoundCue cue)
        {
            if(!Valid(cue) || world?.worldId!=cue.world) return false;
            var actor=world.Find(cue.actor);
            return actor!=null && actor.player && actor.connected && actor.generation==cue.generation &&
                actor.lifecycleSequence==cue.lifecycle && actor.level==cue.level;
        }
    }
}
