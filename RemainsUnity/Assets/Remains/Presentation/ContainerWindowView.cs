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
        public void Validate()
        {
            if(Contents==null || Close==null || Quantity==null || !Contents.transform.IsChildOf(transform))
                throw new InvalidOperationException("Окно контейнера: не назначены содержимое или кнопки.");
            if(Close.GetComponent<Image>()?.sprite==null || Quantity.GetComponent<Image>()?.sprite==null)
                throw new InvalidOperationException("Окно контейнера: не сохранена графика кнопок.");
            Contents.Validate();
        }
        public static ContainerWindowView Create(Transform parent)
        {
            var source=Resources.Load<ContainerWindowView>("Presentation/ContainerWindow");
            if(source==null) throw new InvalidOperationException("Нет префаба Presentation/ContainerWindow.");
            source.Validate();return Instantiate(source,parent,false);
        }
    }
}
