using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
namespace Remains.Presentation
{
    public sealed class ContentComparisonLayout : MonoBehaviour
    {
        [InspectorName("Предупреждение")] public Text Title;
        [InspectorName("Левый заголовок")] public Text LeftTitle;
        [InspectorName("Правый заголовок")] public Text RightTitle;
        [InspectorName("ID пакетов")] public Text[] Ids;
        [InspectorName("Левые значения")] public Text[] Left;
        [InspectorName("Правые значения")] public Text[] Right;
        [InspectorName("Страница")] public Text Page;
        [InspectorName("Назад")] public Button Previous;
        [InspectorName("Далее")] public Button Next;
        [InspectorName("Подтвердить")] public Button Confirm;
        [InspectorName("Отмена")] public Button Cancel;
        public void Validate()
        {
            if(Title==null || LeftTitle==null || RightTitle==null || Page==null || Ids==null || Left==null || Right==null ||
                Ids.Length==0 || Left.Length!=Ids.Length || Right.Length!=Ids.Length)
                throw new InvalidOperationException($"Сравнение контента {name}: неполные ссылки или несовпадающие размеры столбцов.");
            var labels=new HashSet<Text>();
            foreach(var column in new[]{Ids,Left,Right,new[]{Title,LeftTitle,RightTitle,Page}})
                foreach(var text in column)
                    if(text==null || !labels.Add(text) || !text.transform.IsChildOf(transform))
                        throw new InvalidOperationException($"Сравнение контента {name}: неверные или повторные ссылки надписей.");
            var buttons=new HashSet<Button>();
            foreach(var button in new[]{Previous,Next,Confirm,Cancel})
                if(button==null || !buttons.Add(button) || !button.transform.IsChildOf(transform) || button.GetComponentInChildren<Text>(true)==null)
                    throw new InvalidOperationException($"Сравнение контента {name}: неверные ссылки кнопок.");
        }
    }
}
