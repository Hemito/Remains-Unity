using System;
using UnityEngine;

namespace Remains.Content
{
    [CreateAssetMenu(menuName="Remains/Дверь или закреплённая преграда",fileName="DoorAsset")]
    public sealed class DoorAsset : ScriptableObject
    {
        [SerializeField,InspectorName("Устойчивый ID типа")] private string id;
        [SerializeField,InspectorName("Размер закрытого проёма")] private Vector2 size;
        [SerializeField,InspectorName("Закрыта")] private Sprite closed;
        [SerializeField,InspectorName("Открыта")] private Sprite opened;
        [SerializeField,InspectorName("Звук открытия")] private AudioClip openSound;
        [SerializeField,InspectorName("Звук закрытия")] private AudioClip closeSound;
        [SerializeField] private int capabilityVersion;
        [SerializeField,InspectorName("Можно взаимодействовать")] private bool interactive=true;
        [SerializeField,InspectorName("Размер области взаимодействия")] private Vector2 interactionSize;
        [SerializeField,Min(0),InspectorName("Прочность (0 — явно неразрушаемая)")] private float maxHealth;
        [SerializeField,InspectorName("Разрушена")] private Sprite destroyed;
        [SerializeField,InspectorName("Звук разрушения")] private AudioClip destroySound;
        public string Id=>id;
        public Vector2 Size=>size;
        // Version 0 is the explicit contract of pre-barrier authored assets.
        // It does not infer new capabilities from absent Unity-serialized fields.
        public int CapabilityVersion=>capabilityVersion;
        public bool Interactive=>capabilityVersion==0 || interactive;
        public Vector2 InteractionSize=>capabilityVersion==0?size:interactionSize;
        public float MaxHealth=>capabilityVersion==0?0:maxHealth;
        public Sprite Closed=>closed;
        public Sprite Opened=>opened;
        public Sprite Destroyed=>destroyed;
        public AudioClip OpenSound=>openSound;
        public AudioClip CloseSound=>closeSound;
        public AudioClip DestroySound=>destroySound;
        public void Configure(string key,Vector2 bounds,Sprite shut,Sprite open,AudioClip opening,AudioClip closing)
            =>ConfigureBarrier(key,bounds,shut,open,null,opening,closing,null,true,0,bounds);
        public void ConfigureBarrier(string key,Vector2 bounds,Sprite closed,Sprite opened,Sprite destroyed,
            AudioClip openSound,AudioClip closeSound,AudioClip destroySound,bool interactive,float maxHealth,Vector2? interactionSize=null)
        {
            id=key;size=bounds;this.closed=closed;this.opened=opened;this.destroyed=destroyed;
            this.openSound=openSound;this.closeSound=closeSound;this.destroySound=destroySound;
            this.interactive=interactive;this.maxHealth=maxHealth;this.interactionSize=interactionSize??bounds;capabilityVersion=1;Validate();
        }
        public void Validate()
        {
            var interaction=InteractionSize;
            if(capabilityVersion<0 || capabilityVersion>1 || string.IsNullOrWhiteSpace(id) || !float.IsFinite(size.x) || !float.IsFinite(size.y) || size.x<=0 || size.y<=0 ||
                !float.IsFinite(interaction.x) || !float.IsFinite(interaction.y) || interaction.x<=0 || interaction.y<=0 ||
                !float.IsFinite(MaxHealth) || MaxHealth<0 || closed==null || opened==null ||
                Interactive && (openSound==null || closeSound==null) || MaxHealth>0 && (destroyed==null || destroySound==null))
                throw new InvalidOperationException($"Проём {name}: неполное определение id={id}, version={capabilityVersion}, size={size}, interaction={interaction}, interactive={Interactive}, maxHealth={MaxHealth}.");
        }
        private void OnValidate() {try {Validate();} catch(InvalidOperationException error) {Debug.LogWarning(error.Message,this);}}
    }
}
