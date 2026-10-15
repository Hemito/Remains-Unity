using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;

namespace Remains.Presentation
{
    // The mines of the visible level (original Mine.as vis): frame 1 while a placed mine arms, frame 2 while armed, the frames from 2 on looping at 30 fps
    // once triggered, a beep every 5 frames of the countdown. A level mine stays hidden until the local player has been
    // within its visibility radius (then it stays seen for this client); triggered and player mines are always shown.
    public sealed class MineVisuals : MonoBehaviour
    {
        private const float FramesPerSecond=30,BeepPeriod=5/FramesPerSecond,SpotPeriod=.2f;
        private sealed class Art {public Sprite[] frames;public Sprite arming;public float scale;public Vector2 offset;public AudioClip beep,defuse,place;}
        private sealed class Entry {public MineState state;public MineProfile profile;public Art art;public SpriteRenderer renderer;public float animation,beep,fall,fallSpeed;}
        private readonly List<Entry> falling=new();
        private float gravity;
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
            exported=ExportedContentCatalog.Load();gravity=WeaponCatalog.Get("grenade").grenadeGravity;
            session.Changed+=Refresh;session.Mines.DisarmHeard+=Disarmed;session.Mines.PlaceHeard+=Placed;session.Mines.PoseHeard+=Pose;Refresh();
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
                // A held mine is drawn at its relayed telekinesis pose, a lying one at its state.
                if(mine.holder==null) entry.renderer.transform.parent.localPosition=new Vector3(mine.x,mine.y,0);
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
            var all=resource.frames??Array.Empty<ExportedFrame>();
            var frames=all.Where(f=>f.frame>=2).OrderBy(f=>f.frame).Select(exported.FrameSprite).ToArray();
            var arming=all.Where(f=>f.frame==1).Select(exported.FrameSprite).FirstOrDefault();
            if(frames.Length==0 || frames.Any(f=>f==null) || arming==null)
                throw new InvalidOperationException($"Мина {definition}: нет кадров изображения {profile.artwork} (1 — взведение, со 2 — взведена).");
            SpriteArtworkMaterial.Prepare(arming);foreach(var frame in frames) SpriteArtworkMaterial.Prepare(frame);
            var bounds=frames[0].bounds;
            float scale=profile.width/bounds.size.x;
            art=new Art {frames=frames,arming=arming,scale=scale,offset=new Vector2(-bounds.center.x*scale,-bounds.min.y*scale),
                beep=Sound(profile.beepSound,definition),defuse=Sound(profile.defuseSound,definition),place=Sound(profile.placeSound,definition)};
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
            if(!entry.state.triggered) {entry.animation=0;SpriteArtworkMaterial.Apply(entry.renderer,entry.state.arming>0?entry.art.arming:entry.art.frames[0]);}
        }

        // Per frame only the triggered mines animate and beep; spotting is a light distance test of this level's few
        // mines a few times per second.
        private void Update()
        {
            if(views.Count==0) return;
            float dt=Time.deltaTime;
            // A released mine is drawn falling from where it was held (its state already rests on the support).
            for(int i=falling.Count-1;i>=0;i--)
            {
                var drop=falling[i];
                drop.fallSpeed+=gravity*dt;drop.fall=Mathf.Max(0,drop.fall-drop.fallSpeed*dt);
                drop.renderer.transform.localPosition=(Vector3)drop.art.offset+Vector3.up*drop.fall;
                if(drop.fall<=0) falling.RemoveAt(i);
            }
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

        private void Placed(MinePlaceRequest message)
        {
            if(message.level!=level) return;
            string weapon=MineRules.PlacedWeapon(session.World?.Find(message.actor)?.inventory.Find(i=>i.id==message.item)?.definition) ??
                session.World?.mines.Find(m=>m.id=="placed:"+message.transaction)?.definition;
            if(weapon==null) {Debug.LogWarning($"Звук установки мины пропущен: мина placed:{message.transaction} ещё не получена, уровень {message.level}.",this);return;}
            var profile=MineRules.Profile(new MineState {id=message.transaction,definition=weapon});
            playback?.PlayContact(Prepare(weapon,profile).place,message.level,new Vector2(message.x,message.y),1);
            if(message.drop>0 && views.TryGetValue("placed:"+message.transaction,out var entry) && !falling.Contains(entry))
            {
                entry.fall=message.drop;entry.fallSpeed=0;
                entry.renderer.transform.localPosition=(Vector3)entry.art.offset+Vector3.up*entry.fall;falling.Add(entry);
            }
        }

        // Telekinesis poses of held mines (the local holder's own come directly, SessionMines drops their echo); a release
        // moves the mine onto its support and draws the fall.
        private void Pose(MinePoseMessage message)
        {
            if(message.level!=level || !views.TryGetValue(message.mine,out var entry)) return;
            entry.renderer.transform.parent.localPosition=new Vector3(message.x,message.y,0);
            if(message.released && message.drop>0)
            {
                entry.fall=message.drop;entry.fallSpeed=0;
                entry.renderer.transform.localPosition=(Vector3)entry.art.offset+Vector3.up*entry.fall;
                if(!falling.Contains(entry)) falling.Add(entry);
            }
        }

        private void Remove(string id)
        {
            var entry=views[id];var root=entry.renderer.transform.parent.gameObject;falling.Remove(entry);
            root.SetActive(false);if(Application.isPlaying) Destroy(root);else DestroyImmediate(root);views.Remove(id);
        }
        private void Clear() {foreach(string id in views.Keys.ToArray()) Remove(id);falling.Clear();}
        private void OnDestroy() {if(session!=null) {session.Changed-=Refresh;session.Mines.DisarmHeard-=Disarmed;session.Mines.PlaceHeard-=Placed;session.Mines.PoseHeard-=Pose;}}
    }
}
