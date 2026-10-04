using System;
using System.Collections.Generic;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Presentation
{
    public sealed class MeleeImpactAudio : MonoBehaviour
    {
        [Serializable] private sealed class Entry {public string weapon,clip;public string[] actors,surfaces;public float gain;}
        [Serializable] private sealed class Catalog {public Entry[] entries;}
        private readonly Dictionary<string,(AudioClip clip,float gain)> sounds=new();
        private readonly HashSet<string> weapons=new(),seen=new(),missing=new();
        private readonly Queue<string> recent=new();
        private CombatController combat;
        private GameplayAudio playback;
        public int Played {get;private set;}
        public int ActorPlayed {get;private set;}
        public int SurfacePlayed {get;private set;}
        public void Initialize(CombatController combat,GameplayAudio playback)
        {
            this.combat=combat;this.playback=playback;
            var asset=Resources.Load<TextAsset>("Presentation/melee-impact-audio");
            if(asset==null) throw new InvalidOperationException("Нет каталога Presentation/melee-impact-audio.");
            var catalog=JsonUtility.FromJson<Catalog>(asset.text);
            if(catalog?.entries==null) throw new InvalidOperationException("Нет записей звуков контакта ближнего оружия.");
            foreach(var entry in catalog.entries)
            {
                if(string.IsNullOrEmpty(entry.weapon) || string.IsNullOrEmpty(entry.clip) || (entry.actors?.Length??0)+(entry.surfaces?.Length??0)==0 ||
                    !float.IsFinite(entry.gain) || entry.gain<=0 || entry.gain>1)
                    throw new InvalidOperationException($"Некорректный звук контакта: weapon={entry.weapon}, clip={entry.clip}.");
                var clip=Resources.Load<AudioClip>(entry.clip);
                if(clip==null || clip.samples<=0) throw new InvalidOperationException("Нет звука контакта: "+entry.clip);
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
            }
            combat.ContactObserved+=Contact;
            combat.SurfaceContactObserved+=Surface;
        }
        private void Contact(AttackEvent attack,ActorState actor)
            =>Play(attack,"actor:"+actor.actorType,"actor:"+actor.id,attack.End);
        private void Surface(AttackEvent attack,string texture,Vector2 position)
            =>Play(attack,"surface:"+texture,"surface:"+texture,position);
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
                if(kind.StartsWith("actor:",StringComparison.Ordinal)) ActorPlayed++;else SurfacePlayed++;
            }
        }
        private void OnDestroy() {if(combat!=null) {combat.ContactObserved-=Contact;combat.SurfaceContactObserved-=Surface;}}
    }
}
