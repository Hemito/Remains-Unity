using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
namespace Remains.Presentation
{
    public sealed class WorldPickerView : MonoBehaviour
    {
        [InspectorName("Выбор копии мира")] public Button[] Choices;
        [InspectorName("Управление миром")] public Button[] Manage;
        [InspectorName("Пустой список")] public Text Empty;
        [InspectorName("Номер страницы")] public Text Page;
        [InspectorName("Предыдущая страница")] public Button Previous;
        [InspectorName("Следующая страница")] public Button Next;
        [InspectorName("Закрыть")] public Button Close;
        public void Validate()
        {
            if(Choices==null || Manage==null || Choices.Length==0 || Choices.Length!=Manage.Length || Empty==null || Page==null || Previous==null || Next==null || Close==null)
                throw new InvalidOperationException($"Список миров {name}: не заполнены ссылки или размеры строк различаются.");
            var unique=new HashSet<Button>();
            foreach(var group in new[]{Choices,Manage,new[]{Previous,Next,Close}})
                foreach(var button in group)
                    if(button==null || !unique.Add(button) || !button.transform.IsChildOf(transform) || button.GetComponentInChildren<Text>(true)==null)
                        throw new InvalidOperationException($"Список миров {name}: неверные или повторные ссылки кнопок.");
        }
        public static WorldPickerView Create(Transform parent)
        {
            var prefab=Resources.Load<WorldPickerView>("Presentation/WorldPicker");
            if(prefab==null) throw new InvalidOperationException("Нет префаба Presentation/WorldPicker.");
            prefab.Validate();return Instantiate(prefab,parent,false);
        }
    }
}
