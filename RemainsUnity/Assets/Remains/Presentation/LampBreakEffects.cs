using System;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;
using Remains.Content;

namespace Remains.Presentation
{
    public sealed class LampBreakEffects : MonoBehaviour
    {
        private CoopSession session;
        private GameplayAudio audio;
        private WorldVisualOrderTable orders;
        private LampBreakEffectView prefab;
        public void Initialize(CoopSession value,GameplayAudio playback,WorldVisualOrderTable orderTable)
        {
            session=value;audio=playback;orders=orderTable;
            prefab=GameAssets.Load<LampBreakEffectView>("Presentation/Effects/lamp-break");
            if(prefab==null) throw new InvalidOperationException("Нет авторского эффекта Presentation/Effects/lamp-break.");
            prefab.Validate();session.Lamps.Broken+=Broken;
        }
        private void Broken(LampState lamp)
        {
            if(session.LocalActor?.level!=lamp.level) return;
            audio.PlayContact(prefab.Sound,lamp.level,lamp.Bounds.center,.5f);
            var view=Instantiate(prefab,transform);
            view.transform.position=lamp.Bounds.center;
            view.gameObject.AddComponent<WorldVisualOrderBinding>().Initialize(orders,WorldVisualKind.Effect,
                "lamp-break:"+lamp.id+":"+lamp.revision,null,view.Particles.GetComponent<ParticleSystemRenderer>(),lamp.level);
            view.Particles.Play();
        }
        private void OnDestroy() {if(session!=null) session.Lamps.Broken-=Broken;}
    }
}
