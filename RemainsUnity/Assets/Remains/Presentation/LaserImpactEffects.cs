using System;
using System.Collections.Generic;
using Remains.Gameplay;
using Remains.Networking;
using UnityEngine;

namespace Remains.Presentation
{
    public sealed class LaserImpactEffects : MonoBehaviour
    {
        private sealed class Effect {public LaserImpactView view;public float age;public string level;}
        private readonly List<Effect> effects=new();
        private LaserImpactView prefab;
        private ProjectileController controller;
        private CoopSession session;
        private WorldVisualOrderTable orders;
        public void Initialize(CoopSession session,ProjectileController controller,WorldVisualOrderTable orderTable)
        {
            this.session=session;this.controller=controller;orders=orderTable??throw new ArgumentNullException(nameof(orderTable));
            prefab=Resources.Load<LaserImpactView>("Presentation/Effects/laser-impact");
            if(prefab==null) throw new InvalidOperationException("Не назначен префаб Presentation/Effects/laser-impact.");
            prefab.Validate();
            controller.ContactObserved+=Contact;
        }
        private void Contact(ProjectileContact contact)
        {
            if(contact.weapon!="energy" || session.LocalActor?.level!=contact.level) return;
            var view=Instantiate(prefab,transform);view.name="Попадание лазера "+contact.projectile;
            view.gameObject.AddComponent<WorldVisualOrderBinding>().Initialize(orders,WorldVisualKind.Effect,
                "laser:"+contact.projectile+":"+(contact.target ?? contact.sourceDoorId ?? contact.surfaceTexture)+":"+contact.position.x.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+":"+contact.position.y.ToString("R",System.Globalization.CultureInfo.InvariantCulture),
                null,view.Artwork,contact.level);
            view.transform.position=contact.position;view.Draw(0);
            effects.Add(new Effect {view=view,level=contact.level});
        }
        private void Update()=>Step(Time.deltaTime);
        public void Step(float dt)
        {
            for(int i=effects.Count-1;i>=0;i--)
            {
                var effect=effects[i];
                if(session.LocalActor?.level==effect.level && !session.Pause.IsPaused(effect.level)) effect.age+=Mathf.Max(0,dt);
                if(session.LocalActor?.level!=effect.level || !effect.view.Draw(effect.age))
                {Release(effect.view.gameObject);effects.RemoveAt(i);}
            }
        }
        private void OnDestroy()
        {
            if(controller!=null) controller.ContactObserved-=Contact;
        }
        private static void Release(UnityEngine.Object value) {if(Application.isPlaying) Destroy(value);else DestroyImmediate(value);}
    }
}
