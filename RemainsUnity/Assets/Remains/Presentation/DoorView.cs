using System;
using Remains.Content;
using UnityEngine;

namespace Remains.Presentation
{
    public sealed class DoorView : MonoBehaviour
    {
        [SerializeField,InspectorName("Определение двери")] private DoorAsset definition;
        [SerializeField,InspectorName("Изображение")] private SpriteRenderer artwork;
        public DoorAsset Definition=>definition;
        public SpriteRenderer Artwork=>artwork;
        public void Configure(DoorAsset asset,SpriteRenderer renderer) {definition=asset;artwork=renderer;Show(false);}
        // No local toggle: the caller supplies the canonical state. Initial load is silent.
        public void Show(bool open)
        {
            if(definition==null || artwork==null) throw new InvalidOperationException($"Дверь {name}: отсутствует определение или рендерер.");
            definition.Validate();artwork.sprite=open?definition.Opened:definition.Closed;
        }
    }
}
