using System;
using UnityEngine;

namespace Remains.Content
{
    // The flames of fire (FireVisuals): the original's sprFire sheet (part fire: 17 frames 50×67 px, blend hardlight)
    // cut into sprites standing on their base, and the world material they are drawn with (self-lit).
    public sealed class FireArtwork:ScriptableObject
    {
        public const string Resource="Content/fire-artwork";
        [SerializeField,InspectorName("Кадры пламени")] private Sprite[] frames=Array.Empty<Sprite>();
        [SerializeField,InspectorName("Материал")] private Material material;
        [SerializeField,Min(1),InspectorName("Кадров в секунду")] private float framesPerSecond=15;
        public Sprite[] Frames=>frames;public Material Material=>material;public float FramesPerSecond=>framesPerSecond;
        public void Configure(Sprite[] value,Material flameMaterial,float fps) {frames=value;material=flameMaterial;framesPerSecond=fps;Validate();}
        public void Validate()
        {
            if(frames==null || frames.Length==0 || Array.Exists(frames,f=>f==null) || material==null || !(framesPerSecond>0))
                throw new InvalidOperationException("Рисунок огня: нужны кадры, материал и частота.");
        }
        public static FireArtwork Load()
        {
            var artwork=GameAssets.Load<FireArtwork>(Resource)??throw new InvalidOperationException("Нет рисунка огня "+Resource+".");
            artwork.Validate();return artwork;
        }
    }
}
