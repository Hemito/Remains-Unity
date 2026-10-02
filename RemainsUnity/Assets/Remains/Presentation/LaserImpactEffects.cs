using System;
using System.Collections.Generic;
using Remains.Gameplay;
using Remains.Networking;
using UnityEngine;

namespace Remains.Presentation
{
    public sealed class LaserImpactEffects : MonoBehaviour
    {
        private sealed class Effect {public SpriteRenderer renderer;public float age;public string level;}
        private readonly List<Effect> effects=new();
        private Sprite[] frames;
        private Material material;
        private ProjectileController controller;
        private CoopSession session;
        public void Initialize(CoopSession session,ProjectileController controller)
        {
            this.session=session;this.controller=controller;
            var shader=Resources.Load<Shader>("Presentation/ScreenGlow");
            if(shader==null) throw new InvalidOperationException("Нет шейдера Presentation/ScreenGlow.");
            material=new Material(shader);frames=new Sprite[3];
            for(int i=0;i<frames.Length;i++)
            {
                string path="Original/Shots/laser-impact-"+(i+1);
                var texture=Resources.Load<Texture2D>(path);
                if(texture==null) throw new InvalidOperationException("Нет кадра попадания лазера: "+path);
                frames[i]=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),100);
            }
            controller.ContactObserved+=Contact;
        }
        private void Contact(ProjectileContact contact)
        {
            if(contact.weapon!="energy" || session.LocalActor?.level!=contact.level) return;
            var obj=new GameObject("Попадание лазера "+contact.projectile);obj.transform.SetParent(transform,false);
            obj.transform.position=contact.position;
            var renderer=obj.AddComponent<SpriteRenderer>();renderer.sharedMaterial=material;
            renderer.sortingOrder=ActorVisualLayers.EffectOrder;renderer.sprite=frames[0];
            effects.Add(new Effect {renderer=renderer,level=contact.level});
        }
        private void Update()=>Step(Time.deltaTime);
        public void Step(float dt)
        {
            for(int i=effects.Count-1;i>=0;i--)
            {
                var effect=effects[i];
                if(session.LocalActor?.level==effect.level && !session.Pause.IsPaused(effect.level)) effect.age+=Mathf.Max(0,dt);
                int frame=Mathf.FloorToInt(effect.age*30);
                if(session.LocalActor?.level!=effect.level || frame>=frames.Length)
                {Release(effect.renderer.gameObject);effects.RemoveAt(i);}
                else effect.renderer.sprite=frames[frame];
            }
        }
        private void OnDestroy()
        {
            if(controller!=null) controller.ContactObserved-=Contact;
            if(frames!=null) foreach(var frame in frames) if(frame!=null) Release(frame);
            if(material!=null) Release(material);
        }
        private static void Release(UnityEngine.Object value) {if(Application.isPlaying) Destroy(value);else DestroyImmediate(value);}
    }
}
