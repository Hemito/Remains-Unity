using System;
using System.Collections.Generic;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Content;
using UnityEngine;

namespace Remains.Presentation
{
    public sealed class MeleeImpactAudio : MonoBehaviour
    {
        [Serializable] private sealed class Entry {public string weapon,clip;public string[] actors,surfaces,props,materials;public float gain;}
        [Serializable] private sealed class Catalog {public Entry[] entries;}
        private readonly Dictionary<string,(AudioClip clip,float gain)> sounds=new();
        private readonly HashSet<string> weapons=new(),seen=new(),missing=new();
        private readonly Queue<string> recent=new();
        private CombatController combat;
        private GameplayAudio playback;
        public int Played {get;private set;}
        public int ActorPlayed {get;private set;}
        public int SurfacePlayed {get;private set;}
        public int PropPlayed {get;private set;}
        public void Initialize(CombatController combat,GameplayAudio playback)
        {
            this.combat=combat;this.playback=playback;
            var asset=GameAssets.Load<TextAsset>("Presentation/melee-impact-audio");
            if(asset==null) throw new InvalidOperationException("Нет каталога Presentation/melee-impact-audio.");
            var catalog=JsonUtility.FromJson<Catalog>(asset.text);
            if(catalog?.entries==null) throw new InvalidOperationException("Нет записей звуков контакта ближнего оружия.");
            foreach(var entry in catalog.entries)
            {
                if(string.IsNullOrEmpty(entry.weapon) || string.IsNullOrEmpty(entry.clip) || (entry.actors?.Length??0)+(entry.surfaces?.Length??0)+(entry.props?.Length??0)+(entry.materials?.Length??0)==0 ||
                    !float.IsFinite(entry.gain) || entry.gain<=0 || entry.gain>1)
                    throw new InvalidOperationException($"Некорректный звук контакта: weapon={entry.weapon}, clip={entry.clip}.");
                var clip=AudioClipSource.Load(entry.clip);
                weapons.Add(entry.weapon);
                foreach(var actor in entry.actors??Array.Empty<string>())
                {
                    if(string.IsNullOrEmpty(actor)) throw new InvalidOperationException("Пустой тип цели звука: weapon="+entry.weapon);
                    sounds.Add(entry.weapon+":actor:"+actor,(clip,entry.gain));
                }
                foreach(var surface in entry.surfaces??Array.Empty<string>())
                {
                    if(string.IsNullOrEmpty(surface)) throw new InvalidOperationException("Пустая поверхность звука: weapon="+entry.weapon);
                    sounds.Add(entry.weapon+":surface:"+surface,(clip,entry.gain));
                }
                foreach(var material in entry.materials??Array.Empty<string>())
                {
                    if(string.IsNullOrEmpty(material)) throw new InvalidOperationException("Пустой материал звука: weapon="+entry.weapon);
                    sounds.Add(entry.weapon+":material:"+material,(clip,entry.gain));
                }
                foreach(var prop in entry.props??Array.Empty<string>())
                {
                    if(string.IsNullOrEmpty(prop)) throw new InvalidOperationException("Пустой тип объекта звука: weapon="+entry.weapon);
                    sounds.Add(entry.weapon+":prop:"+prop,(clip,entry.gain));
                }
            }
            combat.ContactObserved+=Contact;
            combat.SurfaceContactObserved+=Surface;
            combat.PropContactObserved+=Prop;
        }
        // A type without its own entry for the weapon sounds by its material (Unit.udarBullet).
        private void Contact(AttackEvent attack,ActorState actor)
            =>Play(attack,!sounds.ContainsKey(attack.weapon+":actor:"+actor.actorType) && ActorTypes.Get(actor.actorType).hitMaterial is {Length:>0} material
                ?"material:"+material:"actor:"+actor.actorType,"actor:"+actor.id,attack.End);
        private void Surface(AttackEvent attack,string texture,Vector2 position)
            =>Play(attack,"surface:"+texture,"surface:"+texture,position);
        private void Prop(AttackEvent attack,string id,string definition,Vector2 position)
            =>Play(attack,"prop:"+definition,"prop:"+id,position);
        private void Play(AttackEvent attack,string kind,string target,Vector2 position)
        {
            if(!weapons.Contains(attack.weapon)) return;
            string key=attack.weapon+":"+kind;
            if(!sounds.TryGetValue(key,out var sound))
            {
                if(missing.Add(key)) Debug.LogWarning($"Нет звука контакта: {key}, цель={target}, атака={attack.id}, уровень={attack.level}.",this);
                return;
            }
            string receipt=attack.id+":"+target;
            if(!seen.Add(receipt)) return;
            recent.Enqueue(receipt);if(recent.Count>512) seen.Remove(recent.Dequeue());
            if(playback.PlayContact(sound.clip,attack.level,position,sound.gain))
            {
                Played++;
                if(kind.StartsWith("actor:",StringComparison.Ordinal)) ActorPlayed++;
                else if(kind.StartsWith("prop:",StringComparison.Ordinal)) PropPlayed++;
                else SurfacePlayed++;
            }
        }
        private void OnDestroy() {if(combat!=null) {combat.ContactObserved-=Contact;combat.SurfaceContactObserved-=Surface;combat.PropContactObserved-=Prop;}}
    }
}
