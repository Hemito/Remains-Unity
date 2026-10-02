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
            public float stepDistance,ladderDistance,teleportDistance,volume,landingGain,ladderGain;
            public string[] actorTypes;
            public SoundBank[] banks;
        }
        private sealed class Track {public readonly MotionContacts contacts=new();public int sample;}
        private CoopSession session;
        private SimulationGeometry geometry;
        private GameplayAudio audio;
        private Settings settings;
        private readonly Dictionary<string,Track> tracks=new();
        private readonly Dictionary<string,AudioClip[]> banks=new();
        private readonly HashSet<string> types=new(),missing=new(),active=new();
        private readonly List<string> removed=new();
        public int LocalContacts {get;private set;}
        public int RemoteContacts {get;private set;}
        public int Landings {get;private set;}
        public int LadderContacts {get;private set;}

        public void Initialize(CoopSession session,SimulationGeometry geometry,GameplayAudio audio)
        {
            this.session=session;this.geometry=geometry;this.audio=audio;
            var asset=Resources.Load<TextAsset>("Presentation/movement-audio");
            if(asset==null) throw new InvalidOperationException("Нет настроек Presentation/movement-audio.");
            settings=JsonUtility.FromJson<Settings>(asset.text);
            if(settings.stepDistance<=0 || settings.ladderDistance<=0 || settings.teleportDistance<=0 ||
                settings.volume<=0 || settings.volume>1 || settings.landingGain<=0 || settings.ladderGain<=0 || settings.banks==null || settings.actorTypes==null)
                throw new InvalidOperationException("Некорректные настройки звуков движения.");
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
                    paused || actor.down,settings.stepDistance,settings.ladderDistance,settings.teleportDistance);
                if(contact==MotionContact.None) continue;
                string bank=contact==MotionContact.Ladder?"ladder":room.movementSound;
                if(string.IsNullOrEmpty(bank) || !banks.TryGetValue(bank,out var clips))
                {
                    if(missing.Add(room.id+":"+bank)) Debug.LogWarning($"Нет звуков поверхности: room={room.id}, bank={bank}, actor={actor.id}.",this);
                    continue;
                }
                float gain=settings.volume*(contact==MotionContact.Landing?settings.landingGain:contact==MotionContact.Ladder?settings.ladderGain:1);
                if(!audio.PlayContact(clips[track.sample++%clips.Length],actor.level,actor.Position,gain)) continue;
                if(actor.id==listener.id) LocalContacts++;else RemoteContacts++;
                if(contact==MotionContact.Landing) Landings++;
                if(contact==MotionContact.Ladder) LadderContacts++;
            }
        }
        private void OnDestroy() {if(session!=null) session.Changed-=Reconcile;}
    }
}
