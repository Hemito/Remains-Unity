using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;

namespace Remains.Presentation
{
    // A snapshot is a baseline, never a voice trigger. Owner generation is not a new NPC life.
    public sealed class ActorDeathReceipts
    {
        private string world;
        private readonly Dictionary<(string actor,int life),int> baseline=new();
        private readonly HashSet<(string actor,int life)> deaths=new();
        public void Clear() {world=null;baseline.Clear();deaths.Clear();}
        public void Bind(WorldState state)
        {
            if(state==null) {Clear();return;}
            if(world==state.worldId) return;
            if(string.IsNullOrWhiteSpace(state.worldId)) throw new InvalidOperationException("Голоса смерти NPC: у мира отсутствует устойчивый WorldId.");
            world=state.worldId;baseline.Clear();deaths.Clear();
            // Seed every existing sequence, including previously revived mod NPCs. A late
            // historical receipt must not sound after a new client binds the selected copy.
            foreach(var actor in state.actors)
                if(actor!=null && !actor.player)
                {
                    var key=(actor.id,actor.lifecycleSequence);baseline[key]=actor.damageSequence;
                    if(actor.down && actor.health<=0) deaths.Add(key);
                }
        }
        public bool TryObserve(WorldState state,DamageResult result)
        {
            Bind(state);
            if(state==null || !result.confirmedNpcDeath || !DamageFeedback.Valid(result) || result.world!=world) return false;
            var actor=state.Find(result.actor);
            // Relay has already checked the owner/generation at acceptance. A later
            // ownership transfer may reach this peer before this cosmetic receipt.
            if(actor==null || actor.player || actor.actorType!=result.actorType || actor.level!=result.level ||
                result.generation>actor.generation || actor.lifecycleSequence!=result.lifecycleSequence) return false;
            var key=(result.actor,result.lifecycleSequence);
            if(deaths.Contains(key) || baseline.TryGetValue(key,out int previous) && result.damageSequence<=previous) return false;
            deaths.Add(key);return true;
        }
    }

    // Cosmetic owner-confirmed outcome only. Health, inventory and persistence remain untouched.
    public sealed class ActorDeathAudio : MonoBehaviour,IDisposable
    {
        private CoopSession session;
        private DamageFeedback feedback;
        private GameplayAudio playback;
        private ActorDeathAudioCatalog catalog;
        private readonly ActorDeathReceipts receipts=new();
        private readonly HashSet<string> missing=new(StringComparer.Ordinal);
        private readonly System.Random random=new();
        public int Accepted {get;private set;}
        public int Played {get;private set;}
        public event Action<DamageResult,string,AudioClip> DeathPlayed;
        public void Initialize(CoopSession session,DamageFeedback feedback,GameplayAudio playback,ActorDeathAudioCatalog catalog=null)
        {
            if(this.session!=null) throw new InvalidOperationException("Голоса смерти NPC уже привязаны к сессии.");
            if(session==null || feedback==null || playback==null) throw new ArgumentNullException("Голоса смерти NPC: отсутствуют сессия/подтверждения/аудиосервис.");
            var prepared=catalog??ActorDeathAudioCatalog.Load();
            receipts.Bind(session.World);
            Accepted=Played=0;
            this.session=session;this.feedback=feedback;this.playback=playback;this.catalog=prepared;
            feedback.Received+=Receive;session.Changed+=RefreshContext;session.SessionStopped+=Stopped;
        }
        private void RefreshContext()=>receipts.Bind(session.World);
        private void Stopped() {receipts.Clear();missing.Clear();}
        private void Receive(DamageResult result)
        {
            if(!receipts.TryObserve(session.World,result)) return;
            Accepted++;
            if(!catalog.TrySelect(result.actorType,random.Next(),out var sound))
            {
                if(missing.Add(result.actorType)) Debug.LogWarning($"Не объявлен голос смерти NPC: type={result.actorType}, actor={result.actor}, world={result.world}, level={result.level}, attack={result.attack}; {ActorDeathAudioCatalog.Resource}. Реплика пропущена без подмены.",this);
                return;
            }
            // Remember inaudible/paused outcomes too: a duplicate cannot become audible later.
            if(playback.PlayContact(sound.Clip,result.level,new Vector2(result.x,result.y),sound.Gain))
            {Played++;DeathPlayed?.Invoke(result,sound.Resource,sound.Clip);}
        }
        public void Dispose()
        {
            if(feedback!=null) feedback.Received-=Receive;
            if(session!=null) {session.Changed-=RefreshContext;session.SessionStopped-=Stopped;}
            feedback=null;session=null;playback=null;catalog=null;receipts.Clear();missing.Clear();
        }
        private void OnDestroy()=>Dispose();
    }
}
