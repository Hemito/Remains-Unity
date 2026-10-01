using System;
using System.Collections.Generic;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using UnityEngine;

namespace Remains.Presentation
{
    // Observes physical state locally; no gameplay effects or extra authority.
    public sealed class PropContactAudio : MonoBehaviour
    {
        [Serializable] public sealed class Entry {public string definition,clip;}
        [Serializable] public sealed class Settings {public float volume,teleportDistance;public Entry[] entries;}
        private sealed class Track {public readonly MotionContacts contacts=new();public readonly MovementHull hull=new();}
        private readonly Dictionary<string,Track> tracks=new();
        private readonly Dictionary<string,AudioClip> clips=new();
        private readonly HashSet<string> active=new(),missing=new();
        private readonly List<string> removed=new();
        private CoopSession session;
        private SimulationGeometry geometry;
        private GameplayAudio playback;
        private Settings settings;
        public int Landings {get;private set;}
        public void Initialize(CoopSession value,SimulationGeometry geometry,GameplayAudio playback)
        {
            session=value;this.geometry=geometry;this.playback=playback;
            var asset=Resources.Load<TextAsset>("Presentation/prop-audio");
            if(asset==null) throw new InvalidOperationException("Нет настроек Presentation/prop-audio.");
            settings=JsonUtility.FromJson<Settings>(asset.text);
            if(!float.IsFinite(settings.volume) || settings.volume<=0 || settings.volume>1 ||
                !float.IsFinite(settings.teleportDistance) || settings.teleportDistance<=0 || settings.entries==null)
                throw new InvalidOperationException("Некорректные настройки звуков физических объектов.");
            foreach(var entry in settings.entries)
            {
                var clip=Resources.Load<AudioClip>(entry.clip);
                if(clip==null || clip.samples<=0) throw new InvalidOperationException($"Нет звука падения: definition={entry.definition}, clip={entry.clip}.");
                clips.Add(entry.definition,clip);
            }
            session.Changed+=Reconcile;
        }
        private void Reconcile()
        {
            active.Clear();
            var listener=session.LocalActor;
            if(listener!=null)
                foreach(var prop in WorldProps.All(session.World))
                    if(prop.level==listener.level && prop.movable)
                    {active.Add(prop.id);if(!tracks.ContainsKey(prop.id)) tracks.Add(prop.id,new Track());}
            removed.Clear();
            foreach(string id in tracks.Keys) if(!active.Contains(id)) removed.Add(id);
            foreach(string id in removed) tracks.Remove(id);
        }
        private void LateUpdate()
        {
            var listener=session.LocalActor;if(listener==null) return;
            var room=geometry.Get(listener.level);
            bool paused=session.Pause.IsPaused(listener.level);
            foreach(var prop in WorldProps.All(session.World))
            {
                if(prop.level!=listener.level || !tracks.TryGetValue(prop.id,out var track)) continue;
                track.hull.width=prop.width;track.hull.height=prop.height;
                var feet=prop.Position-Vector2.up*prop.anchorHeight;
                bool grounded=ActorTerrainMotion.Supported(room,track.hull,feet);
                var contact=track.contacts.Sample(feet,prop.level,prop.generation,grounded,false,
                    paused || prop.held,float.MaxValue,float.MaxValue,settings.teleportDistance);
                if(contact!=MotionContact.Landing) continue;
                if(!clips.TryGetValue(prop.definition,out var clip))
                {
                    if(missing.Add(prop.definition)) Debug.LogWarning($"Нет звука падения: prop={prop.id}, definition={prop.definition}, level={prop.level}.",this);
                    continue;
                }
                if(playback.PlayContact(clip,prop.level,prop.Position,settings.volume)) Landings++;
            }
        }
        private void OnDestroy() {if(session!=null) session.Changed-=Reconcile;}
    }
}
