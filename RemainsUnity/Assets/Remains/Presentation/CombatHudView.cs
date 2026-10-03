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
        [InspectorName("Кнопки оружия")] public Button[] Weapons;
        [InspectorName("ID оружия по порядку кнопок")] public string[] WeaponIds;
        [InspectorName("Кадры предупреждения здоровья")] public Sprite[] HealthWarningFrames;
        public void Validate()
        {
            if(Vitals==null || Loadout==null || CharacterName==null || HealthText==null || ManaText==null || Ammunition==null ||
                Grenades==null || GrenadeIcon==null || GrenadeIcon.sprite==null || Health==null || Health.sprite==null || Mana==null || Mana.sprite==null || Weapons==null || WeaponIds==null || Weapons.Length==0 || Weapons.Length!=WeaponIds.Length)
                throw new InvalidOperationException("HUD: отсутствуют ссылки или не совпадают кнопки и ID оружия.");
            var ids=new HashSet<string>();
            for(int i=0;i<Weapons.Length;i++) if(Weapons[i]==null || string.IsNullOrWhiteSpace(WeaponIds[i]) || !ids.Add(WeaponIds[i]))
                throw new InvalidOperationException("HUD: неверная кнопка/ID оружия, позиция "+i);
            if(HealthWarningFrames==null || HealthWarningFrames.Length==0 || Array.Exists(HealthWarningFrames,frame=>frame==null))
                throw new InvalidOperationException("HUD: отсутствуют кадры предупреждения здоровья.");
        }
    }
}
