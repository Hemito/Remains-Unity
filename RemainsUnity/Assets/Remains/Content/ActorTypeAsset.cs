using Remains.Serialization;
using UnityEngine;

namespace Remains.Content
{
    public enum ActorPresentationSource {RegisteredBuilder,Prefab}
    [CreateAssetMenu(menuName="Remains/Тип персонажа",fileName="ActorTypeAsset")]
    public sealed class ActorTypeAsset : ScriptableObject
    {
        [SerializeField,InspectorName("Определение")]
        private ActorTypeDefinition definition=new();
        [SerializeField] private ActorPresentationSource presentationSource;
        [SerializeField] private Remains.Presentation.ActorVisual presentationPrefab;
        public void SetPrefab(Remains.Presentation.ActorVisual value) {presentationPrefab=value;presentationSource=ActorPresentationSource.Prefab;}
        public bool TryGetPrefab(ActorTypeDefinition requested,out Remains.Presentation.ActorVisual prefab)
        {
            prefab=null;
            if(requested.id!=definition.id || requested.presentation!=definition.presentation || requested.artwork!=definition.artwork ||
                presentationSource==ActorPresentationSource.RegisteredBuilder) return false;
            if(presentationPrefab==null) throw new System.InvalidOperationException($"Тип {definition.id}: выбран префаб, но ссылка отсутствует.");
            prefab=presentationPrefab;return true;
        }
        public ActorTypeDefinition CreateDefinition()=>StateJson.FromJson<ActorTypeDefinition>(StateJson.ToJson(definition));
        public void SetDefinition(ActorTypeDefinition value)=>definition=StateJson.FromJson<ActorTypeDefinition>(StateJson.ToJson(value));
        private void OnValidate()
        {
            if(definition!=null && (!definition.hasWeapon || definition.presentation!="atlas" || string.IsNullOrWhiteSpace(definition.artwork)))
                definition.atlasWeaponSocket=false;
            try {ActorTypes.Validate(definition);}
            catch(System.InvalidOperationException error) {Debug.LogWarning($"Ассет {name}: {error.Message}",this);}
        }
    }
}
