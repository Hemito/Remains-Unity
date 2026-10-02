using System;
using UnityEngine;

namespace Remains.Presentation
{
    public sealed class PropView : MonoBehaviour
    {
        [SerializeField,InspectorName("Основной спрайт")] private SpriteRenderer artwork;
        public SpriteRenderer Artwork=>artwork;
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
