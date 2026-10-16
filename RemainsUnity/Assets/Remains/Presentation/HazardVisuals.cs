using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;

namespace Remains.Presentation
{
    // The traps and charges of the visible level (original UnitTrap/UnitDamager vis): a trap rests on frame 1 open and
    // frame 4 shut; snapping plays frames 1–4 and rearming 5–20 at 30 fps (UnitTrap.klac / disarm). A charge has one
    // frame. A level hazard stays hidden until the local player has been within its visibility radius (then it stays
    // seen for this client); a player's trap is always shown.
    public sealed class HazardVisuals : MonoBehaviour
    {
        private const float FramesPerSecond=30,SpotPeriod=.2f;
        private const int ShutFrame=3,SnapLast=3,RearmFirst=4;
        private sealed class Entry {public HazardState state;public HazardDefinition profile;public Sprite[] frames;public SpriteRenderer renderer;public int from=-1,to;public float animation;}
        private readonly Dictionary<string,Entry> views=new(StringComparer.Ordinal);
        private readonly Dictionary<string,Sprite[]> arts=new(StringComparer.Ordinal);
        private readonly Dictionary<string,AudioClip> sounds=new(StringComparer.Ordinal);
        private readonly HashSet<string> spotted=new(StringComparer.Ordinal),current=new(StringComparer.Ordinal);
        private readonly List<Entry> playing=new();
        private CoopSession session;
        private GameplayAudio playback;
        private WorldVisualOrderTable orders;
        private ExportedContentCatalog exported;
        private string worldId,level;
        private float spotClock;
        public int Count=>views.Count;
        public int Animations {get;private set;}
        public SpriteRenderer Find(string id)=>views.TryGetValue(id,out var entry)?entry.renderer:null;
        public bool Shown(string id)=>views.TryGetValue(id,out var entry) && entry.renderer.enabled;

        public void Initialize(CoopSession value,GameplayAudio audio,WorldVisualOrderTable orderTable)
        {
            session=value;playback=audio;orders=orderTable??throw new ArgumentNullException(nameof(orderTable));
            exported=ExportedContentCatalog.Load();
            session.Changed+=Refresh;session.Hazards.EventHeard+=Heard;Refresh();
        }

        // Views change only with hazards, the visible level or the world (CoopSession.LastChange).
        private void Refresh()
        {
            var world=session.World;string visible=session.LocalActor?.level;
            bool identity=worldId!=world?.worldId || level!=visible;
            if(!identity && !session.LastChange.Any(WorldChangeKind.Hazards)) return;
            if(identity) {Clear();if(worldId!=world?.worldId) spotted.Clear();worldId=world?.worldId;level=visible;}
            if(world==null || visible==null) return;
            current.Clear();
            foreach(var hazard in world.hazards)
            {
                if(hazard.level!=visible) continue;
                if(!current.Add(hazard.id)) throw new InvalidOperationException("Повторный ID изображения ловушки: "+hazard.id);
                if(!views.TryGetValue(hazard.id,out var entry)) views.Add(hazard.id,entry=Create(hazard));
                entry.state=hazard;
                entry.renderer.transform.parent.localPosition=new Vector3(hazard.x,hazard.y,0);
                Show(entry);
            }
            foreach(string id in views.Keys.Where(id=>!current.Contains(id)).ToArray()) Remove(id);
            spotClock=SpotPeriod;
        }

