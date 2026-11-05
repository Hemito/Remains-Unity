using System;
using UnityEngine;
using UnityEngine.UI;
using Remains.Content;

namespace Remains.Presentation
{
    public sealed class WorldMapView : MonoBehaviour
    {
        [InspectorName("Карта оригинала")] public Image Map;
        [InspectorName("Основной завод на карте")] public Button Factory;
        [InspectorName("Описание места")] public Text Description;
        [InspectorName("Номер завода")] public InputField Index;
        [InspectorName("Номер меньше")] public Button Previous;
        [InspectorName("Номер больше")] public Button Next;
        [InspectorName("Отправиться")] public Button Travel;
        [InspectorName("Закрыть")] public Button Close;
        public void Validate()
        {
            if(Map==null || Map.sprite==null || Factory==null || Description==null || Index==null || Previous==null || Next==null || Travel==null || Close==null)
                throw new InvalidOperationException("Карта мира: отсутствуют изображения или ссылки интерфейса.");
        }
        public static WorldMapView Create(Transform parent)
        {
            var prefab=GameAssets.Load<WorldMapView>("Presentation/WorldMap");
            if(prefab==null) throw new InvalidOperationException("Не найден сохранённый префаб Presentation/WorldMap.");
            prefab.Validate();return Instantiate(prefab,parent,false);
        }
    }
}
