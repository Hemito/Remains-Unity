using System;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;

namespace Remains.Presentation
{
    public sealed class NaturalHitEffects : MonoBehaviour
    {
        private const int Count=7;
        private CoopSession session;
        private DamageFeedback feedback;
        private WorldVisualOrderTable orders;
        private Material material;
        public event Action<DamageResult> Shown;

        public void Initialize(CoopSession session,DamageFeedback feedback,WorldVisualOrderTable orders)
        {
            if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            this.session=session;this.feedback=feedback;this.orders=orders;
            material=WorldArtworkMaterial.Create(Texture2D.whiteTexture);
            material.SetFloat("_Emission",1);
            feedback.Received+=Show;
        }
        private void Show(DamageResult result)
        {
            if(!result.naturalContact || session.LocalActor?.level!=result.level || session.World?.worldId!=result.world) return;
            var target=session.World.Find(result.actor);
            if(target==null || target.generation!=result.generation || target.lifecycleSequence!=result.lifecycleSequence || target.level!=result.level) return;
            var effect=new GameObject("Попадание "+result.Key);effect.SetActive(false);effect.transform.SetParent(transform,false);
            effect.transform.position=new Vector2(result.x,result.y+Anatomy.Movement(target).height*.55f);
            var particles=effect.AddComponent<ParticleSystem>();
            var main=particles.main;
            main.playOnAwake=false;main.loop=false;main.duration=.26f;main.maxParticles=Count;
            main.startLifetime=.23f;main.stopAction=ParticleSystemStopAction.Destroy;
            var emission=particles.emission;emission.enabled=false;
            var renderer=effect.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;
            effect.AddComponent<WorldVisualOrderBinding>().Initialize(orders,WorldVisualKind.Effect,"natural-hit:"+result.Key,null,renderer,result.level);
            effect.SetActive(true);particles.Play(false);
            for(int i=0;i<Count;i++)
            {
                float angle=(i+.4f)*2*Mathf.PI/Count;
                var direction=new Vector3(Mathf.Cos(angle),Mathf.Sin(angle),0);
                var particle=new ParticleSystem.EmitParams {velocity=direction*(1.3f+i*.14f),startSize=.06f+i*.004f,
                    startColor=i%2==0?new Color(1,.83f,.53f):new Color(1,.56f,.26f)};
                particles.Emit(particle,1);
            }
            Shown?.Invoke(result);
        }
        private void OnDestroy()
        {
            if(feedback!=null) feedback.Received-=Show;
            if(material!=null) Destroy(material);
        }
    }
}
