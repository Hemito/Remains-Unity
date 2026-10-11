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
        private sealed class Clip {public string weapon,ammo;public float fps,gain;public Sprite[] sprites,masks;public Vector2 offset;}
        private sealed class Playing {public Clip clip;public string shot,world,level,owner,item;public long selection;public int generation,lifecycle;public float elapsed;}
        private CoopSession session;
        private string world,level;
        private readonly Dictionary<string,Clip> byAttack=new();
        private readonly Dictionary<string,Playing> playing=new();
        private readonly HashSet<string> seen=new();
        private readonly List<string> expired=new();
        private readonly HashSet<string> warnings=new();
        public void Initialize(CoopSession value)
        {
            if(session!=null)throw new InvalidOperationException("Кадры выстрела уже инициализированы.");
            session=value??throw new ArgumentNullException(nameof(value));WeaponRigs.Load();
            var catalog=Resources.Load<WeaponAssetCatalog>("Content/WeaponCatalog");
            if(catalog==null) throw new InvalidOperationException("Нет Content/WeaponCatalog для анимаций оружия.");
            foreach(var weapon in catalog.AnimatedWeapons())
            {
                var animation=weapon.ShotAnimation;
                var clip=new Clip {weapon=weapon.Id,ammo=weapon.CreateDefinition().ammo,gain=weapon.MuzzleFlashGain,
                    fps=animation.FramesPerSecond,sprites=animation.CopyFrames(),masks=animation.EmissionFrames,offset=animation.EmissionOffset};
                byAttack.Add(weapon.Id,clip);
            }
            var lightCatalog=Resources.Load<MuzzleFlashCatalog>("Content/MuzzleFlashCatalog");
            if(lightCatalog==null) throw new InvalidOperationException("Нет Content/MuzzleFlashCatalog для света выстрелов.");
            var lightProfiles=lightCatalog.Read();
            foreach(var clip in byAttack.Values)
            {
                if(clip.masks==null || clip.masks.Length==0) continue;
                if(string.IsNullOrEmpty(clip.ammo) || !lightProfiles.TryGetValue(clip.ammo,out float gain))
                {Debug.LogWarning($"Свет вспышки пропущен: оружие={clip.weapon}, патрон={clip.ammo ?? "<нет>"}, профиль патрона отсутствует.",this);clip.gain=0;}
                else clip.gain*=gain;
            }
            session.AttackReceived+=Observe;session.Projectiles.Rejected+=Rejected;session.Changed+=Reconcile;session.SessionStopped+=Stopped;Reconcile();
        }
        public void Observe(AttackEvent attack)
        {
            if(attack==null || NpcNaturalAttack.IsNatural(attack)) return;
            Reconcile();
            if(attack.level!=session.LocalActor?.level || !byAttack.TryGetValue(attack.weapon,out var clip)) return;
            if(attack.rangedPose==null)
            {
                if(warnings.Add(attack.weapon))Debug.LogWarning($"Кадры выстрела пропущены: нет captured pose, shot={attack.id}, actor={attack.attacker}, weapon={attack.weapon}, level={attack.level}. Legacy synthetic fixture должна явно задать исторический контекст.",this);
                return;
            }
            attack.rangedPose.Validate("кадры выстрела "+attack.id);
            var actor=session.World?.Find(attack.attacker);
            var pose=attack.rangedPose;
            var effect=new Playing {clip=clip,shot=attack.id,world=pose.worldId,level=attack.level,owner=attack.sourceOwner,item=pose.weaponItem,
                selection=pose.selectionRevision,generation=pose.actorGeneration,lifecycle=pose.actorLifecycle};
            if(string.IsNullOrWhiteSpace(attack.id) || string.IsNullOrWhiteSpace(attack.sourceOwner) || actor==null || !Matches(actor,effect))return;
            if(!seen.Add(attack.id)) return;
            playing[actor.id]=effect;
        }
        public Sprite Select(ActorState actor,Sprite rest)
        {
            if(actor==null || !playing.TryGetValue(actor.id,out var effect) || !Matches(actor,effect)) return rest;
            int frame=Mathf.FloorToInt(effect.elapsed*effect.clip.fps);
            return frame<effect.clip.sprites.Length?effect.clip.sprites[frame]:rest;
        }
        // Shares the exact selection/time/lifecycle gate with the visible shot, including rejection and pause.
        public bool TryEmission(ActorState actor,Sprite drawn,Matrix4x4 weaponMatrix,out RoomRadianceField.SpriteEmission emission)
        {
            emission=default;
            if(actor==null || !playing.TryGetValue(actor.id,out var effect) || !Matches(actor,effect))return false;
            var clip=effect.clip;int frame=Mathf.FloorToInt(effect.elapsed*clip.fps);
            if(clip.gain<=0 || frame>=clip.sprites.Length || drawn!=clip.sprites[frame] || clip.masks==null || frame>=clip.masks.Length || clip.masks[frame]==null)return false;
            emission=new RoomRadianceField.SpriteEmission(clip.masks[frame],weaponMatrix*Matrix4x4.Translate(clip.offset),clip.gain);
            return true;
        }
        private bool Matches(ActorState actor,Playing effect)=>!ActorSessionRules.IsPassive(actor) && ActorPresence.IsPresent(actor) && actor.level==session.LocalActor?.level &&
            !HeldItemSelection.HasSelection(actor) && session.World?.worldId==effect.world && actor.level==effect.level && actor.weapon==effect.clip.weapon &&
            actor.owner==effect.owner && actor.equippedWeaponItem==effect.item && actor.weaponSelectionRevision==effect.selection &&
            actor.generation==effect.generation && actor.lifecycleSequence==effect.lifecycle;
        public void Step(float dt)
        {
            if(!float.IsFinite(dt) || dt<0)throw new ArgumentOutOfRangeException(nameof(dt),"Некорректный шаг кадров выстрела.");
            Reconcile();
            expired.Clear();
            foreach(var pair in playing)
            {
                var actor=session.World?.Find(pair.Key);var effect=pair.Value;
                if(actor==null || !Matches(actor,effect)) {expired.Add(pair.Key);continue;}
                if(!session.Pause.IsPaused(effect.level)) effect.elapsed+=dt;
                if(effect.elapsed>=effect.clip.sprites.Length/effect.clip.fps) expired.Add(pair.Key);
            }
            foreach(string id in expired) playing.Remove(id);
        }
        private void Reconcile()
        {
            string nextWorld=session.World?.worldId,nextLevel=session.LocalActor?.level;
            if(world==nextWorld && level==nextLevel)return;
            playing.Clear();if(world!=nextWorld){seen.Clear();warnings.Clear();}world=nextWorld;level=nextLevel;
        }
        private void Stopped(){playing.Clear();seen.Clear();warnings.Clear();world=level=null;}
        private void Rejected(string id)
        {
            if(string.IsNullOrWhiteSpace(id))return;seen.Add(id);expired.Clear();
            foreach(var pair in playing)if(pair.Value.shot==id)expired.Add(pair.Key);
            foreach(string actor in expired)playing.Remove(actor);
        }
        private void OnDestroy()
        {
            if(session!=null){session.AttackReceived-=Observe;session.Projectiles.Rejected-=Rejected;session.Changed-=Reconcile;session.SessionStopped-=Stopped;}
        }
    }
}

