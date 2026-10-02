using System;
using System.Collections.Generic;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Presentation
{
    public sealed class ProjectileAudio : MonoBehaviour
    {
        [Serializable] private sealed class Entry {public string kind,key,clip;public float gain;}
        [Serializable] private sealed class Catalog {public string[] weapons;public Entry[] entries;}
        private readonly Dictionary<string,(AudioClip clip,float gain)> sounds=new();
        private readonly HashSet<string> weapons=new(),missing=new();
        private ProjectileController controller;
        private GameplayAudio playback;
        public int Played {get;private set;}
        public void Initialize(ProjectileController controller,GameplayAudio playback)
        {
            this.controller=controller;this.playback=playback;
            var asset=Resources.Load<TextAsset>("Presentation/projectile-audio");
            if(asset==null) throw new InvalidOperationException("Нет каталога Presentation/projectile-audio.");
            var catalog=JsonUtility.FromJson<Catalog>(asset.text);
            if(catalog?.entries==null || catalog.weapons==null) throw new InvalidOperationException("Неполный каталог звуков попадания.");
            foreach(string weapon in catalog.weapons) weapons.Add(weapon);
            foreach(var entry in catalog.entries)
            {
                if(entry.kind is not ("actor" or "surface") || string.IsNullOrEmpty(entry.key) ||
                    !float.IsFinite(entry.gain) || entry.gain<=0 || entry.gain>1)
                    throw new InvalidOperationException($"Некорректный звук попадания: {entry.kind}/{entry.key}.");
                var clip=Resources.Load<AudioClip>(entry.clip);
                if(clip==null || clip.samples<=0) throw new InvalidOperationException("Нет звука попадания: "+entry.clip);
                sounds.Add(entry.kind+":"+entry.key,(clip,entry.gain));
            }
            controller.ContactObserved+=Contact;
        }
        private void Contact(ProjectileContact contact)
        {
            if(!weapons.Contains(contact.weapon)) return;
            string key=contact.target!=null?"actor:"+contact.targetType:"surface:"+contact.surfaceTexture;
            if(!sounds.TryGetValue(key,out var sound))
            {
                if(missing.Add(key)) Debug.LogWarning($"Нет звука контакта: {key}, снаряд={contact.projectile}, уровень={contact.level}.",this);
                return;
            }
            if(playback.PlayContact(sound.clip,contact.level,contact.position,sound.gain)) Played++;
        }
        private void OnDestroy() {if(controller!=null) controller.ContactObserved-=Contact;}
    }
}
