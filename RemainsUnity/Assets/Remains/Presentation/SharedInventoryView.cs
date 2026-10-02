using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
namespace Remains.Presentation
{
    public sealed class SharedInventoryView : MonoBehaviour
    {
        [InspectorName("Ячейки контейнера (по строкам)")] public Button[] Slots;
        [InspectorName("Иконки ячеек")] public Image[] Icons;
        [InspectorName("Заголовок")] public Text Title;
        [InspectorName("Предыдущая страница")] public Button Previous;
        [InspectorName("Следующая страница")] public Button Next;
        public void Validate()
        {
            if(Slots==null || Icons==null || Slots.Length!=9 || Icons.Length!=9 || Title==null || Previous==null || Next==null)
                throw new InvalidOperationException("Общий контейнер: нужны девять ячеек, девять иконок, заголовок и кнопки страниц.");
            var unique=new HashSet<Button>();
            for(int i=0;i<Slots.Length;i++)
                if(Slots[i]==null || Icons[i]==null || !unique.Add(Slots[i]) || !Slots[i].transform.IsChildOf(transform) ||
                    !Icons[i].transform.IsChildOf(Slots[i].transform) || Slots[i].GetComponentInChildren<Text>()==null)
                    throw new InvalidOperationException($"Общий контейнер: неверные ссылки ячейки {i} в {name}.");
            if(Previous==Next || unique.Contains(Previous) || unique.Contains(Next) || !Previous.transform.IsChildOf(transform) ||
                !Next.transform.IsChildOf(transform) || !Title.transform.IsChildOf(transform))
                throw new InvalidOperationException($"Общий контейнер: неверные ссылки навигации в {name}.");
        }
        public static SharedInventoryView Create(Transform parent)
        {
            var prefab=Resources.Load<SharedInventoryView>("Presentation/SharedInventory");
            if(prefab==null) throw new InvalidOperationException("Нет префаба Presentation/SharedInventory.");
            prefab.Validate();return UnityEngine.Object.Instantiate(prefab,parent,false);
        }
    }
}
