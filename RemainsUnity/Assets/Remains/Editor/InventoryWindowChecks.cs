using System;
using Remains.Presentation;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
namespace Remains.Editor
{
    public static class InventoryWindowChecks
    {
        public static void Run()
        {
            var parent=new GameObject("Проверка инвентаря",typeof(RectTransform));
            try
            {
                var view=InventoryWindowView.Create(parent.transform);
                var source=Resources.Load<InventoryWindowView>("Presentation/InventoryWindow");
                Require(view.Personal.activeSelf,"Личная страница должна быть готова к открытию");
                Require(PrefabUtility.GetCorrespondingObjectFromSource(source.PersonalContents)==Resources.Load<PersonalInventoryView>("Presentation/PersonalInventory"),"Личная страница потеряла связь с вложенным префабом");
                Require(view.GetComponentsInChildren<PersonalInventoryView>(true).Length==1,"Личная страница отсутствует или дублируется");
                Require(view.GetComponent<RectTransform>().sizeDelta==source.GetComponent<RectTransform>().sizeDelta,"Изменены размеры оригинального корпуса");
                foreach(var text in view.GetComponentsInChildren<Text>(true))
                    Require(!text.text.StartsWith("Инвентарь ·",StringComparison.Ordinal) && !text.text.Contains("пока недоступна") &&
                        text.text is not ("Личные вещи" or "Общий контейнер" or "Закрыть · Tab"),"Сохранён лишний заголовок, кнопка или заглушка: "+text.text);
                var separate=InventoryWindowView.Create(parent.transform);view.Personal.SetActive(false);
                Require(separate.Personal.activeSelf && source.Personal.activeSelf,"Рабочий экземпляр изменил другую личную страницу");
                var container=ContainerWindowView.Create(parent.transform);container.Validate();
                Require(!container.transform.IsChildOf(separate.transform),"Контейнер должен быть самостоятельным окном");
                Require(Resources.Load<GameObject>("Presentation/SharedInventory")==null,"Устаревшая страница контейнера осталась в ресурсах");
                Debug.Log("Окно инвентаря: PASS — оригинальный корпус, одна личная страница, удалённые заглушки и независимый контейнер.");
            }
            finally {UnityEngine.Object.DestroyImmediate(parent);}
        }
        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException(message);}
    }
}
