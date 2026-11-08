using System;
using System.Collections.Generic;
using Remains.Content;
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
        private readonly Dictionary<string,PropContactObservation> tracks=new();
        private readonly Dictionary<string,AudioClip> clips=new();
        private readonly HashSet<string> active=new(),missing=new();
        private readonly HashSet<string> pending=new(StringComparer.Ordinal);
        private readonly List<string> samples=new();
        private readonly List<string> removed=new();
        private CoopSession session;
        private InteractionAdapter interactions;
        private SimulationGeometry geometry;
        private GameplayAudio playback;
        private Settings settings;
        private ItemLandingAudioCatalog itemSounds;
        private WorldPropView props;
        public int Landings {get;private set;}
        public ItemLandingAudioCatalog ItemSounds=>itemSounds;
        public event Action<string,string,AudioClip> LandingPlayed;
        public void Initialize(CoopSession value,SimulationGeometry geometry,GameplayAudio playback)=>Initialize(value,geometry,playback,null);
        public void Initialize(CoopSession value,SimulationGeometry geometry,GameplayAudio playback,ItemLandingAudioCatalog itemSounds,
            InteractionAdapter interactions=null)
        {
            session=value;this.geometry=geometry;this.playback=playback;this.interactions=interactions;
            this.itemSounds=itemSounds??ItemLandingAudioCatalog.Load();
            var asset=GameAssets.Load<TextAsset>("Presentation/prop-audio");
            if(asset==null) throw new InvalidOperationException("Нет настроек Presentation/prop-audio.");
            settings=JsonUtility.FromJson<Settings>(asset.text);
            if(!float.IsFinite(settings.volume) || settings.volume<=0 || settings.volume>1 ||
                !float.IsFinite(settings.teleportDistance) || settings.teleportDistance<=0 || settings.entries==null)
                throw new InvalidOperationException("Некорректные настройки звуков физических объектов.");
            foreach(var entry in settings.entries)
            {
                var clip=GameAssets.Load<AudioClip>(entry.clip);
                if(clip==null || clip.samples<=0) throw new InvalidOperationException($"Нет звука падения: definition={entry.definition}, clip={entry.clip}.");
                clips.Add(entry.definition,clip);
            }
            session.Changed+=Reconcile;
            if(interactions!=null) interactions.PropMoved+=OnPropMoved;
        }
        private void OnPropMoved(PropState prop,Vector2 previous)
        {
            if(tracks.ContainsKey(prop.id)) pending.Add(prop.id);
        }
        private string reconciledLevel;
        // Tracks follow the listener's level bodies; their support can change with bodies or doors only.
        private void Reconcile()
        {
            var listener=session.LocalActor;
            bool sameContext=listener!=null && listener.level==reconciledLevel && props?.World.worldId==session.World?.worldId;
            if(sameContext && !session.LastChange.Any(WorldChangeKind.Containers|WorldChangeKind.Props|WorldChangeKind.Doors)) return;
            reconciledLevel=listener?.level;
            if(session.World!=null && props?.World!=session.World) props=new WorldPropView(session.World);
            if(interactions!=null && sameContext && !session.LastChange.Any(WorldChangeKind.Doors) && session.LastChangedParts!=null)
            {
                foreach(string key in session.LastChangedParts)
                {
                    bool container=key.StartsWith("containers:",StringComparison.Ordinal);
                    if(!container && !key.StartsWith("props:",StringComparison.Ordinal)) continue;
                    string id=key.Substring(container?11:6);
                    var prop=WorldProps.Find(session.World,id);
                    if(prop==null || prop.level!=reconciledLevel || !prop.movable || !prop.hasPhysicalBody)
                    {tracks.Remove(id);pending.Remove(id);continue;}
                    if(!tracks.TryGetValue(id,out var track)) tracks.Add(id,track=new PropContactObservation());
                    track.InvalidateSupport();pending.Add(id);
                }
                return;
            }
            active.Clear();
            if(listener!=null && session.World!=null)
                foreach(var prop in props)
                    if(prop.level==listener.level && prop.movable && prop.hasPhysicalBody)
                    {active.Add(prop.id);if(!tracks.ContainsKey(prop.id)) tracks.Add(prop.id,new PropContactObservation());}
            removed.Clear();
            foreach(string id in tracks.Keys) if(!active.Contains(id)) removed.Add(id);
            foreach(string id in removed) tracks.Remove(id);
            // Shared changes can replace geometry or neighbours without moving this prop.
            foreach(var pair in tracks)
            {
                pair.Value.InvalidateSupport();
                if(interactions!=null) pending.Add(pair.Key);
            }
        }
        private void LateUpdate()
        {
            using var frameSection=Remains.Diagnostics.FrameHitchLog.Section("PropContactAudio.LateUpdate");
            if(interactions!=null && pending.Count==0) return;
            var listener=session.LocalActor;if(listener==null) return;
            if(props?.World!=session.World) props=new WorldPropView(session.World);
            var room=geometry.Get(listener.level);
            bool paused=session.Pause.IsPaused(listener.level);
            if(interactions==null)
            {
                foreach(var prop in props)
                    if(prop.level==listener.level) Sample(prop,room,paused);
            }
            else
            {
                samples.Clear();samples.AddRange(pending);pending.Clear();
                foreach(string id in samples)
                {
                    if(!tracks.ContainsKey(id)) continue;
                    var prop=WorldProps.Find(session.World,id);
                    if(prop?.level==listener.level) Sample(prop,room,paused);
                }
            }
        }
        private void Sample(PropState prop,RoomLayout room,bool paused)
        {
            if(!tracks.TryGetValue(prop.id,out var track) ||
                track.Sample(prop,room,props,paused,settings.teleportDistance,session.World.worldId)!=MotionContact.Landing) return;
            if(!TryGetLandingClip(prop,out var clip,out var reason))
            {
                string item=(prop as ContainerState)?.presentationItem;
                string key=item==null?prop.definition:"item:"+item;
                if(missing.Add(key)) Debug.LogWarning($"Нет звука падения: prop={prop.id}, definition={prop.definition}, presentationItem={item ?? "<отсутствует>"}, action=landing, level={prop.level}. {reason}",this);
                return;
            }
            if(playback.PlayContact(clip,prop.level,prop.Position,settings.volume))
            {Landings++;LandingPlayed?.Invoke(prop.id,(prop as ContainerState)?.presentationItem??prop.definition,clip);}
        }
        // The same cached resolver is available to diagnostics; it does not sample physics or load assets.
        public bool TryGetLandingClip(PropState prop,out AudioClip clip,out string reason)
        {
            clip=null;
            if(prop is ContainerState {presentationItem:not null} item)
                return itemSounds.TryGet(item.presentationItem,out clip,out reason);
            if(prop?.definition=="loose-item")
            {reason="У брошенного предмета отсутствует presentationItem; action=landing не может быть выбран.";return false;}
            reason=null;
            if(prop?.definition!=null && clips.TryGetValue(prop.definition,out clip)) return true;
            reason="Не объявлен банк физического объекта для action=landing.";
            return false;
        }
        private void OnDestroy()
        {
            if(session!=null) session.Changed-=Reconcile;
            if(interactions!=null) interactions.PropMoved-=OnPropMoved;
        }
    }
}
