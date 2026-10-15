using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;

namespace Remains.Presentation
{
    // The mines of the visible level (original Mine.as vis): frame 2 while armed, the frames from 2 on looping at 30 fps
    // once triggered, a beep every 5 frames of the countdown. A level mine stays hidden until the local player has been
    // within its visibility radius (then it stays seen for this client); triggered and player mines are always shown.
    public sealed class MineVisuals : MonoBehaviour
    {
        private const float FramesPerSecond=30,BeepPeriod=5/FramesPerSecond,SpotPeriod=.2f;
        private sealed class Art {public Sprite[] frames;public float scale;public Vector2 offset;public AudioClip beep,defuse;}
        private sealed class Entry {public MineState state;public MineProfile profile;public Art art;public SpriteRenderer renderer;public float animation,beep;}
        private readonly Dictionary<string,Entry> views=new(StringComparer.Ordinal);
        private readonly Dictionary<string,Art> arts=new(StringComparer.Ordinal);
        private readonly HashSet<string> spotted=new(StringComparer.Ordinal);
        private readonly HashSet<string> current=new(StringComparer.Ordinal);
        private CoopSession session;
        private GameplayAudio playback;
        private WorldVisualOrderTable orders;
        private ExportedContentCatalog exported;
        private string worldId,level;
        private float spotClock;
        public int Count=>views.Count;
        public int Beeps {get;private set;}
        public SpriteRenderer Find(string id)=>views.TryGetValue(id,out var entry)?entry.renderer:null;
        public bool Shown(string id)=>views.TryGetValue(id,out var entry) && entry.renderer.enabled;

        public void Initialize(CoopSession value,GameplayAudio audio,WorldVisualOrderTable orderTable)
        {
            session=value;playback=audio;orders=orderTable??throw new ArgumentNullException(nameof(orderTable));
            exported=ExportedContentCatalog.Load();
            session.Changed+=Refresh;session.Mines.DisarmHeard+=Disarmed;Refresh();
        }

        // Views change only with mines, the visible level or the world (CoopSession.LastChange).
        private void Refresh()
        {
            var world=session.World;string visible=session.LocalActor?.level;
            bool identity=worldId!=world?.worldId || level!=visible;
            if(!identity && !session.LastChange.Any(WorldChangeKind.Mines)) return;
            if(identity) {Clear();if(worldId!=world?.worldId) spotted.Clear();worldId=world?.worldId;level=visible;}
            if(world==null || visible==null) return;
            current.Clear();
            foreach(var mine in world.mines)
            {
                if(mine.level!=visible) continue;
                if(!current.Add(mine.id)) throw new InvalidOperationException("Повторный ID изображения мины: "+mine.id);
                if(!views.TryGetValue(mine.id,out var entry)) views.Add(mine.id,entry=Create(mine));
                if(entry.state.triggered && !mine.triggered) throw new InvalidOperationException($"Мина {mine.id}: снимок отменяет срабатывание.");
                entry.state=mine;
                if(mine.triggered) spotted.Add(mine.id);
                Show(entry);
            }
            foreach(string id in views.Keys.Where(id=>!current.Contains(id)).ToArray()) Remove(id);
            spotClock=SpotPeriod;
        }

        private Entry Create(MineState mine)
        {
            var profile=MineRules.Profile(mine);
            var art=Prepare(mine.definition,profile);
            var root=new GameObject("Мина: "+mine.id);root.transform.SetParent(transform,false);
            root.transform.localPosition=new Vector3(mine.x,mine.y,0);
            var body=new GameObject("Изображение");body.transform.SetParent(root.transform,false);
            body.transform.localPosition=art.offset;body.transform.localScale=new Vector3(art.scale,art.scale,1);
            var renderer=body.AddComponent<SpriteRenderer>();SpriteArtworkMaterial.Apply(renderer,art.frames[0]);
            root.AddComponent<WorldVisualOrderBinding>().Initialize(orders,WorldVisualKind.Mine,mine.id,null,renderer);
            return new Entry {state=mine,profile=profile,art=art,renderer=renderer};
        }

