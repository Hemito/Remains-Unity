using Remains.Serialization;
using UnityEngine;

namespace Remains.Content
{
    [CreateAssetMenu(menuName="Remains/Физический объект",fileName="PropAsset")]
    public sealed class PropAsset : ScriptableObject
    {
        [SerializeField,InspectorName("Определение")]
        private PropDefinition definition=new();
        [SerializeField,InspectorName("Текстура объекта")] private Texture2D texture;
        public Texture2D Texture=>texture;
        public void SetTexture(Texture2D value)=>texture=value;
        public bool MatchesArtwork(string id,string resource)=>definition.id==id && definition.resource==resource;
        public PropDefinition CreateDefinition()=>StateJson.FromJson<PropDefinition>(StateJson.ToJson(definition));
        public void SetDefinition(PropDefinition value)=>definition=StateJson.FromJson<PropDefinition>(StateJson.ToJson(value));
        private void OnValidate()
        {
            try {PropCatalog.Validate(definition);}
            catch(System.InvalidOperationException error) {Debug.LogWarning($"Ассет {name}: {error.Message}",this);}
            if(texture==null) Debug.LogWarning($"Ассет {name}: отсутствует текстура объекта {definition?.id}.",this);
        }
    }
}
