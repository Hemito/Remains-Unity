using System;
using Remains.Content;
using Remains.Domain;
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
        // No local toggle or pivot correction: every state keeps its authored registration.
        public void Show(bool open)=>Show(open,false);
        public void Show(DoorState state)
        {
            if(state==null) throw new ArgumentNullException(nameof(state));
            state.Validate();
            if(definition==null || state.definition!=definition.Id) throw new InvalidOperationException($"Проём {name}: state={state.id}/{state.definition}, view={definition?.Id ?? "<null>"}.");
            Show(state.open,state.Destroyed);
        }
        public void Show(bool open,bool destroyed)
        {
            if(definition==null || artwork==null) throw new InvalidOperationException($"Проём {name}: отсутствует определение или рендерер.");
            definition.Validate();
            if(destroyed && (definition.MaxHealth<=0 || definition.Destroyed==null))
                throw new InvalidOperationException($"Проём {name}, type={definition.Id}: состояние разрушено, но определение не поддерживает разрушение.");
            artwork.sprite=destroyed?definition.Destroyed:open?definition.Opened:definition.Closed;
        }
    }
}
