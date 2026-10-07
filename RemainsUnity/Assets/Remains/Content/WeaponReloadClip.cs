using System;
using UnityEngine;

namespace Remains.Content
{
    [CreateAssetMenu(menuName="Remains/Анимация перезарядки",fileName="WeaponReload")]
    public sealed class WeaponReloadClip : ScriptableObject
    {
        [SerializeField,InspectorName("Кадры в исходном порядке; время задаёт перезарядка оружия")]
        private Sprite[] frames=Array.Empty<Sprite>();
        public int FrameCount=>frames.Length;
        public Sprite Frame(int index)=>frames[index];
        public void Configure(Sprite[] value)
        {frames=(Sprite[])(value??throw new ArgumentNullException(nameof(value))).Clone();Validate();}
        public int FrameIndex(float progress)
            =>IndexForProgress(frames.Length,progress);
        public static int IndexForProgress(int count,float progress)
        {
            if(count<=0 || !float.IsFinite(progress)) throw new InvalidOperationException($"Перезарядка: некорректные count={count}, progress={progress}.");
            return Mathf.Min(count-1,Mathf.FloorToInt(Mathf.Clamp01(progress)*count));
        }
        public void Validate()
        {
            if(frames==null || frames.Length==0) throw new InvalidOperationException($"Перезарядка {name}: не заданы кадры.");
            for(int i=0;i<frames.Length;i++) if(frames[i]==null)
                throw new InvalidOperationException($"Перезарядка {name}: отсутствует кадр {i}.");
        }
        public void ValidateAgainst(Sprite rest)
        {
            Validate();
            if(rest==null) throw new InvalidOperationException($"Перезарядка {name}: отсутствует спрайт покоя.");
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
