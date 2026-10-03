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
        [SerializeField,Min(0),InspectorName("Вынос огнестрела на лестнице (м)")] private float climbingAimRadius;
        [SerializeField,InspectorName("Максимальный горизонтальный вынос огнестрела (м)")] private float aimRadius=1;
        [SerializeField,InspectorName("Высота точки удержания")] private float anchorHeight=.98f;
        public float AimRadius=>aimRadius;
        public float ClimbingAimRadius=>climbingAimRadius;
        public float AnchorHeight=>anchorHeight;
        private void OnValidate()
        {
            if(!float.IsFinite(aimRadius) || !float.IsFinite(climbingAimRadius) || !float.IsFinite(anchorHeight))
                Debug.LogWarning($"Каталог {name}: неконечные параметры удержания оружия заменены исходными значениями. Проверьте радиусы и высоту.",this);
            aimRadius=float.IsFinite(aimRadius)?Mathf.Max(.01f,aimRadius):1;
            climbingAimRadius=float.IsFinite(climbingAimRadius)?Mathf.Clamp(climbingAimRadius,0,aimRadius):0;
            anchorHeight=float.IsFinite(anchorHeight)?Mathf.Max(0,anchorHeight):.98f;
        }
        public System.Collections.Generic.IEnumerable<WeaponAsset> AnimatedWeapons()
        {
            foreach(var asset in weapons)
            {
                if(asset==null) throw new InvalidOperationException("Нет ссылки на оружие в "+name);
                if(asset.ShotAnimation==null) continue;
                if(asset.Presentation==null) throw new InvalidOperationException("Нет префаба анимированного оружия: "+asset.Id);
                asset.ShotAnimation.ValidateAgainst(asset.Presentation.Artwork.sprite);
                yield return asset;
            }
        }
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
