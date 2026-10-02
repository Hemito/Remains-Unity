using Remains.Serialization;
using UnityEngine;

namespace Remains.Content
{
    [CreateAssetMenu(menuName="Remains/Физический объект",fileName="PropAsset")]
    public sealed class PropAsset : ScriptableObject
    {
        [SerializeField,InspectorName("Определение")]
        private PropDefinition definition=new();
        public PropDefinition CreateDefinition()=>StateJson.FromJson<PropDefinition>(StateJson.ToJson(definition));
        public void SetDefinition(PropDefinition value)=>definition=StateJson.FromJson<PropDefinition>(StateJson.ToJson(value));
        private void OnValidate()
        {
            try {PropCatalog.Validate(definition);}
            catch(System.InvalidOperationException error) {Debug.LogWarning($"Ассет {name}: {error.Message}",this);}
        }
    }
}
