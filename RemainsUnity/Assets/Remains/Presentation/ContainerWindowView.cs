using System;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    public sealed class ContainerWindowView : MonoBehaviour
    {
        [InspectorName("Содержимое контейнера")] public SharedInventoryView Contents;
        [InspectorName("Закрыть контейнер")] public Button Close;
        [InspectorName("Количество")] public Button Quantity;
        [InspectorName("Названия предметов")] public Text[] Names;
        [InspectorName("Количество в стопках")] public Text[] Counts;
        public void SetSlotLabel(int index,string title,int count)
        {
            Names[index].text=title??"Пусто";
            Counts[index].text=title==null?"":"×"+count;
        }
        public void Validate()
        {
            if(Contents==null || Close==null || Quantity==null || !Contents.transform.IsChildOf(transform))
                throw new InvalidOperationException("Окно контейнера: не назначены содержимое или кнопки.");
            if(Close.GetComponent<Image>()?.sprite==null || Quantity.GetComponent<Image>()?.sprite==null)
                throw new InvalidOperationException("Окно контейнера: не сохранена графика кнопок.");
            Contents.Validate();
            if(Names?.Length!=9 || Counts?.Length!=9) throw new InvalidOperationException("Окно контейнера: нужны отдельные названия и количества девяти стопок.");
            for(int i=0;i<9;i++)
                if(Names[i]==null || Counts[i]==null || Names[i]==Counts[i] || !Names[i].transform.IsChildOf(Contents.Slots[i].transform) || !Counts[i].transform.IsChildOf(Contents.Slots[i].transform))
                    throw new InvalidOperationException("Окно контейнера: повреждены подписи ячейки "+i);
        }
        public static ContainerWindowView Create(Transform parent)
        {
            var source=Resources.Load<ContainerWindowView>("Presentation/ContainerWindow");
            if(source==null) throw new InvalidOperationException("Нет префаба Presentation/ContainerWindow.");
            source.Validate();return Instantiate(source,parent,false);
        }
    }
}
