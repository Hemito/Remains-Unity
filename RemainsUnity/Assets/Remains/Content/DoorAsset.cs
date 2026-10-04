using System;
using UnityEngine;

namespace Remains.Content
{
    [CreateAssetMenu(menuName="Remains/Дверь",fileName="DoorAsset")]
    public sealed class DoorAsset : ScriptableObject
    {
        [SerializeField,InspectorName("Устойчивый ID типа")] private string id;
        [SerializeField,InspectorName("Размер закрытого проёма")] private Vector2 size;
        [SerializeField,InspectorName("Закрыта")] private Sprite closed;
        [SerializeField,InspectorName("Открыта")] private Sprite opened;
        [SerializeField,InspectorName("Звук открытия")] private AudioClip openSound;
        [SerializeField,InspectorName("Звук закрытия")] private AudioClip closeSound;
        public string Id=>id;
        public Vector2 Size=>size;
        public Sprite Closed=>closed;
        public Sprite Opened=>opened;
        public AudioClip OpenSound=>openSound;
        public AudioClip CloseSound=>closeSound;
        public void Configure(string key,Vector2 bounds,Sprite shut,Sprite open,AudioClip opening,AudioClip closing)
        {id=key;size=bounds;closed=shut;opened=open;openSound=opening;closeSound=closing;Validate();}
        public void Validate()
        {
            if(string.IsNullOrWhiteSpace(id) || !float.IsFinite(size.x) || !float.IsFinite(size.y) || size.x<=0 || size.y<=0 ||
                closed==null || opened==null || openSound==null || closeSound==null)
                throw new InvalidOperationException($"Дверь {name}: неполное определение id={id}, size={size}.");
        }
        private void OnValidate() {try {Validate();} catch(InvalidOperationException error) {Debug.LogWarning(error.Message,this);}}
    }
}
