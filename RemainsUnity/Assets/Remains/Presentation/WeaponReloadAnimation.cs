using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using UnityEngine;

namespace Remains.Presentation
{
    // Presentation reads actual reload progress; it never completes reloads or changes ammunition.
    public sealed class WeaponReloadAnimation : MonoBehaviour
    {
        public readonly struct Status
        {
            public readonly string Token,Weapon,Item,Owner;
            public readonly long Selection;
            public readonly int Frame,FrameCount,Generation,Lifecycle;
            public readonly float Progress;
            public Status(string token,string weapon,string item,string owner,long selection,int generation,int lifecycle,int frame,int count,float progress)
            {Token=token;Weapon=weapon;Item=item;Owner=owner;Selection=selection;Generation=generation;Lifecycle=lifecycle;Frame=frame;FrameCount=count;Progress=progress;}
        }
        private sealed class Playing
        {public ReloadSoundMessage message;public string owner,world;public WeaponReloadClip clip;public float duration,elapsed;}
        private CoopSession session;
        private WeaponReloadController localReload;
        private readonly Dictionary<string,WeaponReloadClip> clips=new(StringComparer.Ordinal);
        private readonly Dictionary<string,Playing> playing=new(StringComparer.Ordinal);
        private readonly ReloadSoundGate gate=new();
        private readonly List<string> expired=new();
        private readonly HashSet<string> warnings=new(StringComparer.Ordinal);
        public void Initialize(CoopSession value,WeaponReloadController reload)
        {
            if(session!=null) throw new InvalidOperationException("Изображение перезарядки уже инициализировано.");
            session=value??throw new ArgumentNullException(nameof(value));localReload=reload;
            var catalog=Resources.Load<WeaponAssetCatalog>("Content/WeaponCatalog");
            if(catalog==null) throw new InvalidOperationException("Нет Content/WeaponCatalog для изображения перезарядки.");
            foreach(var weapon in catalog.ReloadAnimatedWeapons()) clips.Add(weapon.Id,weapon.ReloadAnimation);
            session.ReloadAudio.Sent+=Observe;
            session.ReloadAudio.Received+=Observe;
        }
        public void Observe(ReloadSoundMessage message)
        {
            if(!message.started)
            {
                if(gate.Accept(message.actor,message.token,false) && playing.TryGetValue(message.actor,out var current) && current.message.token==message.token)
                    playing.Remove(message.actor);
                return;
            }
            var actor=session.World?.Find(message.actor);
            if(actor==null || !actor.player || !SessionReloadAudio.Matches(actor,message) || !Visible(actor)) return;
            if(actor.id==session.LocalActorId && (localReload==null || !localReload.Active))
            {Warn("controller:"+actor.id,$"Нет действительной локальной перезарядки: actor={actor.id}, item={message.weaponItem}, token={message.token}.");return;}
            if(!clips.TryGetValue(message.weapon,out var clip))
            {Warn("clip:"+message.weapon,$"Нет анимации перезарядки: actor={actor.id}, weapon={message.weapon}, item={message.weaponItem}, token={message.token}. Кадры другого оружия не подставлены.");return;}
            if(!gate.Accept(message.actor,message.token,true)) return;
            playing[actor.id]=new Playing {message=message,clip=clip,owner=actor.owner,world=session.World.worldId,duration=Duration(message.weapon)};
        }
        private static float Duration(string weapon)
        {
            float value=WeaponCatalog.Get(weapon).reloadSeconds;
            if(!float.IsFinite(value) || value<=0) throw new InvalidOperationException($"Нет положительного времени перезарядки: weapon={weapon}, seconds={value}.");
            return value;
        }
        private bool Visible(ActorState actor)=>ActorPresence.IsPresent(actor) && !ActorSessionRules.IsPassive(actor) &&
            actor.level==session.LocalActor?.level && !HeldItemSelection.HasSelection(actor) && (!actor.player || actor.connected);
        private bool Matches(ActorState actor,Playing value)=>actor!=null && Visible(actor) && actor.owner==value.owner &&
            session.World?.worldId==value.world && SessionReloadAudio.Matches(actor,value.message) &&
            (actor.id!=session.LocalActorId || localReload?.Active==true);
        public bool TryGet(ActorState actor,out Status status)
        {
            status=default;
            if(actor==null || !Visible(actor)) return false;
            if(!actor.player)
            {
                var action=actor.npcWeaponAction;
                if(!actor.attackBusy || action?.kind!=NpcWeaponActions.Reload || !NpcWeaponActions.StillSelected(actor,action)) return false;
                if(!clips.TryGetValue(action.weapon,out var clip))
                {Warn("npc:"+actor.id+":"+action.weapon,$"Нет анимации перезарядки NPC: actor={actor.id}, weapon={action.weapon}, action={actor.attackId}.");return false;}
                float progress=Progress(Duration(action.weapon),actor.attackRemaining,actor.id);
                status=new Status(actor.attackId,action.weapon,action.item,actor.owner,action.selectionRevision,actor.generation,actor.lifecycleSequence,
                    clip.FrameIndex(progress),clip.FrameCount,progress);return true;
            }
            if(!playing.TryGetValue(actor.id,out var value) || !Matches(actor,value)) return false;
            float remaining=actor.id==session.LocalActorId?localReload.Remaining:value.duration-value.elapsed;
            float current=Progress(value.duration,remaining,value.message.token);
            status=new Status(value.message.token,value.message.weapon,value.message.weaponItem,value.owner,value.message.selectionRevision,
                value.message.generation,value.message.lifecycle,value.clip.FrameIndex(current),value.clip.FrameCount,current);return true;
        }
        public static float Progress(float duration,float remaining,string context)
        {
            if(!float.IsFinite(duration) || duration<=0 || !float.IsFinite(remaining))
                throw new InvalidOperationException($"Некорректное время изображения перезарядки: {context}, duration={duration}, remaining={remaining}.");
            return Mathf.Clamp01(1-remaining/duration);
        }
        public Sprite Select(ActorState actor,Sprite rest)=>TryGet(actor,out var status)?clips[status.Weapon].Frame(status.Frame):rest;
        public void Step(float dt)
        {
            if(!float.IsFinite(dt) || dt<0) throw new ArgumentOutOfRangeException(nameof(dt),"Некорректный шаг изображения перезарядки.");
            expired.Clear();
            foreach(var pair in playing)
            {
                var actor=session.World?.Find(pair.Key);var value=pair.Value;
                if(!Matches(actor,value)) {expired.Add(pair.Key);continue;}
                if(actor.id!=session.LocalActorId && !session.Pause.IsPaused(actor.level)) value.elapsed=Mathf.Min(value.duration,value.elapsed+dt);
            }
            foreach(string id in expired) playing.Remove(id);
        }
        private void Warn(string key,string reason) {if(warnings.Add(key)) Debug.LogWarning(reason,this);}
        private void OnDestroy()
        {
            if(session!=null) {session.ReloadAudio.Sent-=Observe;session.ReloadAudio.Received-=Observe;}
            playing.Clear();gate.Clear();
        }
    }
}
