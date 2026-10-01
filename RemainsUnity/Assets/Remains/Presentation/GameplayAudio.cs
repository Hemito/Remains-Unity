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
        private sealed class Voice
        {
            public AudioSource source;
            public string level;
            public Vector2 position;
            public float remaining;
            public bool paused;
        }
        private CoopSession session;
        private WeaponReloadController reload;
        private readonly Dictionary<string,AudioClip> shots=new();
        private readonly List<Voice> voices=new();
        private readonly HashSet<string> seen=new(),missing=new();
        private readonly Queue<string> recent=new();
        private AudioClip reloadClip;
        private Voice reloadVoice;
        public int LocalShots {get;private set;}
        public int RemoteShots {get;private set;}
        public int ReloadStarts {get;private set;}
        public float Volume {get;set;}=.65f;
        public void Initialize(CoopSession session,WeaponReloadController reload,Camera camera)
        {
            this.session=session;this.reload=reload;
            var pistol=Load("pistol-shot");shots.Add("pistol",pistol);shots.Add("npc-shot",pistol);
            shots.Add("energy",Load("laser-shot"));reloadClip=Load("pistol-reload");
            if(camera.GetComponent<AudioListener>()==null) camera.gameObject.AddComponent<AudioListener>();
            session.AttackReceived+=Attack;reload.Started+=ReloadStarted;reload.Finished+=ReloadFinished;
        }
        private static AudioClip Load(string key)
        {
            var clip=Resources.Load<AudioClip>("Original/Audio/"+key);
            if(clip==null || clip.samples<=0) throw new InvalidOperationException("Отсутствует или пуст звук Original/Audio/"+key);
            return clip;
        }
        public static float Attenuation(float distance)=>Mathf.Clamp01(1-Mathf.Max(0,distance-2)/23);
        private Voice Play(AudioClip clip,string level,Vector2 position)
        {
            if(voices.Count>=64) {Debug.LogWarning($"Превышен лимит 64 звуков: clip={clip.name}, level={level}.",this);return null;}
            var source=new GameObject("Звук "+clip.name).AddComponent<AudioSource>();source.transform.SetParent(transform,false);
            source.playOnAwake=false;source.spatialBlend=0;source.clip=clip;
            // Background verification must not make sound on the user's desktop.
            source.mute=Application.isBatchMode;
            source.volume=Mathf.Clamp01(Volume)*Attenuation(Vector2.Distance(position,session.LocalActor.Position));
            var voice=new Voice {source=source,level=level,position=position,remaining=clip.length};voices.Add(voice);
            source.Play();
            if(session.Pause.IsPaused(level)) {source.Pause();voice.paused=true;}
            return voice;
        }
        private void Attack(AttackEvent attack)
        {
            if(session.LocalActor?.level!=attack.level || attack.weapon is "spear" or "crowbar" or "grenade") return;
            if(!shots.TryGetValue(attack.weapon,out var clip))
            {if(missing.Add(attack.weapon)) Debug.LogWarning($"Нет звука выстрела: weapon={attack.weapon}, attack={attack.id}.",this);return;}
            if(!seen.Add(attack.id)) return;
            recent.Enqueue(attack.id);if(recent.Count>512) seen.Remove(recent.Dequeue());
            if(Play(clip,attack.level,attack.Origin)==null) return;
            if(attack.attacker==session.LocalActorId) LocalShots++;else RemoteShots++;
        }
        private void ReloadStarted(ActorState actor,string weapon)
        {
            ReloadFinished();
            if(weapon is not ("pistol" or "energy"))
            {Debug.LogWarning($"Нет звука перезарядки: actor={actor.id}, weapon={weapon}.",this);return;}
            reloadVoice=Play(reloadClip,actor.level,actor.Position);if(reloadVoice!=null) ReloadStarts++;
        }
        private void ReloadFinished()
        {
            if(reloadVoice==null) return;
            if(reloadVoice.source!=null) {reloadVoice.source.Stop();Destroy(reloadVoice.source.gameObject);}
            voices.Remove(reloadVoice);reloadVoice=null;
        }
        private void Update()
        {
            for(int i=voices.Count-1;i>=0;i--)
            {
                var voice=voices[i];var listener=session.LocalActor;
                if(listener==null || listener.level!=voice.level || voice.remaining<=0)
                {voice.source.Stop();Destroy(voice.source.gameObject);voices.RemoveAt(i);if(voice==reloadVoice) reloadVoice=null;continue;}
                bool pause=session.Pause.IsPaused(voice.level);
                if(pause!=voice.paused) {if(pause) voice.source.Pause();else voice.source.UnPause();voice.paused=pause;}
                if(!pause) voice.remaining-=Time.deltaTime;
                voice.source.volume=Mathf.Clamp01(Volume)*Attenuation(Vector2.Distance(voice.position,listener.Position));
            }
        }
        private void OnDestroy()
        {
            if(session!=null) session.AttackReceived-=Attack;
            if(reload!=null) {reload.Started-=ReloadStarted;reload.Finished-=ReloadFinished;}
        }
    }
}
