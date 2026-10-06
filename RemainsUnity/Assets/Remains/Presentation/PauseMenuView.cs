using System;
using UnityEngine;
using UnityEngine.UI;
namespace Remains.Presentation
{
    public sealed class PauseMenuView : MonoBehaviour
    {
        [InspectorName("Панель меню")] public GameObject Panel;
        [InspectorName("Состояние паузы")] public Text Status;
        [InspectorName("Справка управления")] public Text Controls;
        [InspectorName("Остановить уровень")] public Button ManualPause;
        [InspectorName("Возобновить уровень")] public Button Resume;
        [InspectorName("Пауза при меню")] public Button MenuSetting;
        [InspectorName("Пауза при инвентаре")] public Button InventorySetting;
        [InspectorName("Закрыть меню")] public Button Close;
        [InspectorName("Настройки звука")] public Button Sound;
        [InspectorName("Дружественный огонь")] public Button FriendlyFire;
        [InspectorName("Решение о попадании")] public Button FriendlyDecision;
        [InspectorName("Выход из сессии")] public Button Leave;
        public void Validate()
        {
            if(Panel==null || Status==null || Controls==null || !Panel.transform.IsChildOf(transform) ||
                !Status.transform.IsChildOf(transform) || !Controls.transform.IsChildOf(Panel.transform))
                throw new InvalidOperationException($"Меню паузы {name}: не назначены панель, индикатор или справка управления.");
            var buttons=new[]{ManualPause,Resume,MenuSetting,InventorySetting,Close,Sound,FriendlyFire,FriendlyDecision,Leave};
            var unique=new System.Collections.Generic.HashSet<Button>();
            foreach(var button in buttons)
                if(button==null || !unique.Add(button) || !button.transform.IsChildOf(Panel.transform) || button.GetComponentInChildren<Text>(true)==null)
                    throw new InvalidOperationException($"Меню паузы {name}: неверные или повторные ссылки кнопок.");
        }
        public static PauseMenuView Create(Transform parent)
        {
            var prefab=Resources.Load<PauseMenuView>("Presentation/PauseMenu");
            if(prefab==null) throw new InvalidOperationException("Нет префаба Presentation/PauseMenu.");
            prefab.Validate();return Instantiate(prefab,parent,false);
        }
    }
}
