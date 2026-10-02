using System;
using Remains.Presentation;
using UnityEngine;
namespace Remains.Editor
{
    public static class InventoryWindowChecks
    {
        public static void Run()
        {
            var parent=new GameObject("Проверка инвентаря",typeof(RectTransform));
            try
            {
                var view=InventoryWindowView.Create(parent.transform);int closed=0,sounds=0;
                var window=new InventoryWindow(view,()=>closed++,()=>sounds++);
                Require(view.Personal.activeSelf && !view.Shared.activeSelf,"Неверная начальная вкладка");
                view.SharedTab.onClick.Invoke();Require(!view.Personal.activeSelf && view.Shared.activeSelf && sounds==1,"Общий контейнер не открылся");
                view.PersonalTab.onClick.Invoke();Require(view.Personal.activeSelf && !view.Shared.activeSelf && sounds==2,"Личные вещи не открылись");
                view.Close.onClick.Invoke();Require(closed==1,"Закрытие вызвано неверное число раз");
                var source=Resources.Load<InventoryWindowView>("Presentation/InventoryWindow");
                view.SharedTab.onClick.Invoke();Require(source.Personal.activeSelf && !source.Shared.activeSelf,"Вкладка изменяет исходный префаб");
                Require(view.GetComponent<RectTransform>().sizeDelta==source.GetComponent<RectTransform>().sizeDelta,"Контроллер изменил размеры корпуса");
                Debug.Log("Окно инвентаря: PASS — вкладки, закрытие, звук страницы и независимость префаба.");
            }
            finally {UnityEngine.Object.DestroyImmediate(parent);}
        }
        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException(message);}
    }
}
