using System;
using System.Collections.Generic;
using System.IO;
using Mirror;
using Remains.Domain;
using Remains.Persistence;
using Remains.Serialization;

namespace Remains.Networking
{
    // A receiver without the base of a pieces message asks its sender for the actor's full text.
    public struct ActorResyncRequest : NetworkMessage {public string actor;}

    // Actor updates as pieces (ActorText): each stream (a client to the host, the host to everyone) numbers its
    // messages per actor; after the first, a message carries the template and only the cold texts that changed since
    // the previous one (a telekinesis mana tick no longer sends ~50 KB of inventory and content history).
    public sealed class ActorPiecesSender
    {
        private readonly Dictionary<string,(uint sequence,ActorText.Pieces pieces)> sent=new(StringComparer.Ordinal);
        public void Clear()=>sent.Clear();
        public void Forget(string actorId) {if(actorId!=null) sent.Remove(actorId);}
        public ActorMessage Next(ActorState actor)
        {
            if(string.IsNullOrEmpty(actor?.id)) throw new ArgumentException("Сообщение персонажа без ID.");
            var next=ActorText.Pieces.Of(actor);
            if(!sent.TryGetValue(actor.id,out var previous)) {sent[actor.id]=(1,next);return Full(actor.id,1,next);}
            uint sequence=unchecked(previous.sequence+1);if(sequence==0) sequence=1;
            var cold=new string[next.Cold.Length];
            for(int i=0;i<cold.Length;i++) cold[i]=next.Cold[i]==previous.pieces.Cold[i]?null:next.Cold[i];
            sent[actor.id]=(sequence,next);
            return new ActorMessage {actor=actor.id,baseSequence=previous.sequence,sequence=sequence,template=next.Template,cold=cold};
        }
        // The last state sent for this actor again, whole (for a receiver that lacks its base).
        public bool TryFull(string actorId,out ActorMessage message)
        {
            message=default;
            if(actorId==null || !sent.TryGetValue(actorId,out var state)) return false;
            message=Full(actorId,state.sequence,state.pieces);return true;
        }
        private static ActorMessage Full(string actorId,uint sequence,ActorText.Pieces pieces)
            =>new ActorMessage {actor=actorId,sequence=sequence,json=ActorText.Join(pieces.Template,pieces.Cold)};
    }

    public sealed class ActorPiecesReceiver
    {
        private readonly Dictionary<string,(uint sequence,string text,ActorText.Pieces pieces)> received=new(StringComparer.Ordinal);
        public void Clear()=>received.Clear();
        // The actor of a message, strictly checked (whole, or by its template and changed cold texts); false when a
        // pieces message does not continue this receiver's base: the caller asks the sender for the full text.
        public bool TryRead(ActorMessage message,out ActorState actor)
        {
            actor=null;
            if(message.json!=null)
            {
                actor=StateJson.FromJson<ActorState>(message.json);
                if(message.actor!=null)
                {
                    if(actor.id!=message.actor) throw new InvalidDataException($"Сообщение персонажа {message.actor} содержит другого персонажа {actor.id}.");
                    received[message.actor]=(message.sequence,message.json,null);
                }
                return true;
            }
            if(message.actor==null || message.template==null || message.cold==null || message.cold.Length!=ActorText.Cold.Count)
                throw new InvalidDataException($"Неполное сообщение персонажа по частям: actor={message.actor}.");
            if(!received.TryGetValue(message.actor,out var previous) || previous.sequence!=message.baseSequence) return false;
            var prior=previous.pieces??ActorText.Pieces.OfText(previous.text);
            var merged=new string[ActorText.Cold.Count];
            for(int i=0;i<merged.Length;i++) merged[i]=message.cold[i]??prior.Cold[i];
            string text=ActorText.Join(message.template,merged);
            var sent=message.cold;
            ActorText.Verify(message.template,true,merged,i=>sent[i]!=null);
            actor=StateJson.FromTrustedJson<ActorState>(text);
            if(actor.id!=message.actor) throw new InvalidDataException($"Сообщение персонажа {message.actor} содержит другого персонажа {actor.id}.");
            received[message.actor]=(message.sequence,text,new ActorText.Pieces(message.template,merged));
            return true;
        }
    }
}
