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
        [SerializeField,InspectorName("Радиус следования оружия за курсором")] private float aimRadius=1;
        [SerializeField,InspectorName("Высота точки удержания")] private float anchorHeight=.98f;
        public float AimRadius=>aimRadius;
        public float AnchorHeight=>anchorHeight;
        public System.Collections.Generic.IEnumerable<Remains.Presentation.WeaponView> Presentations()
        {
            var ids=new System.Collections.Generic.Dictionary<string,Remains.Presentation.WeaponView>(StringComparer.Ordinal);
            foreach(var asset in weapons)
            {
                if(asset==null) throw new InvalidOperationException("Нет ссылки на оружие в "+name);
                var prefab=asset.Presentation;if(prefab==null) continue;
                if(ids.TryGetValue(prefab.Id,out var previous))
                {if(previous!=prefab) throw new InvalidOperationException("Конфликт префабов оружия: "+prefab.Id);continue;}
                ids.Add(prefab.Id,prefab);yield return prefab;
            }
        }
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
