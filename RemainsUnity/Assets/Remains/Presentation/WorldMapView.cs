using System;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    public sealed class WorldMapView : MonoBehaviour
    {
        [InspectorName("Карта оригинала")] public Image Map;
        [InspectorName("Основной завод на карте")] public Button Factory;
        [InspectorName("Описание места")] public Text Description;
        [InspectorName("Отправиться")] public Button Travel;
        [InspectorName("Закрыть")] public Button Close;
        public void Validate()
        {
            if(Map==null || Map.sprite==null || Factory==null || Description==null || Travel==null || Close==null)
                throw new InvalidOperationException("Карта мира: отсутствуют изображения или ссылки интерфейса.");
        }
        public static WorldMapView Create(Transform parent)
        {
            var prefab=Resources.Load<WorldMapView>("Presentation/WorldMap");
            if(prefab==null) throw new InvalidOperationException("Не найден сохранённый префаб Presentation/WorldMap.");
            prefab.Validate();return Instantiate(prefab,parent,false);
        }
    }
}
