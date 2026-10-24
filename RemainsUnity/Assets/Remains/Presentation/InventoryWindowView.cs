using System;
using UnityEngine;
using UnityEngine.UI;
using Remains.Content;
namespace Remains.Presentation
{
    public sealed class InventoryWindowView : MonoBehaviour
    {
        [InspectorName("Страница личных вещей")] public GameObject Personal;
        [InspectorName("Содержимое личной страницы")] public PersonalInventoryView PersonalContents;
        public void Validate()
        {
            if(Personal==null || GetComponent<Image>()?.sprite==null)
                throw new InvalidOperationException("Окно инвентаря: отсутствуют личная страница или оригинальный корпус.");
            if(PersonalContents==null || !PersonalContents.transform.IsChildOf(Personal.transform))
                throw new InvalidOperationException("Окно инвентаря: содержимое должно быть сохранено внутри личной страницы.");
            foreach(var button in GetComponentsInChildren<Button>(true))
                if(!button.transform.IsChildOf(PersonalContents.transform))
                    throw new InvalidOperationException($"Окно инвентаря: лишняя кнопка вне содержимого: {button.name}.");
            PersonalContents.Validate();
        }
        public static InventoryWindowView Create(Transform parent)
        {
            var prefab=GameAssets.Load<InventoryWindowView>("Presentation/InventoryWindow");
            if(prefab==null) throw new InvalidOperationException("Нет префаба Presentation/InventoryWindow.");
            prefab.Validate();return UnityEngine.Object.Instantiate(prefab,parent,false);
        }
    }
}