        private Entry Create(HazardState hazard)
        {
            var profile=HazardRules.Profile(hazard);
            var frames=Prepare(profile);
            var root=new GameObject($"{profile.title}: {hazard.id}");root.transform.SetParent(transform,false);
            root.transform.localPosition=new Vector3(hazard.x,hazard.y,0);
            var body=new GameObject("Изображение");body.transform.SetParent(root.transform,false);
            // Exported frames keep the original scale; their pivot is the unit's registration point (its feet).
            var renderer=body.AddComponent<SpriteRenderer>();SpriteArtworkMaterial.Apply(renderer,frames[0]);
            root.AddComponent<WorldVisualOrderBinding>().Initialize(orders,WorldVisualKind.Hazard,hazard.id,null,renderer);
            return new Entry {state=hazard,profile=profile,frames=frames,renderer=renderer};
        }
        private Sprite[] Prepare(HazardDefinition profile)
        {
            if(arts.TryGetValue(profile.id,out var frames)) return frames;
            var all=exported.Resource(profile.artwork).frames??Array.Empty<ExportedFrame>();
            frames=all.OrderBy(f=>f.frame).Select(exported.FrameSprite).ToArray();
            if(frames.Length==0 || frames.Any(f=>f==null) || profile.kind==HazardRules.Trap && frames.Length<=RearmFirst)
                throw new InvalidOperationException($"Ловушка {profile.id}: нет кадров изображения {profile.artwork} (капкан: 1 открыт, 2–4 захлопывается, 5+ взводится).");
            foreach(var frame in frames) SpriteArtworkMaterial.Prepare(frame);
            arts.Add(profile.id,frames);
            return frames;
        }
        private AudioClip Sound(string resource)
        {
            if(sounds.TryGetValue(resource,out var clip)) return clip;
            sounds.Add(resource,clip=AudioClipSource.Load(AudioClipSource.ExportedPrefix+resource));
            return clip;
        }

        private int Rest(Entry entry)=>entry.profile.kind==HazardRules.Trap && entry.state.closed?ShutFrame:0;
        private void Show(Entry entry)
        {
            entry.renderer.enabled=entry.state.side==MineRules.PlayerSide || spotted.Contains(entry.state.id);
            if(entry.from<0) SpriteArtworkMaterial.Apply(entry.renderer,entry.frames[Rest(entry)]);
        }

        // Per frame only a snapping or rearming trap animates; spotting is a light distance test of this level's few
        // hazards a few times per second.
        private void Update()
        {
            if(views.Count==0) return;
            float dt=Time.deltaTime;
            spotClock+=dt;
            if(spotClock>=SpotPeriod)
            {
                spotClock=0;var viewer=session.LocalActor;
                foreach(var entry in views.Values)
                    if(!entry.renderer.enabled && HazardRules.Visible(entry.state,viewer,entry.profile)) {spotted.Add(entry.state.id);Show(entry);}
            }
            if(playing.Count==0 || level!=null && session.Pause.IsPaused(level)) return;
            for(int i=playing.Count-1;i>=0;i--)
            {
                var entry=playing[i];entry.animation+=dt;
                int frame=entry.from+(int)(entry.animation*FramesPerSecond);
                if(frame>entry.to) {entry.from=-1;playing.RemoveAt(i);SpriteArtworkMaterial.Apply(entry.renderer,entry.frames[Rest(entry)]);continue;}
                if(entry.renderer.sprite!=entry.frames[frame]) SpriteArtworkMaterial.Apply(entry.renderer,entry.frames[frame]);
            }
        }

        private void Heard(HazardEventMessage message)
        {
            if(message.level!=level) return;
            var profile=HazardCatalog.Get(message.definition);var position=new Vector2(message.x,message.y);
            switch(message.kind)
            {
                case SessionHazards.Snap: Play(message.hazard,0,SnapLast);playback?.PlayContact(Sound(profile.snapSound),message.level,position,1);break;
                case SessionHazards.Rearm: Play(message.hazard,RearmFirst,int.MaxValue);playback?.PlayContact(Sound(profile.rearmSound),message.level,position,1);break;
                // A bite sounds as its attack (combat-audio: the bite weapon); a disarmed charge and a broken trap are
                // simply removed with the world state.
            }
            spotted.Add(message.hazard);
            if(views.TryGetValue(message.hazard,out var seen)) Show(seen);
        }
        private void Play(string id,int from,int to)
        {
            if(!views.TryGetValue(id,out var entry)) return;
            entry.from=from;entry.to=Mathf.Min(to,entry.frames.Length-1);entry.animation=0;Animations++;
            SpriteArtworkMaterial.Apply(entry.renderer,entry.frames[from]);
            if(!playing.Contains(entry)) playing.Add(entry);
        }

        private void Remove(string id)
        {
            var entry=views[id];var root=entry.renderer.transform.parent.gameObject;playing.Remove(entry);
            root.SetActive(false);if(Application.isPlaying) Destroy(root);else DestroyImmediate(root);views.Remove(id);
        }
        private void Clear() {foreach(string id in views.Keys.ToArray()) Remove(id);playing.Clear();}
        private void OnDestroy() {if(session!=null) {session.Changed-=Refresh;session.Hazards.EventHeard-=Heard;}}
    }
}
