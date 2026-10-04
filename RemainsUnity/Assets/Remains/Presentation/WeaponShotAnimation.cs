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
        private sealed class Clip {public string weapon;public float fps;public Sprite[] sprites;}
        private sealed class Playing {public Clip clip;public string level;public int generation,lifecycle;public float elapsed;}
        private CoopSession session;
        private readonly Dictionary<string,Clip> byAttack=new();
        private readonly Dictionary<string,Playing> playing=new();
        private readonly HashSet<string> seen=new();
        private readonly Queue<string> recent=new();
        private readonly List<string> expired=new();
        public void Initialize(CoopSession value)
        {
            session=value;WeaponRigs.Load();
            var catalog=Resources.Load<WeaponAssetCatalog>("Content/WeaponCatalog");
            if(catalog==null) throw new InvalidOperationException("Нет Content/WeaponCatalog для анимаций оружия.");
            foreach(var weapon in catalog.AnimatedWeapons())
            {
                var animation=weapon.ShotAnimation;
                var clip=new Clip {weapon=weapon.Presentation.Id,fps=animation.FramesPerSecond,sprites=animation.CopyFrames()};
                byAttack.Add(weapon.Id,clip);
            }
            session.AttackReceived+=Observe;
        }
        public void Observe(AttackEvent attack)
        {
            if(attack.level!=session.LocalActor?.level || !byAttack.TryGetValue(attack.weapon,out var clip)) return;
            var actor=session.World?.Find(attack.attacker);
            if(actor==null || ActorSessionRules.IsPassive(actor) || actor.weapon!=clip.weapon || actor.level!=attack.level || !ActorPresence.IsPresent(actor)) return;
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
        private bool Matches(ActorState actor,Playing effect)=>!ActorSessionRules.IsPassive(actor) && ActorPresence.IsPresent(actor) && actor.level==session.LocalActor?.level &&
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
        }
    }
}

