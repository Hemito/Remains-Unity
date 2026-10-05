using System;
using Remains.Domain;
using UnityEngine;
using UnityEngine.UI;
namespace Remains.Presentation
{
    public sealed class CharacterPortraitView : MonoBehaviour
    {
        [InspectorName("Портрет пони")] public GameObject Pony;
        [InspectorName("Сохранённые части")] public PortraitPart[] Parts;
        [InspectorName("Последнее оружие")] public Image Weapon;
        private InventoryPortrait portrait;
        public void Validate()
        {
            if(Pony==null || Parts==null || Parts.Length==0 || Weapon==null) throw new InvalidOperationException("Портрет персонажа не заполнен: "+name);
            foreach(var part in Parts) {if(part==null) throw new InvalidOperationException("Пустая часть портрета: "+name);part.Validate();}
        }
        public void Show(ActorState actor,InventoryArtwork artwork)
        {
            portrait??=new InventoryPortrait(Pony,Parts);portrait.Refresh(actor);
            string weapon=actor==null?null:actor.weaponInventoryVersion==1?WeaponInventory.Selected(actor)?.definition:actor.weapon;
            artwork.Set(Weapon,weapon);
        }
    }
}
