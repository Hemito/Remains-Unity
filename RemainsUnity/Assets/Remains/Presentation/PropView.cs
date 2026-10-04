using System;
using UnityEngine;

namespace Remains.Presentation
{
    public sealed class PropView : MonoBehaviour
    {
        [SerializeField,InspectorName("Основной спрайт")] private SpriteRenderer artwork;
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
            glow.Initialize(artwork,new Color(.1f,1,.15f,.7f));
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
