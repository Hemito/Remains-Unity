using System;
using System.Collections.Generic;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Presentation
{
    public sealed class MeleeImpactAudio : MonoBehaviour
    {
        [Serializable] private sealed class Entry {public string weapon,clip;public string[] actors;public float gain;}
        [Serializable] private sealed class Catalog {public Entry[] entries;}
        private readonly Dictionary<string,(AudioClip clip,float gain)> sounds=new();
        private readonly HashSet<string> weapons=new(),seen=new(),missing=new();
        private readonly Queue<string> recent=new();
        private CombatController combat;
        private GameplayAudio playback;
        public int Played {get;private set;}
        public void Initialize(CombatController combat,GameplayAudio playback)
        {
            this.combat=combat;this.playback=playback;
            var asset=Resources.Load<TextAsset>("Presentation/melee-impact-audio");
            if(asset==null) throw new InvalidOperationException("Нет каталога Presentation/melee-impact-audio.");
            var catalog=JsonUtility.FromJson<Catalog>(asset.text);
            if(catalog?.entries==null) throw new InvalidOperationException("Нет записей звуков контакта ближнего оружия.");
            foreach(var entry in catalog.entries)
            {
                if(string.IsNullOrEmpty(entry.weapon) || string.IsNullOrEmpty(entry.clip) || entry.actors==null || entry.actors.Length==0 ||
                    !float.IsFinite(entry.gain) || entry.gain<=0 || entry.gain>1)
                    throw new InvalidOperationException($"Некорректный звук контакта: weapon={entry.weapon}, clip={entry.clip}.");
                var clip=Resources.Load<AudioClip>(entry.clip);
                if(clip==null || clip.samples<=0) throw new InvalidOperationException("Нет звука контакта: "+entry.clip);
                weapons.Add(entry.weapon);
                foreach(var actor in entry.actors)
                {
                    if(string.IsNullOrEmpty(actor)) throw new InvalidOperationException("Пустой тип цели звука: weapon="+entry.weapon);
                    sounds.Add(entry.weapon+":"+actor,(clip,entry.gain));
                }
            }
            combat.ContactObserved+=Contact;
        }
        private void Contact(AttackEvent attack,ActorState actor)
        {
            if(!weapons.Contains(attack.weapon)) return;
            string key=attack.weapon+":"+actor.actorType;
            if(!sounds.TryGetValue(key,out var sound))
            {
                if(missing.Add(key)) Debug.LogWarning($"Нет звука контакта: {key}, цель={actor.id}, атака={attack.id}, уровень={attack.level}.",this);
                return;
            }
            string receipt=attack.id+":"+actor.id;
            if(!seen.Add(receipt)) return;
            recent.Enqueue(receipt);if(recent.Count>512) seen.Remove(recent.Dequeue());
            if(playback.PlayContact(sound.clip,attack.level,attack.End,sound.gain)) Played++;
        }
        private void OnDestroy() {if(combat!=null) combat.ContactObserved-=Contact;}
    }
}
