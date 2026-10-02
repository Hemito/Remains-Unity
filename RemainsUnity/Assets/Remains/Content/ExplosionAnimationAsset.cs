using System;
using UnityEngine;
namespace Remains.Content
{
    [CreateAssetMenu(menuName="Remains/Анимация взрыва",fileName="ExplosionAnimation")]
    public sealed class ExplosionAnimationAsset : ScriptableObject
    {
        [SerializeField,Min(.01f),InspectorName("Кадров в секунду")] private float framesPerSecond=30;
        [SerializeField,InspectorName("Кадры по порядку")] private Sprite[] frames=Array.Empty<Sprite>();
        public Remains.Presentation.ExplosionArtwork CreateArtwork()
        {
            Validate();
            return new Remains.Presentation.ExplosionArtwork {framesPerSecond=framesPerSecond,frames=(Sprite[])frames.Clone()};
        }
        public void Validate()
        {
            if(!float.IsFinite(framesPerSecond) || framesPerSecond<=0 || frames==null || frames.Length==0)
                throw new InvalidOperationException($"Взрыв {name}: неверная частота или пустая последовательность кадров.");
            for(int i=0;i<frames.Length;i++) if(frames[i]==null)
                throw new InvalidOperationException($"Взрыв {name}: отсутствует кадр {i}.");
        }
        private void OnValidate()
        {
            try {Validate();} catch(InvalidOperationException error) {Debug.LogWarning(error.Message,this);}
        }
    }
}
