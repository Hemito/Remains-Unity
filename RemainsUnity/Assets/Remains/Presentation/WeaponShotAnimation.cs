using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;

namespace Remains.Presentation
{
    // Original shot frames share the resting sprite's authored origin. No gameplay pose changes.
    public sealed class WeaponShotAnimation : MonoBehaviour
    {
        [Serializable] private sealed class Clip {public string weapon;public string[] attacks,frames;public float fps;[NonSerialized] public Sprite[] sprites;}
        [Serializable] private sealed class Catalog {public Clip[] clips;}
        private sealed class Playing {public Clip clip;public string level;public int generation,lifecycle;public float elapsed;}
        private CoopSession session;
        private readonly Dictionary<string,Clip> byAttack=new();
        private readonly Dictionary<string,Playing> playing=new();
        private readonly HashSet<string> seen=new();
        private readonly Queue<string> recent=new();
        private readonly List<string> expired=new();
        private readonly List<Sprite> owned=new();
        public void Initialize(CoopSession value)
        {
            session=value;WeaponRigs.Load();
            var asset=Resources.Load<TextAsset>("Original/Weapons/shot-animation");
            if(asset==null) throw new InvalidOperationException("Нет Original/Weapons/shot-animation.");
            var catalog=JsonUtility.FromJson<Catalog>(asset.text);
            if(catalog?.clips==null) throw new InvalidOperationException("Нет анимаций выстрела оружия.");
            foreach(var clip in catalog.clips)
            {
                if(clip.frames==null || clip.frames.Length==0 || clip.attacks==null || clip.attacks.Length==0 || !float.IsFinite(clip.fps) || clip.fps<=0)
                    throw new InvalidOperationException("Некорректная анимация выстрела: "+clip.weapon);
                var rig=WeaponRigs.Get(clip.weapon);clip.sprites=new Sprite[clip.frames.Length];
                for(int i=0;i<clip.frames.Length;i++)
                {
                    var texture=Resources.Load<Texture2D>(clip.frames[i]);
                    if(texture==null) throw new InvalidOperationException("Нет кадра выстрела: "+clip.frames[i]);
                    var pivot=new Vector2((rig.x+rig.width*rig.pivotX)/texture.width,(rig.y+rig.height*rig.pivotY)/texture.height);
                    var sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),pivot,WeaponRigs.PixelsPerUnit);
                    clip.sprites[i]=sprite;owned.Add(sprite);SpriteArtworkMaterial.Prepare(sprite);
                }
                foreach(string attack in clip.attacks) byAttack.Add(attack,clip);
            }
            session.AttackReceived+=Observe;
        }
        public void Observe(AttackEvent attack)
        {
            if(attack.level!=session.LocalActor?.level || !byAttack.TryGetValue(attack.weapon,out var clip)) return;
            var actor=session.World?.Find(attack.attacker);
            if(actor==null || actor.down || actor.weapon!=clip.weapon || actor.level!=attack.level || !ActorPresence.IsPresent(actor)) return;
            if(!seen.Add(attack.id)) return;
            recent.Enqueue(attack.id);if(recent.Count>512) seen.Remove(recent.Dequeue());
            playing[actor.id]=new Playing {clip=clip,level=actor.level,generation=actor.generation,lifecycle=actor.lifecycleSequence};
        }
        public Sprite Select(ActorState actor,Sprite rest)
        {
            if(!playing.TryGetValue(actor.id,out var effect) || !Matches(actor,effect)) return rest;
            int frame=Mathf.FloorToInt(effect.elapsed*effect.clip.fps);
            return frame<effect.clip.sprites.Length?effect.clip.sprites[frame]:rest;
        }
        private bool Matches(ActorState actor,Playing effect)=>!actor.down && ActorPresence.IsPresent(actor) && actor.level==session.LocalActor?.level &&
            actor.level==effect.level && actor.weapon==effect.clip.weapon && actor.generation==effect.generation && actor.lifecycleSequence==effect.lifecycle;
        public void Step(float dt)
        {
            expired.Clear();
            foreach(var pair in playing)
            {
                var actor=session.World?.Find(pair.Key);var effect=pair.Value;
                if(actor==null || !Matches(actor,effect)) {expired.Add(pair.Key);continue;}
                if(!session.Pause.IsPaused(effect.level)) effect.elapsed+=Mathf.Max(0,dt);
                if(effect.elapsed>=effect.clip.sprites.Length/effect.clip.fps) expired.Add(pair.Key);
            }
            foreach(string id in expired) playing.Remove(id);
        }
        private void OnDestroy()
        {
            if(session!=null) session.AttackReceived-=Observe;
            foreach(var sprite in owned) {if(Application.isPlaying) Destroy(sprite);else DestroyImmediate(sprite);}
        }
    }
}
