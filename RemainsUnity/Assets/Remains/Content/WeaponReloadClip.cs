using System;
using UnityEngine;

namespace Remains.Content
{
    [CreateAssetMenu(menuName="Remains/Анимация перезарядки",fileName="WeaponReload")]
    public sealed class WeaponReloadClip : ScriptableObject
    {
        [SerializeField,InspectorName("Кадры в исходном порядке; время задаёт перезарядка оружия")]
        private Sprite[] frames=Array.Empty<Sprite>();
        // Frames of the original export loaded on demand, aligned by their pivots (as WeaponShotClip's).
        [SerializeField,InspectorName("Кадры выгрузки по требованию (опора — регистрация оригинала)")] private DeferredSprites deferred=new();
        public bool Deferred=>deferred!=null && deferred.Count>0;
        public int FrameCount=>Deferred?deferred.Count:frames.Length;
        // A frame to draw: null while a deferred frame is still loading (draw the resting sprite).
        public Sprite Frame(int index)=>Deferred?deferred.Get(index):frames[index];
        public void Preload() {if(Deferred) deferred.Preload();}
        public Sprite LoadFrameNow(int index)=>Deferred?deferred.LoadNow(index):frames[index];
        public void ConfigureDeferred(string[] resources,float pixelsPerUnit)
        {frames=Array.Empty<Sprite>();deferred=new DeferredSprites();deferred.Configure(resources,pixelsPerUnit);Validate();}
        public void Configure(Sprite[] value)
        {frames=(Sprite[])(value??throw new ArgumentNullException(nameof(value))).Clone();Validate();}
        public int FrameIndex(float progress)
            =>IndexForProgress(FrameCount,progress);
        public static int IndexForProgress(int count,float progress)
        {
            if(count<=0 || !float.IsFinite(progress)) throw new InvalidOperationException($"Перезарядка: некорректные count={count}, progress={progress}.");
            return Mathf.Min(count-1,Mathf.FloorToInt(Mathf.Clamp01(progress)*count));
        }
        public void Validate()
        {
            if(frames==null || frames.Length==0 && !Deferred || frames.Length>0 && Deferred) throw new InvalidOperationException($"Перезарядка {name}: не заданы кадры (свои или по требованию).");
            if(Deferred) deferred.Validate("Перезарядка "+name);
            for(int i=0;i<frames.Length;i++) if(frames[i]==null)
                throw new InvalidOperationException($"Перезарядка {name}: отсутствует кадр {i}.");
        }
        public void ValidateAgainst(Sprite rest)
        {
            Validate();
            if(rest==null) throw new InvalidOperationException($"Перезарядка {name}: отсутствует спрайт покоя.");
            if(Deferred)
            {
                if(Mathf.Abs(deferred.PixelsPerUnit-rest.pixelsPerUnit)>.0001f)
                    throw new InvalidOperationException($"Перезарядка {name}: масштаб кадров выгрузки расходится с {rest.name}.");
                return;
            }
            Vector2 origin=rest.rect.position+rest.pivot;
            for(int i=0;i<frames.Length;i++)
                if(Mathf.Abs(frames[i].pixelsPerUnit-rest.pixelsPerUnit)>.0001f ||
                    Vector2.Distance(frames[i].rect.position+frames[i].pivot,origin)>.001f)
                    throw new InvalidOperationException($"Перезарядка {name}, кадр {i}: масштаб/точка привязки расходится с {rest.name}.");
        }
        private void OnValidate()
        {try {Validate();} catch(InvalidOperationException error) {Debug.LogWarning(error.Message,this);}}
    }
}
