using Remains.Serialization;
using UnityEngine;

namespace Remains.Content
{
    [CreateAssetMenu(menuName="Remains/Тип персонажа",fileName="ActorTypeAsset")]
    public sealed class ActorTypeAsset : ScriptableObject
    {
        [SerializeField,InspectorName("Определение")]
        private ActorTypeDefinition definition=new();
        public ActorTypeDefinition CreateDefinition()=>StateJson.FromJson<ActorTypeDefinition>(StateJson.ToJson(definition));
        public void SetDefinition(ActorTypeDefinition value)=>definition=StateJson.FromJson<ActorTypeDefinition>(StateJson.ToJson(value));
        private void OnValidate()
        {
            try {ActorTypes.Validate(definition);}
            catch(System.InvalidOperationException error) {Debug.LogWarning($"Ассет {name}: {error.Message}",this);}
        }
    }
}