        // Frames from the exported original (frame 1 is the reloading pose of a just placed mine; 2 is armed). The width
        // follows the definition's sX; the drawing stands on the mine's bottom centre.
        private Art Prepare(string definition,MineProfile profile)
        {
            if(arts.TryGetValue(definition,out var art)) return art;
            var resource=exported.Resource(profile.artwork);
            var frames=(resource.frames??Array.Empty<ExportedFrame>()).Where(f=>f.frame>=2).OrderBy(f=>f.frame).Select(exported.FrameSprite).ToArray();
            if(frames.Length==0 || frames.Any(f=>f==null))
                throw new InvalidOperationException($"Мина {definition}: нет кадров изображения {profile.artwork} начиная со второго.");
            foreach(var frame in frames) SpriteArtworkMaterial.Prepare(frame);
            var bounds=frames[0].bounds;
            float scale=profile.width/bounds.size.x;
            art=new Art {frames=frames,scale=scale,offset=new Vector2(-bounds.center.x*scale,-bounds.min.y*scale),
                beep=Sound(profile.beepSound,definition),defuse=Sound(profile.defuseSound,definition)};
            arts.Add(definition,art);
            return art;
        }
        private AudioClip Sound(string id,string definition)
        {
            var sounds=exported.Resource(id).sounds;
            if(sounds==null || sounds.Length==0 || sounds[0]==null) throw new InvalidOperationException($"Мина {definition}: нет звука {id}.");
            return sounds[0];
        }

        private void Show(Entry entry)
        {
            entry.renderer.enabled=entry.state.triggered || entry.state.side==MineRules.PlayerSide || spotted.Contains(entry.state.id);
            if(!entry.state.triggered) {entry.animation=0;SpriteArtworkMaterial.Apply(entry.renderer,entry.art.frames[0]);}
        }

        // Per frame only the triggered mines animate and beep; spotting is a light distance test of this level's few
        // mines a few times per second.
        private void Update()
        {
            if(views.Count==0) return;
            float dt=Time.deltaTime;
            bool paused=level!=null && session.Pause.IsPaused(level);
            spotClock+=dt;
            if(spotClock>=SpotPeriod)
            {
                spotClock=0;var viewer=session.LocalActor;
                foreach(var entry in views.Values)
                    if(!entry.renderer.enabled && MineRules.Visible(entry.state,viewer,entry.profile)) {spotted.Add(entry.state.id);Show(entry);}
            }
            if(paused) return;
            foreach(var entry in views.Values)
            {
                if(!entry.state.triggered) continue;
                entry.animation+=dt;
                int frame=(int)(entry.animation*FramesPerSecond)%entry.art.frames.Length;
                if(entry.renderer.sprite!=entry.art.frames[frame]) SpriteArtworkMaterial.Apply(entry.renderer,entry.art.frames[frame]);
                entry.beep-=dt;
                if(entry.beep>0) continue;
                entry.beep+=BeepPeriod;if(entry.beep<0) entry.beep=BeepPeriod;
                Beeps++;playback?.PlayContact(entry.art.beep,entry.state.level,entry.state.Position,1);
            }
        }

        private void Disarmed(MineDisarmedMessage message)
        {
            if(message.level!=level) return;
            var profile=MineRules.Profile(new MineState {id=message.mine,definition=message.definition});
            playback?.PlayContact(Prepare(message.definition,profile).defuse,message.level,new Vector2(message.x,message.y),1);
        }

        private void Remove(string id)
        {
            var entry=views[id];var root=entry.renderer.transform.parent.gameObject;
            root.SetActive(false);if(Application.isPlaying) Destroy(root);else DestroyImmediate(root);views.Remove(id);
        }
        private void Clear() {foreach(string id in views.Keys.ToArray()) Remove(id);}
        private void OnDestroy() {if(session!=null) {session.Changed-=Refresh;session.Mines.DisarmHeard-=Disarmed;}}
    }
}
