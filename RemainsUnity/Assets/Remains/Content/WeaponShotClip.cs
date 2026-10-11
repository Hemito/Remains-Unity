using System;
using UnityEngine;

namespace Remains.Content
{
    [CreateAssetMenu(menuName="Remains/Анимация выстрела",fileName="WeaponShot")]
    public sealed class WeaponShotClip : ScriptableObject
    {
        [SerializeField,Min(.01f),InspectorName("Кадров в секунду")] private float fps=30;
        [SerializeField,InspectorName("Кадры по порядку")] private Sprite[] frames=Array.Empty<Sprite>();
        public float FramesPerSecond=>fps;
        [SerializeField,InspectorName("Маски дульной вспышки по кадрам (пусто — без света)")] private Sprite[] emissionFrames=Array.Empty<Sprite>();
        [SerializeField,InspectorName("Смещение вспышки от начала оружия (м)")] private Vector2 emissionOffset;
        public Sprite[] EmissionFrames=>emissionFrames;
        public Vector2 EmissionOffset=>emissionOffset;
        public void ConfigureEmission(Sprite[] masks,Vector2 offset)
        {emissionFrames=(Sprite[])masks.Clone();emissionOffset=offset;Validate();}
        public Sprite[] CopyFrames()
        {
            Validate();return (Sprite[])frames.Clone();
        }
        public void Configure(float rate,Sprite[] sprites) {fps=rate;frames=(Sprite[])sprites.Clone();Validate();}
        public void Validate()
        {
            if(!float.IsFinite(fps) || fps<=0 || frames==null || frames.Length==0)
                throw new InvalidOperationException($"Анимация {name}: нужны кадры и положительная конечная частота.");
            for(int i=0;i<frames.Length;i++)
                if(frames[i]==null) throw new InvalidOperationException($"Анимация {name}: отсутствует кадр {i}.");
            if(emissionFrames!=null && emissionFrames.Length!=0 && emissionFrames.Length!=frames.Length)
                throw new InvalidOperationException($"Анимация {name}: число масок вспышки не совпадает с кадрами.");
            if(!float.IsFinite(emissionOffset.x) || !float.IsFinite(emissionOffset.y))
                throw new InvalidOperationException($"Анимация {name}: неконечное смещение вспышки.");
        }
        public void ValidateAgainst(Sprite rest)
        {
            Validate();
            if(rest==null) throw new InvalidOperationException($"Анимация {name}: нет исходного спрайта оружия.");
            var origin=rest.rect.position+rest.pivot;
            for(int i=0;i<frames.Length;i++)
                if(Mathf.Abs(frames[i].pixelsPerUnit-rest.pixelsPerUnit)>.0001f ||
                    Vector2.Distance(frames[i].rect.position+frames[i].pivot,origin)>.001f)
                    throw new InvalidOperationException($"Анимация {name}, кадр {i}: масштаб или точка привязки расходится со спрайтом {rest.name}.");
        }
        private void OnValidate()
        {
            try {Validate();} catch(InvalidOperationException error) {Debug.LogWarning(error.Message,this);}
        }
    }
}
