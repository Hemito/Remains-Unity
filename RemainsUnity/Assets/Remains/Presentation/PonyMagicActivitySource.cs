using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Presentation
{
    public enum PonyMagicAction { None, SelectedItem, Telekinesis }
    public readonly struct PonyMagicActivity
    {
        public readonly float Opacity,MassKg;
        public readonly PonyMagicAction Action;
        public readonly string SourceId;
        public PonyMagicActivity(float opacity,PonyMagicAction action,string sourceId=null,float massKg=0)
        {
            if(!float.IsFinite(opacity) || opacity<0 || opacity>1 || !float.IsFinite(massKg) || massKg<0)
                throw new ArgumentException("Некорректное состояние свечения рога.");
            Opacity=opacity;Action=action;SourceId=sourceId;MassKg=massKg;
        }
    }
    public interface IPonyMagicActivitySource { PonyMagicActivity Get(ActorState actor); }

    // Event-built presentation only. No inventory lookup, world scan or content
    // resolution in DrawPose. It neither grants holding nor changes simulation.
    public sealed class PonyMagicActivitySource : IPonyMagicActivitySource
    {
        private sealed class Entry
        {
            public string owner,level,weaponItem,heldItem;
            public int generation,lifecycle;
            public long selection;
            public PonyMagicActivity selected,held;
            public PropState prop;
            public HeldPropColor propContext;
            public ActorState body;
            public int bodyGeneration,bodyHoldEpoch;
            public string bodyToken;
            public bool invalidHold;
        }
        private readonly Dictionary<string,Entry> actors=new(StringComparer.Ordinal);
        private readonly HashSet<string> retained=new(StringComparer.Ordinal);
        private readonly List<string> removed=new();
        private readonly HashSet<string> warnings=new(StringComparer.Ordinal);
        private readonly float fullIntensityMassKg;
        public string WorldId {get;private set;}
        public PonyMagicActivitySource(float fullIntensityMassKg=50)
        {
            if(!float.IsFinite(fullIntensityMassKg) || fullIntensityMassKg<=0)
                throw new ArgumentOutOfRangeException(nameof(fullIntensityMassKg));
            this.fullIntensityMassKg=fullIntensityMassKg;
        }
        public void Rebuild(WorldState world,HeldPropColorSource colors)
        {
            if(WorldId!=world?.worldId) actors.Clear();WorldId=world?.worldId;
            retained.Clear();removed.Clear();
            if(world==null) {actors.Clear();return;}
            if(string.IsNullOrWhiteSpace(WorldId) || colors==null || colors.WorldId!=WorldId)
                throw new InvalidOperationException("Свечение рога требует тот же подтверждённый мир и источник держателей.");
            foreach(var actor in world.actors)
            {
                if(string.IsNullOrWhiteSpace(actor.id) || !retained.Add(actor.id))
                    throw new InvalidOperationException("Неоднозначный ID актёра при подготовке свечения рога: "+actor.id);
                if(!actors.TryGetValue(actor.id,out var entry)) {entry=new Entry();actors.Add(actor.id,entry);}
                entry.owner=actor.owner;entry.level=actor.level;entry.generation=actor.generation;entry.lifecycle=actor.lifecycleSequence;
                entry.weaponItem=actor.equippedWeaponItem;entry.heldItem=actor.heldItemId;entry.selection=actor.weaponSelectionRevision;
                entry.selected=entry.held=default;entry.prop=null;entry.body=null;entry.invalidHold=false;
                if(!Active(actor)) continue;
                // Ordinary selection shadows a previous weapon even if missing
                // or fully reserved. It is never a second equipped item.
                var item=HeldItemSelection.HasSelection(actor)?HeldItemSelection.Selected(actor):WeaponInventory.Selected(actor);
                if(item!=null && InventoryAvailability.Count(actor,item)>0 &&
                    (HeldItemSelection.HasSelection(actor) || WeaponInventory.Available(actor,item.definition)))
                    entry.selected=new PonyMagicActivity(.5f,PonyMagicAction.SelectedItem,item.id);
            }
            foreach(var id in actors.Keys) if(!retained.Contains(id)) removed.Add(id);
            foreach(var id in removed) actors.Remove(id);
            foreach(var prop in world.props) PrepareProp(prop,colors);
            foreach(var container in world.containers) PrepareProp(container,colors);
            foreach(var body in world.actors)
            {
                if(!ActorHoldRules.Active(body)) continue;
                var holder=world.Find(body.hold.holder);
                if(!ActiveHolder(holder) || !ActorPresence.IsPresent(body) || holder.level!=body.level ||
                    !actors.TryGetValue(holder.id,out var entry))
                {Warn("body:"+body.id,"Недействительный держатель тела: body="+body.id+", holder="+body.hold.holder);continue;}
                float mass=ActorTypes.Get(body.actorType).bodyMassKg;
                if(!ValidMass(mass,"body="+body.id)) {entry.invalidHold=true;continue;}
                if(!BeginHold(entry,holder.id,body.id,mass)) continue;
                entry.body=body;entry.bodyGeneration=body.generation;entry.bodyHoldEpoch=body.holdEpoch;entry.bodyToken=body.hold.token;
            }
        }
        private void PrepareProp(PropState prop,HeldPropColorSource colors)
        {
            if(!prop.held) return;
            HeldPropColor context=default;string reason=null;
            if(!prop.hasPhysicalBody || !prop.movable || !colors.TryResolve(prop,out context,out reason))
            {Warn("prop:"+prop.id,"Недействительное удержание предмета: prop="+prop.id+", owner="+prop.owner+", level="+prop.level+", reason="+(prop.hasPhysicalBody && prop.movable?reason:"нет переносимого физического тела"));return;}
            if(!actors.TryGetValue(context.HolderId,out var entry))
                throw new InvalidOperationException("Подтверждённый держатель отсутствует в мире: "+context.HolderId);
            if(!ValidMass(prop.mass,"prop="+prop.id)) {entry.invalidHold=true;return;}
            if(!BeginHold(entry,context.HolderId,prop.id,prop.mass)) return;
            entry.prop=prop;entry.propContext=context;
        }
        private bool BeginHold(Entry entry,string holder,string id,float mass)
        {
            if(entry.invalidHold || entry.held.Action==PonyMagicAction.Telekinesis)
            {
                entry.invalidHold=true;entry.held=default;entry.prop=null;entry.body=null;
                Warn("multiple:"+holder,"У одного актёра несколько подтверждённых удержаний: holder="+holder+", source="+id);
                return false;
            }
            entry.held=new PonyMagicActivity(mass>=fullIntensityMassKg?1:.5f,PonyMagicAction.Telekinesis,id,mass);return true;
        }
        public PonyMagicActivity Get(ActorState actor)
        {
            if(!Active(actor)) return default;
            if(!actors.TryGetValue(actor.id,out var entry) || entry.owner!=actor.owner || entry.level!=actor.level ||
                entry.generation!=actor.generation || entry.lifecycle!=actor.lifecycleSequence)
            {Warn("epoch:"+actor.id,"Нет текущего контекста свечения рога: actor="+actor.id+", level="+actor.level+", generation="+actor.generation+", lifecycle="+actor.lifecycleSequence);return default;}
            if(entry.invalidHold) return default;
            // Release/down must take effect even on an unchanged animation frame.
            bool propHeld=entry.prop!=null && entry.propContext.Matches(WorldId,entry.prop);
            bool bodyHeld=entry.body!=null && ActorPresence.IsPresent(entry.body) && ActorHoldRules.Active(entry.body) &&
                entry.body.generation==entry.bodyGeneration && entry.body.holdEpoch==entry.bodyHoldEpoch &&
                entry.body.hold.holder==actor.id && entry.body.hold.token==entry.bodyToken && entry.body.level==actor.level;
            if(entry.prop?.held==true && !propHeld || entry.body?.hold?.holder==actor.id && ActorHoldRules.Active(entry.body) && !bodyHeld)
            {Warn("hold-epoch:"+actor.id,"Удержание изменено без подготовки свечения рога: actor="+actor.id+", source="+entry.held.SourceId);return default;}
            if(propHeld || bodyHeld) return entry.held;
            if(entry.selection!=actor.weaponSelectionRevision || entry.weaponItem!=actor.equippedWeaponItem || entry.heldItem!=actor.heldItemId)
            {Warn("selection:"+actor.id,"Выбор вещи изменён без подготовки свечения рога: actor="+actor.id+", revision="+actor.weaponSelectionRevision);return default;}
            return entry.selected;
        }
        private static bool Active(ActorState actor)=>ActorPresence.IsPresent(actor) && !ActorSessionRules.IsPassive(actor) && actor.health>0;
        private static bool ActiveHolder(ActorState actor)=>Active(actor) && actor.player && actor.connected && !string.IsNullOrWhiteSpace(actor.owner);
        private bool ValidMass(float mass,string context)
        {
            if(float.IsFinite(mass) && mass>0) return true;
            Warn("mass:"+context,"Неверная масса удержания: "+context+", mass="+mass);return false;
        }
        private void Warn(string key,string reason)
        {if(warnings.Add(WorldId+":"+key+":"+reason)) Debug.LogWarning("Свечение рога скрыто: world="+WorldId+"; "+reason+".");}
    }
}
