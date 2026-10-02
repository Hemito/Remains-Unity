using System;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Content
{
    [CreateAssetMenu(menuName="Remains/Каталог оружия",fileName="WeaponCatalog")]
    public sealed class WeaponAssetCatalog : ScriptableObject
    {
        [SerializeField,InspectorName("Оружие в порядке загрузки")]
        private WeaponAsset[] weapons=Array.Empty<WeaponAsset>();
        public void SetAssets(WeaponAsset[] value)=>weapons=(WeaponAsset[])value.Clone();
        public string CreateJson()
        {
            var entries=new WeaponDefinition[weapons.Length];
            for(int i=0;i<entries.Length;i++)
            {
                if(weapons[i]==null) throw new InvalidOperationException($"Каталог {name}: отсутствует оружие в позиции {i}.");
                entries[i]=weapons[i].CreateDefinition();
            }
            return StateJson.ToJson(new WeaponList {weapons=entries});
        }
    }
}
