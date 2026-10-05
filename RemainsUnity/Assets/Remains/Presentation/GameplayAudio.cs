using System;
using System.Collections.Generic;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using UnityEngine;

namespace Remains.Presentation
{
    // Local presentation only. Audio does not author attacks, inventories or reloads.
    public sealed class GameplayAudio : MonoBehaviour
    {
        [Serializable] private sealed class SoundEntry
        {public string weapon,clip,kind;public float minGain,maxGain;[NonSerialized] public AudioClip audio;}
        [Serializable] private sealed class SoundCatalog {public SoundEntry[] entries;}
        private sealed class Voice
        {
            public AudioSource source;
            public string level;
            public Vector2 position;
            public float remaining;
            public bool paused;
            public float gain=1;
            public ReloadSoundMessage? reload;
        }
        private CoopSession session;
        private WeaponReloadController reload;
        private readonly Dictionary<string,SoundEntry> sounds=new();
        private readonly List<Voice> voices=new();
        private readonly HashSet<string> seen=new(),missing=new();
        private readonly Queue<string> recent=new();
        // Presentation randomness must not advance a gameplay random sequence.
        private readonly System.Random gainRandom=new();
        private AudioClip reloadClip;
        private ReloadSoundMessage? localReload;
        public int RemoteReloadStarts {get;private set;}
        public int RemoteReloadStops {get;private set;}
        public int LocalShots {get;private set;}
        public int RemoteShots {get;private set;}
        public int ReloadStarts {get;private set;}
        public int MeleeSounds {get;private set;}
        public int ExplosionSounds {get;private set;}
        private AudioMixSettings mix;
        private Camera view;
        public float BaseVolume=>mix.effectsVolume;
        public float Volume {get;set;}
        public void Initialize(CoopSession session,WeaponReloadController reload,Camera camera)
        {
            this.session=session;this.reload=reload;
            mix=AudioMixSettings.Load();view=camera;Volume=BaseVolume;
            var asset=Resources.Load<TextAsset>("Presentation/combat-audio");
            if(asset==null) throw new InvalidOperationException("Отсутствует Presentation/combat-audio.");
            var catalog=JsonUtility.FromJson<SoundCatalog>(asset.text);
            if(catalog?.entries==null) throw new InvalidOperationException("Не заданы записи звуков боя.");
            foreach(var entry in catalog.entries)
            {
                if(string.IsNullOrEmpty(entry.weapon) || string.IsNullOrEmpty(entry.clip) || entry.kind is not ("shot" or "melee" or "explosion") ||
                    !float.IsFinite(entry.minGain) || !float.IsFinite(entry.maxGain) || entry.minGain<=0 || entry.maxGain<entry.minGain || entry.maxGain>1)
                    throw new InvalidOperationException($"Некорректная звуковая привязка: weapon={entry.weapon}, clip={entry.clip}, kind={entry.kind}.");
                entry.audio=Load(entry.clip);sounds.Add(entry.weapon,entry);
            }
            reloadClip=Load("pistol-reload");
            if(camera.GetComponent<AudioListener>()==null) camera.gameObject.AddComponent<AudioListener>();
            session.ReloadAudio.Received+=RemoteReload;
            session.AttackReceived+=Attack;reload.Started+=ReloadStarted;reload.Finished+=ReloadFinished;
        }
        private static AudioClip Load(string key)
        {
            var clip=Resources.Load<AudioClip>("Original/Audio/"+key);
            if(clip==null || clip.samples<=0) throw new InvalidOperationException("Отсутствует или пуст звук Original/Audio/"+key);
            return clip;
        }
        public static float Attenuation(float distance)=>Mathf.Clamp01(1-Mathf.Max(0,distance-2)/23);
        public bool PlayContact(AudioClip clip,string level,Vector2 position,float gain)
        {
            if(session.LocalActor?.level!=level || session.Pause.IsPaused(level) ||
                Attenuation(Vector2.Distance(position,session.LocalActor.Position))<=0) return false;
            return Play(clip,level,position,gain)!=null;
        }
        private Voice Play(AudioClip clip,string level,Vector2 position,float gain=1)
        {
            if(voices.Count>=64) {Debug.LogWarning($"Превышен лимит 64 звуков: clip={clip.name}, level={level}.",this);return null;}
            var source=new GameObject("Звук "+clip.name).AddComponent<AudioSource>();source.transform.SetParent(transform,false);
            source.playOnAwake=false;source.spatialBlend=0;source.clip=clip;
            // Background verification must not make sound on the user's desktop.
            source.mute=Application.isBatchMode;
            source.volume=Mathf.Clamp01(Volume)*gain*Attenuation(Vector2.Distance(position,session.LocalActor.Position));
            source.panStereo=mix.Pan(position.x,view.transform.position.x);
            var voice=new Voice {source=source,level=level,position=position,remaining=clip.length,gain=gain};voices.Add(voice);
            source.Play();
            if(session.Pause.IsPaused(level)) {source.Pause();voice.paused=true;}
            return voice;
        }
        private void Attack(AttackEvent attack)
        {
            if(session.LocalActor?.level!=attack.level) return;
            if(NpcNaturalAttack.IsNatural(attack))
            {
                if(missing.Add("natural:"+attack.naturalAttackType))
                    Debug.LogWarning($"Звук естественной атаки пока не настроен: type={attack.naturalAttackType}, actor={attack.attacker}, attack={attack.id}. Оружейный звук не подставляется; см. CRAWLER_SPEC.md.",this);
                return;
            }
            if(!sounds.TryGetValue(attack.weapon,out var entry))
            {if(missing.Add(attack.weapon)) Debug.LogWarning($"Нет звука атаки: weapon={attack.weapon}, attack={attack.id}.",this);return;}
            if(!seen.Add(attack.id)) return;
            recent.Enqueue(attack.id);if(recent.Count>512) seen.Remove(recent.Dequeue());
            // A one-shot outside hearing range must not consume the voice budget.
            // Remember its ID first so a late duplicate cannot sound after approaching.
            if(Attenuation(Vector2.Distance(attack.Origin,session.LocalActor.Position))<=0) return;
            float gain=Mathf.Lerp(entry.minGain,entry.maxGain,(float)gainRandom.NextDouble());
            if(Play(entry.audio,attack.level,attack.Origin,gain)==null) return;
            if(entry.kind=="melee") MeleeSounds++;
            else if(entry.kind=="explosion") ExplosionSounds++;
            else if(attack.attacker==session.LocalActorId) LocalShots++;else RemoteShots++;
        }
        private void ReloadStarted(ActorState actor,string weapon)
        {
            ReloadFinished();
            var message=new ReloadSoundMessage {actor=actor.id,token=Guid.NewGuid().ToString("N"),weapon=weapon,
                level=actor.level,generation=actor.generation,lifecycle=actor.lifecycleSequence,started=true};
            localReload=message;session.ReloadAudio.Send(message);
            if(PlayReload(message,actor)) ReloadStarts++;
        }
        private bool PlayReload(ReloadSoundMessage message,ActorState actor)
        {
            if(message.weapon is not ("pistol" or "energy"))
            {Debug.LogWarning($"Нет звука перезарядки: actor={message.actor}, weapon={message.weapon}.",this);return false;}
            if(actor==null || session.LocalActor?.level!=message.level || actor.down || !actor.connected ||
                !SessionReloadAudio.Matches(actor,message)) return false;
            var voice=Play(reloadClip,message.level,actor.Position);
            if(voice==null) return false;
            voice.reload=message;return true;
        }
        private void ReloadFinished()
        {
            if(!localReload.HasValue) return;
            var message=localReload.Value;message.started=false;session.ReloadAudio.Send(message);
            StopReload(message.actor,message.token);localReload=null;
        }
        private void RemoteReload(ReloadSoundMessage message)
        {
            if(!message.started)
            {StopReload(message.actor,message.token);RemoteReloadStops++;return;}
            StopReload(message.actor,null);
            if(PlayReload(message,session.World?.Find(message.actor))) RemoteReloadStarts++;
        }
        private void StopReload(string actor,string token)
        {
            for(int i=voices.Count-1;i>=0;i--)
                if(voices[i].reload is ReloadSoundMessage message && message.actor==actor && (token==null || message.token==token))
                    RemoveVoice(i);
        }
        private void RemoveVoice(int index)
        {
            var voice=voices[index];voice.source.Stop();Destroy(voice.source.gameObject);voices.RemoveAt(index);
        }
        private void Update()
        {
            for(int i=voices.Count-1;i>=0;i--)
            {
                var voice=voices[i];var listener=session.LocalActor;
                bool invalidReload=false;
                if(voice.reload is ReloadSoundMessage message)
                {
                    var actor=session.World?.Find(message.actor);
                    invalidReload=actor==null || actor.down || !actor.connected || !SessionReloadAudio.Matches(actor,message);
                    if(!invalidReload) voice.position=actor.Position;
                }
                if(listener==null || listener.level!=voice.level || voice.remaining<=0 || invalidReload)
                {RemoveVoice(i);continue;}
                bool pause=session.Pause.IsPaused(voice.level);
                if(pause!=voice.paused) {if(pause) voice.source.Pause();else voice.source.UnPause();voice.paused=pause;}
                if(!pause) voice.remaining-=Time.deltaTime;
                voice.source.volume=Mathf.Clamp01(Volume)*voice.gain*Attenuation(Vector2.Distance(voice.position,listener.Position));
                voice.source.panStereo=mix.Pan(voice.position.x,view.transform.position.x);
            }
        }
        private void OnDestroy()
        {
            if(session!=null) {session.AttackReceived-=Attack;session.ReloadAudio.Received-=RemoteReload;}
            if(reload!=null) {reload.Started-=ReloadStarted;reload.Finished-=ReloadFinished;}
        }
    }
}
