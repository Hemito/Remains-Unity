using System;
using UnityEngine;
using UnityEngine.UI;
namespace Remains.Presentation
{
    public sealed class InventoryWindowView : MonoBehaviour
    {
        [InspectorName("Страница личных вещей")] public GameObject Personal;
        [InspectorName("Страница общего контейнера")] public GameObject Shared;
        [InspectorName("Вкладка личных вещей")] public Button PersonalTab;
        [InspectorName("Вкладка общего контейнера")] public Button SharedTab;
        [InspectorName("Закрыть")] public Button Close;
        [InspectorName("Количество предметов")] public Button Quantity;
        public void Validate()
        {
            if(Personal==null || Shared==null || PersonalTab==null || SharedTab==null || Close==null || Quantity==null || GetComponent<Image>()?.sprite==null)
                throw new InvalidOperationException("Окно инвентаря: отсутствуют ссылки на страницы, кнопки или корпус.");
        }
        public static InventoryWindowView Create(Transform parent)
        {
            var prefab=Resources.Load<InventoryWindowView>("Presentation/InventoryWindow");
            if(prefab==null) throw new InvalidOperationException("Нет префаба Presentation/InventoryWindow.");
            prefab.Validate();return UnityEngine.Object.Instantiate(prefab,parent,false);
        }
    }
}
