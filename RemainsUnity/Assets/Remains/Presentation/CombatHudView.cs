using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
namespace Remains.Presentation
{
    public sealed class CombatHudView : MonoBehaviour
    {
        [InspectorName("Панель состояния")] public GameObject Vitals;
        [InspectorName("Панель оружия")] public GameObject Loadout;
        [InspectorName("Имя / состояние")] public Text CharacterName;
        [InspectorName("Текст здоровья")] public Text HealthText;
        [InspectorName("Текст маны")] public Text ManaText;
        [InspectorName("Боезапас")] public Text Ammunition;
        [InspectorName("Запас гранат")] public Text Grenades;
        [InspectorName("Иконка гранаты")] public Image GrenadeIcon;
        [InspectorName("Шкала здоровья")] public Image Health;
        [InspectorName("Шкала маны")] public Image Mana;
        [InspectorName("Быстрые клавиши предметов")] public Button[] Weapons;
        [InspectorName("Оружие старых данных: первые четыре клавиши (диагностика)")] public string[] WeaponIds;
        [InspectorName("Кадры предупреждения здоровья")] public Sprite[] HealthWarningFrames;
        public void Validate()
        {
            if(Vitals==null || Loadout==null || CharacterName==null || HealthText==null || ManaText==null || Ammunition==null ||
                Grenades==null || GrenadeIcon==null || GrenadeIcon.sprite==null || Health==null || Health.sprite==null || Mana==null || Mana.sprite==null || Weapons==null || WeaponIds==null || Weapons.Length!=Remains.Domain.ItemShortcuts.Keys.Count || WeaponIds.Length!=4)
                throw new InvalidOperationException("HUD: отсутствуют ссылки или не совпадают кнопки и ID оружия.");
            var ids=new HashSet<string>();
            for(int i=0;i<Weapons.Length;i++) if(Weapons[i]==null || !ids.Add(Weapons[i].name))
                throw new InvalidOperationException("HUD: неверная кнопка/ID оружия, позиция "+i);
            if(Array.Exists(WeaponIds,string.IsNullOrWhiteSpace)) throw new InvalidOperationException("HUD: не заполнены старые диагностические определения оружия.");
            if(HealthWarningFrames==null || HealthWarningFrames.Length==0 || Array.Exists(HealthWarningFrames,frame=>frame==null))
                throw new InvalidOperationException("HUD: отсутствуют кадры предупреждения здоровья.");
        }
    }
}
