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
        [SerializeField,InspectorName("Область удержания обычных вещей")]
        private HoldingArea ordinaryHoldingArea;
        public HoldingArea OrdinaryHoldingArea=>ordinaryHoldingArea;
        public void SetOrdinaryHoldingArea(HoldingArea value)
        {value.Validate("каталог "+name+", обычные вещи");ordinaryHoldingArea=value;}
        [SerializeField,Min(0),InspectorName("Подъём огнестрела при удержании вверх (м)")] private float raiseHeight=.8f;
        [SerializeField,Min(0),InspectorName("Задержка подъёма огнестрела (с)")] private float raiseDelay=.3666667f;
        public float RaiseHeight=>raiseHeight;
        public float RaiseDelay=>raiseDelay;
        public System.Collections.Generic.IEnumerable<WeaponAsset> AllAssets()
        {
            foreach(var weapon in weapons)
            {if(weapon==null)throw new InvalidOperationException("Нет ссылки на оружие в "+name);yield return weapon;}
        }
        public System.Collections.Generic.IEnumerable<WeaponAsset> CasingWeapons()
        {
            foreach(var weapon in AllAssets())
            {
                if(ReferenceEquals(weapon.CasingProfile,null))continue;
                weapon.CasingProfile.Validate("каталог "+name+", оружие "+weapon.Id);
                if(weapon.CreateDefinition().projectileSpeed<=0)throw new InvalidOperationException("Гильза задана не для дальнего выстрела: weapon="+weapon.Id);
                yield return weapon;
            }
        }
        private void OnValidate()
        {
            try {ordinaryHoldingArea.Validate("каталог "+name+", обычные вещи");}
            catch(System.IO.InvalidDataException error) {Debug.LogWarning(error.Message,this);}
            if(!float.IsFinite(raiseHeight) || !float.IsFinite(raiseDelay) || raiseHeight<0 || raiseDelay<0)
                Debug.LogWarning($"Каталог {name}: высота и задержка подъёма должны быть конечными неотрицательными числами.",this);
            if(float.IsFinite(raiseHeight)) raiseHeight=Mathf.Max(0,raiseHeight);
            if(float.IsFinite(raiseDelay)) raiseDelay=Mathf.Max(0,raiseDelay);
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
                asset.ShotAnimation.ValidateAgainst(asset.Presentation.RestReference);
                yield return asset;
            }
        }
        public System.Collections.Generic.IEnumerable<WeaponAsset> ReloadAnimatedWeapons()
        {
            foreach(var asset in weapons)
            {
                if(asset==null) throw new InvalidOperationException("Нет ссылки на оружие в "+name);
                if(asset.ReloadAnimation==null) continue;
                var definition=asset.CreateDefinition();
                if(asset.Presentation==null || string.IsNullOrEmpty(definition.ammo) || definition.reloadSeconds<=0)
                    throw new InvalidOperationException("Нет префаба/правила перезарядки у анимированного оружия: "+asset.Id);
                asset.ReloadAnimation.ValidateAgainst(asset.Presentation.RestReference);
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
        public System.Collections.Generic.IEnumerable<WeaponAsset> PresentedWeapons()
        {
            foreach(var asset in weapons)
            {
                if(asset==null) throw new InvalidOperationException("Нет ссылки на оружие в "+name);
                if(asset.Presentation!=null) yield return asset;
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
