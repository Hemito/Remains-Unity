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
        // Frames of the original export, loaded on demand: each sprite is cropped to its own image and its pivot is the
        // weapon's registration point, so frames line up by pivot, not by a shared canvas.
        [SerializeField,InspectorName("Кадры выгрузки по требованию (опора — регистрация оригинала)")] private DeferredSprites deferred=new();
        public bool Deferred=>deferred!=null && deferred.Count>0;
        public int FrameCount=>Deferred?deferred.Count:frames.Length;
        // A frame to draw: null while a deferred frame is still loading (draw the resting sprite).
        public Sprite Frame(int index)=>Deferred?deferred.Get(index):frames[index];
        public void Preload() {if(Deferred) deferred.Preload();}
        public Sprite LoadFrameNow(int index)=>Deferred?deferred.LoadNow(index):frames[index];
        public Sprite[] EmissionFrames=>emissionFrames;
        public Vector2 EmissionOffset=>emissionOffset;
        public void ConfigureEmission(Sprite[] masks,Vector2 offset)
        {emissionFrames=(Sprite[])masks.Clone();emissionOffset=offset;Validate();}
        public Sprite[] CopyFrames()
        {
            Validate();
            if(Deferred) throw new InvalidOperationException($"Анимация {name}: кадры загружаются по требованию (Frame/Preload).");
            return (Sprite[])frames.Clone();
        }
        public void Configure(float rate,Sprite[] sprites) {fps=rate;frames=(Sprite[])sprites.Clone();Validate();}
        public void ConfigureDeferred(float rate,string[] resources,float pixelsPerUnit)
        {fps=rate;frames=Array.Empty<Sprite>();deferred=new DeferredSprites();deferred.Configure(resources,pixelsPerUnit);Validate();}
        public void Validate()
        {
            if(!float.IsFinite(fps) || fps<=0 || frames==null || frames.Length==0 && !Deferred || frames.Length>0 && Deferred)
                throw new InvalidOperationException($"Анимация {name}: нужны кадры (свои или по требованию) и положительная конечная частота.");
            if(Deferred) deferred.Validate("Анимация "+name);
            for(int i=0;i<frames.Length;i++)
                if(frames[i]==null) throw new InvalidOperationException($"Анимация {name}: отсутствует кадр {i}.");
            if(emissionFrames!=null && emissionFrames.Length!=0 && emissionFrames.Length!=FrameCount)
                throw new InvalidOperationException($"Анимация {name}: число масок вспышки не совпадает с кадрами.");
            if(!float.IsFinite(emissionOffset.x) || !float.IsFinite(emissionOffset.y))
                throw new InvalidOperationException($"Анимация {name}: неконечное смещение вспышки.");
        }
        public void ValidateAgainst(Sprite rest)=>ValidateAgainst(rest==null?null:CatalogSpriteReference.FromSprite(rest));
        public void ValidateAgainst(CatalogSpriteReference rest)
        {
            Validate();
            if(rest==null) throw new InvalidOperationException($"Анимация {name}: нет исходного спрайта оружия.");
            rest.Validate("Кадр покоя "+name);
            if(Deferred)
            {
                if(Mathf.Abs(deferred.PixelsPerUnit-rest.PixelsPerUnit)>.0001f)
                    throw new InvalidOperationException($"Анимация {name}: масштаб кадров выгрузки расходится со спрайтом {rest.Key}.");
                return;
            }
            var origin=rest.Rect.position+rest.Pivot;
            for(int i=0;i<frames.Length;i++)
                if(Mathf.Abs(frames[i].pixelsPerUnit-rest.PixelsPerUnit)>.0001f ||
                    Vector2.Distance(frames[i].rect.position+frames[i].pivot,origin)>.001f)
                    throw new InvalidOperationException($"Анимация {name}, кадр {i}: масштаб или точка привязки расходится со спрайтом {rest.Key}.");
        }
        private void OnValidate()
        {
            try {Validate();} catch(InvalidOperationException error) {Debug.LogWarning(error.Message,this);}
        }
    }
}
