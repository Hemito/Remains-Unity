using Remains.Serialization;
using UnityEngine;

namespace Remains.Content
{
    [CreateAssetMenu(menuName="Remains/Оружие",fileName="Weapon")]
    public sealed class WeaponAsset : ScriptableObject
    {
        [SerializeField,InspectorName("Определение оружия")]
        private WeaponDefinition definition=new();
        public WeaponDefinition CreateDefinition()
        {
            var result=StateJson.FromJson<WeaponDefinition>(StateJson.ToJson(definition));
            // Unity serializes absent arrays as empty arrays; both mean no authored melee motion.
            if(result.meleeMotion?.Length==0) result.meleeMotion=null;
            return result;
        }
        public void SetDefinition(WeaponDefinition value)=>definition=StateJson.FromJson<WeaponDefinition>(StateJson.ToJson(value));
        private void OnValidate()
        {
            if(definition==null || string.IsNullOrWhiteSpace(definition.id))
            {Debug.LogWarning($"Оружие {name}: задайте стабильный ID определения.",this);return;}
            try {WeaponCatalogLoader.Validate(CreateDefinition(),"ассет "+name);}
            catch(System.IO.InvalidDataException error) {Debug.LogWarning(error.Message,this);}
        }
    }
}
