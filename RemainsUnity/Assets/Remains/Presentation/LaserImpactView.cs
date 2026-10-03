using System;
using UnityEngine;

namespace Remains.Presentation
{
    public sealed class LaserImpactView : MonoBehaviour
    {
        [InspectorName("Изображение вспышки")] public SpriteRenderer Artwork;
        [InspectorName("Кадры вспышки")] public Sprite[] Frames;
        [InspectorName("Кадров в секунду"),Min(.01f)] public float FramesPerSecond=30;
        public void Validate()
        {
            if(Artwork==null || Artwork.sharedMaterial==null || Frames==null || Frames.Length==0 ||
                Array.Exists(Frames,frame=>frame==null) || !float.IsFinite(FramesPerSecond) || FramesPerSecond<=0)
                throw new InvalidOperationException($"Вспышка лазера {name}: проверьте изображение, материал, кадры и частоту.");
        }
        public bool Draw(float age)
        {
            int frame=Mathf.FloorToInt(Mathf.Max(0,age)*FramesPerSecond);
            if(frame>=Frames.Length) return false;
            Artwork.sprite=Frames[frame];return true;
        }
        private void OnValidate()
        {try {Validate();} catch(InvalidOperationException error) {Debug.LogWarning(error.Message,this);}}
    }
}
