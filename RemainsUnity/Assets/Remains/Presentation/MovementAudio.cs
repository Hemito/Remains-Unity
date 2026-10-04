using System;
using System.Collections.Generic;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using UnityEngine;

namespace Remains.Presentation
{
    public sealed class MovementAudio : MonoBehaviour
    {
        [Serializable] public sealed class SoundBank {public string id;public string[] clips;}
        [Serializable] public sealed class Settings
        {
            public float teleportDistance,volume,landingGain,ladderGain;
            public MovementBeatPattern[] rhythms;
            public string[] quietPoses;
            public int landingLayers;
            public string[] actorTypes;
            public SoundBank[] banks;
        }
        private sealed class Track {public readonly MotionContacts contacts=new();public readonly MovementRhythm rhythm=new();public int sample;}
        private CoopSession session;
        private SimulationGeometry geometry;
        private GameplayAudio audio;
        private Settings settings;
        private readonly Dictionary<string,Track> tracks=new();
        private readonly Dictionary<string,AudioClip[]> banks=new();
        private readonly Dictionary<string,MovementBeatPattern> rhythms=new();
        private readonly HashSet<string> quietPoses=new();
        private readonly HashSet<string> types=new(),missing=new(),active=new();
        private readonly List<string> removed=new();
        public int LocalContacts {get;private set;}
        public int RemoteContacts {get;private set;}
        public int Landings {get;private set;}
        public int LandingVoices {get;private set;}
        public int LadderContacts {get;private set;}

        public void Initialize(CoopSession session,SimulationGeometry geometry,GameplayAudio audio)
        {
            this.session=session;this.geometry=geometry;this.audio=audio;
            var asset=Resources.Load<TextAsset>("Presentation/movement-audio");
            if(asset==null) throw new InvalidOperationException("Нет настроек Presentation/movement-audio.");
            settings=JsonUtility.FromJson<Settings>(asset.text);
            if(settings.teleportDistance<=0 || settings.rhythms==null || settings.quietPoses==null ||
                settings.volume<=0 || settings.volume>1 || settings.landingGain<=0 || settings.ladderGain<=0 || settings.landingLayers<1 || settings.landingLayers>8 || settings.banks==null || settings.actorTypes==null)
                throw new InvalidOperationException("Некорректные настройки звуков движения.");
            foreach(var rhythm in settings.rhythms) {rhythm.Validate();rhythms.Add(rhythm.pose,rhythm);}
            foreach(string pose in settings.quietPoses)
                if(string.IsNullOrWhiteSpace(pose) || rhythms.ContainsKey(pose) || !quietPoses.Add(pose))
                    throw new InvalidOperationException("Некорректная тихая поза звука движения: "+pose);
            foreach(string type in settings.actorTypes) types.Add(type);
            foreach(var bank in settings.banks)
            {
                if(bank.clips==null || bank.clips.Length==0) throw new InvalidOperationException("Пустой набор шагов: "+bank.id);
                var clips=new AudioClip[bank.clips.Length];
                for(int i=0;i<clips.Length;i++)
                {
                    clips[i]=Resources.Load<AudioClip>(bank.clips[i]);
                    if(clips[i]==null || clips[i].samples<=0) throw new InvalidOperationException("Нет записи шагов: "+bank.clips[i]);
                }
                banks.Add(bank.id,clips);
            }
            session.Changed+=Reconcile;
        }
        private void Reconcile()
        {
            active.Clear();
            var listener=session.LocalActor;
            if(listener!=null)
                foreach(var actor in session.World.actors)
                    if(actor.level==listener.level && types.Contains(actor.actorType))
                    {active.Add(actor.id);if(!tracks.ContainsKey(actor.id)) tracks.Add(actor.id,new Track());}
            removed.Clear();
            foreach(string id in tracks.Keys) if(!active.Contains(id)) removed.Add(id);
            foreach(string id in removed) tracks.Remove(id);
        }
        private void LateUpdate()
        {
            var listener=session.LocalActor;if(listener==null) return;
            var room=geometry.Get(listener.level);
            bool paused=session.Pause.IsPaused(listener.level);
            foreach(var actor in session.World.actors)
            {
                if(actor.level!=listener.level || !tracks.TryGetValue(actor.id,out var track)) continue;
                bool grounded=ActorTerrainMotion.Supported(room,Anatomy.Movement(actor),actor.Position);
                var contact=track.contacts.Sample(actor.Position,actor.level,actor.generation,grounded,actor.climbing,
                    paused || actor.down,float.PositiveInfinity,float.PositiveInfinity,settings.teleportDistance);
                rhythms.TryGetValue(actor.pose,out var rhythm);
                if(rhythm==null && !quietPoses.Contains(actor.pose) && missing.Add("pose:"+actor.pose))
                    Debug.LogWarning($"Нет ритма или явной тихой позы: actor={actor.id}, pose={actor.pose}, level={actor.level}. Шаги пропущены.",this);
                int beats=track.rhythm.Sample(actor,rhythm,grounded,paused || actor.down,settings.teleportDistance);
                if(contact!=MotionContact.Landing && beats>0) contact=actor.climbing?MotionContact.Ladder:MotionContact.Step;
                if(contact==MotionContact.None) continue;
                string bank=contact==MotionContact.Ladder?"ladder":SurfaceContactSound.Resolve(room,Anatomy.Movement(actor),actor.Position);
                if(string.IsNullOrEmpty(bank) || !banks.TryGetValue(bank,out var clips))
                {
                    if(missing.Add(room.id+":"+bank)) Debug.LogWarning($"Нет звуков поверхности: room={room.id}, bank={bank}, actor={actor.id}.",this);
                    continue;
                }
                float gain=settings.volume*(contact==MotionContact.Landing?settings.landingGain:contact==MotionContact.Ladder?settings.ladderGain:1);
                int layers=contact==MotionContact.Landing?settings.landingLayers:beats;
                int played=0;
                for(int layer=0;layer<layers;layer++)
                    if(audio.PlayContact(clips[track.sample++%clips.Length],actor.level,actor.Position,gain)) played++;
                if(played==0) continue;
                if(actor.id==listener.id) LocalContacts++;else RemoteContacts++;
                if(contact==MotionContact.Landing) {Landings++;LandingVoices+=played;}
                if(contact==MotionContact.Ladder) LadderContacts++;
            }
        }
        private void OnDestroy() {if(session!=null) session.Changed-=Reconcile;}
    }
}
