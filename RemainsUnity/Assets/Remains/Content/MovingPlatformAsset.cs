using System;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;
namespace Remains.Content
{
    [CreateAssetMenu(menuName="Remains/Движущаяся платформа",fileName="MovingPlatform")]
    public sealed class MovingPlatformAsset : ScriptableObject
    {
        [SerializeField,InspectorName("Определение платформы")] private MovingPlatformDefinition definition=new();
        [SerializeField,InspectorName("Префаб исходной графики (без коллизии)")] private GameObject prefab;
        public string Id=>definition.id;
        public GameObject Prefab=>prefab;
        public MovingPlatformDefinition CreateDefinition()=>StateJson.Copy(definition);
        public void Configure(MovingPlatformDefinition value,GameObject artwork) {definition=StateJson.Copy(value);prefab=artwork;Validate();}
        public void Validate()
        {
            if(definition==null) throw new InvalidOperationException("Платформа "+name+": нет определения.");
            definition.Validate();
            if(prefab==null) throw new InvalidOperationException("Платформа "+name+": нет сохранённого префаба, id="+definition.id);
        }
        private void OnValidate() {try {Validate();}catch(InvalidOperationException error){Debug.LogWarning(error.Message,this);}}
    }
}
