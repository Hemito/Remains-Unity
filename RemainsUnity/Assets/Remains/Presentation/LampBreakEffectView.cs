using System;
using UnityEngine;

namespace Remains.Presentation
{
    public sealed class LampBreakEffectView : MonoBehaviour
    {
        [InspectorName("Частицы разбитой лампы")] public ParticleSystem Particles;
        [InspectorName("Звук стекла")] public AudioClip Sound;
        public void Validate()
        {
            if(Particles==null || Sound==null || Particles.GetComponent<ParticleSystemRenderer>().sharedMaterial==null)
                throw new InvalidOperationException("Эффект разбитой лампы: не назначены частицы, материал или звук.");
        }
    }
}
