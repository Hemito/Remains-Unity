using System;
using UnityEngine;

namespace Remains.Presentation
{
    public sealed class PropView : MonoBehaviour
    {
        [SerializeField,InspectorName("Основной спрайт")] private SpriteRenderer artwork;
        [Header("Свечение при удержании")]
        [SerializeField,ColorUsage(false),InspectorName("Цвет магии")] private Color heldColor=new(.1f,1,.15f,1);
        [SerializeField,Range(.005f,.25f),InspectorName("Радиус в координатах спрайта")] private float heldRadius=.08f;
        [SerializeField,Range(0,1),InspectorName("Интенсивность")] private float heldIntensity=.7f;
        public SpriteRenderer Artwork=>artwork;
        private readonly RemotePropMotion motion=new();
        private HeldPropGlow glow;
        public bool HeldGlowVisible=>glow!=null && glow.Visible;
        public void PrepareHeldGlow()
        {
            if(glow!=null) return;
            var child=new GameObject("Свечение удерживаемого предмета");
            child.transform.SetParent(artwork.transform,false);
            glow=child.AddComponent<HeldPropGlow>();
            var color=heldColor;color.a=heldIntensity;
            glow.Initialize(artwork,color,heldRadius);
        }
        private void OnValidate()
        {
            if(!float.IsFinite(heldRadius) || !float.IsFinite(heldIntensity))
                Debug.LogWarning($"Некорректные настройки свечения {name}: radius={heldRadius}, intensity={heldIntensity}; восстановлены допустимые значения.",this);
            heldRadius=float.IsFinite(heldRadius)?Mathf.Clamp(heldRadius,.005f,.25f):.08f;
            heldIntensity=float.IsFinite(heldIntensity)?Mathf.Clamp01(heldIntensity):.7f;
        }
        public void DrawMotion(Remains.Domain.PropState state,string localOwner,bool paused,float dt)
        {transform.position=motion.Sample(state,localOwner,paused,dt);glow?.Draw(state.held);}
        public void SetArtwork(SpriteRenderer value)=>artwork=value;
        public void FitWidth(float width)
        {
            if(artwork==null || artwork.sprite==null) throw new InvalidOperationException($"Объект {name}: нет основного спрайта.");
            if(!float.IsFinite(width) || width<=0 || artwork.bounds.size.x<=0) throw new InvalidOperationException($"Объект {name}: некорректная ширина {width}.");
            transform.localScale*=width/artwork.bounds.size.x;
        }
        public void SetVisible(bool visible)=>gameObject.SetActive(visible);
    }
}
