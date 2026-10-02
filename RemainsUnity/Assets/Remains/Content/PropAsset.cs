using Remains.Serialization;
using UnityEngine;

namespace Remains.Content
{
    [CreateAssetMenu(menuName="Remains/Физический объект",fileName="PropAsset")]
    public sealed class PropAsset : ScriptableObject
    {
        [SerializeField,InspectorName("Определение")]
        private PropDefinition definition=new();
        public Texture2D Texture=>prefab!=null && prefab.Artwork!=null && prefab.Artwork.sprite!=null?prefab.Artwork.sprite.texture:null;
        [SerializeField,InspectorName("Префаб объекта")] private Remains.Presentation.PropView prefab;
        public Remains.Presentation.PropView Prefab=>prefab;
        public void SetPrefab(Remains.Presentation.PropView value)=>prefab=value;
        public bool MatchesArtwork(string id,string resource)=>definition.id==id && definition.resource==resource;
        public PropDefinition CreateDefinition()=>StateJson.FromJson<PropDefinition>(StateJson.ToJson(definition));
        public void SetDefinition(PropDefinition value)=>definition=StateJson.FromJson<PropDefinition>(StateJson.ToJson(value));
        private void OnValidate()
        {
            try {PropCatalog.Validate(definition);}
            catch(System.InvalidOperationException error) {Debug.LogWarning($"Ассет {name}: {error.Message}",this);}
            if(Texture==null) Debug.LogWarning($"Ассет {name}: отсутствует префаб или его основной спрайт для {definition?.id}.",this);
        }
    }
}
