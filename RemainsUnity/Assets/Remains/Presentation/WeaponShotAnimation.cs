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
        private sealed class Clip {public string weapon,ammo;public float fps,gain;public WeaponShotClip source;public int count;public Sprite[] masks;public Vector2 offset;}
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
            var catalog=GameAssets.Load<WeaponAssetCatalog>("Content/WeaponCatalog");
            if(catalog==null) throw new InvalidOperationException("Нет Content/WeaponCatalog для анимаций оружия.");
            foreach(var weapon in catalog.AnimatedWeapons())
            {
                var animation=weapon.ShotAnimation;
                var clip=new Clip {weapon=weapon.Id,ammo=weapon.CreateDefinition().ammo,gain=weapon.MuzzleFlashGain,
                    fps=animation.FramesPerSecond,source=animation,count=animation.FrameCount,masks=animation.EmissionFrames,offset=animation.EmissionOffset};
                // Authored frames are loaded with the catalog; frames of the export arrive when the weapon is in sight.
                if(!animation.Deferred) for(int i=0;i<clip.count;i++) _=animation.Frame(i);
                byAttack.Add(weapon.Id,clip);
            }
            foreach(var weapon in catalog.ReloadAnimatedWeapons()) reloads.Add(weapon.Id,weapon.ReloadAnimation);
            var lightCatalog=GameAssets.Load<MuzzleFlashCatalog>("Content/MuzzleFlashCatalog");
            if(lightCatalog==null) throw new InvalidOperationException("Нет Content/MuzzleFlashCatalog для света выстрелов.");
            var lightProfiles=lightCatalog.Read();
            foreach(var clip in byAttack.Values)
            {
                if(clip.masks==null || clip.masks.Length==0) continue;
                if(string.IsNullOrEmpty(clip.ammo) || !lightProfiles.TryGetValue(clip.ammo,out float gain))
                {Debug.LogWarning($"Свет вспышки пропущен: оружие={clip.weapon}, патрон={clip.ammo ?? "<нет>"}, профиль патрона отсутствует.",this);clip.gain=0;}
                else clip.gain*=gain;
            }
            session.AttackReceived+=Observe;session.Projectiles.Rejected+=Rejected;session.Changed+=Reconcile;session.Changed+=PreloadInSight;session.SessionStopped+=Stopped;Reconcile();PreloadInSight();
        }
        public void Observe(AttackEvent attack)
        {
            if(attack==null || NpcNaturalAttack.IsNatural(attack) || attack.volley!=null) return;   // the volley's first pellet fires the frames
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
            return frame<effect.clip.count?effect.clip.source.Frame(frame)??rest:rest;
        }
        // Shares the exact selection/time/lifecycle gate with the visible shot, including rejection and pause.
        public bool TryEmission(ActorState actor,Sprite drawn,Matrix4x4 weaponMatrix,out RoomRadianceField.SpriteEmission emission)
        {
            emission=default;
            if(actor==null || !playing.TryGetValue(actor.id,out var effect) || !Matches(actor,effect))return false;
            var clip=effect.clip;int frame=Mathf.FloorToInt(effect.elapsed*clip.fps);
            if(clip.gain<=0 || frame>=clip.count || drawn!=clip.source.Frame(frame) || clip.masks==null || frame>=clip.masks.Length || clip.masks[frame]==null)return false;
            emission=new RoomRadianceField.SpriteEmission(clip.masks[frame],weaponMatrix*Matrix4x4.Translate(clip.offset),clip.gain);
            return true;
        }
        public string EmissionId(ActorState actor)=>actor!=null && playing.TryGetValue(actor.id,out var effect) && Matches(actor,effect)?effect.shot:null;
        // AttackReceived runs after Observe registered the shot. Warm shadow visibility from
        // its captured pose before the first drawn muzzle frame, outside the pose callback.
        public bool TryAttackEmission(AttackEvent attack,out RoomRadianceField.SpriteEmission emission)
        {
            emission=default;
            if(attack?.rangedPose==null) return false;
            var actor=session.World?.Find(attack.attacker);
            if(EmissionId(actor)!=attack.id) return false;
            var pose=attack.rangedPose;
            var matrix=Matrix4x4.TRS(new Vector3(pose.rootX,pose.rootY,0),Quaternion.Euler(0,0,pose.angleDegrees),
                new Vector3(1,pose.mirrored?-1:1,1));
            return TryEmission(actor,Select(actor,null),matrix,out emission);
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
                if(effect.elapsed>=effect.clip.count/effect.clip.fps) expired.Add(pair.Key);
            }
            foreach(string id in expired) playing.Remove(id);
        }
        private void Reconcile()
        {
            string nextWorld=session.World?.worldId,nextLevel=session.LocalActor?.level;
            if(world==nextWorld && level==nextLevel)return;
            playing.Clear();if(world!=nextWorld){seen.Clear();warnings.Clear();}world=nextWorld;level=nextLevel;
        }
        // Weapons in the hooves of the visible level's actors start loading their deferred frames (once per clip).
        private readonly Dictionary<string,WeaponReloadClip> reloads=new(StringComparer.Ordinal);
        private string preloadedWorld,preloadedLevel;
        private void PreloadInSight()
        {
            if(session.World==null || session.LocalActor==null) {preloadedWorld=preloadedLevel=null;return;}
            string visible=session.LocalActor.level;
            bool same=preloadedWorld==session.World.worldId && preloadedLevel==visible;
            preloadedWorld=session.World.worldId;preloadedLevel=visible;
            if(same && !session.LastChange.Any(WorldChangeKind.Actors|WorldChangeKind.Identity)) return;
            if(same && session.LastChangedParts!=null && !session.LastChange.Any(WorldChangeKind.Identity))
            {
                foreach(string part in session.LastChangedParts)
                    if(part.StartsWith("actors:",StringComparison.Ordinal)) PreloadActor(session.World.Find(part.Substring(7)),visible);
                return;
            }
            foreach(var actor in session.World.actors)
                PreloadActor(actor,visible);
        }
        private void PreloadActor(ActorState actor,string visible)
        {
            if(actor==null || actor.level!=visible || string.IsNullOrEmpty(actor.weapon)) return;
            if(byAttack.TryGetValue(actor.weapon,out var clip)) clip.source.Preload();
            if(reloads.TryGetValue(actor.weapon,out var reload)) reload.Preload();
        }
        private void Stopped(){playing.Clear();seen.Clear();warnings.Clear();world=level=preloadedWorld=preloadedLevel=null;}
        private void Rejected(string id)
        {
            if(string.IsNullOrWhiteSpace(id))return;seen.Add(id);expired.Clear();
            foreach(var pair in playing)if(pair.Value.shot==id)expired.Add(pair.Key);
            foreach(string actor in expired)playing.Remove(actor);
        }
        private void OnDestroy()
        {
            if(session!=null){session.AttackReceived-=Observe;session.Projectiles.Rejected-=Rejected;session.Changed-=Reconcile;session.Changed-=PreloadInSight;session.SessionStopped-=Stopped;}
        }
    }
}

