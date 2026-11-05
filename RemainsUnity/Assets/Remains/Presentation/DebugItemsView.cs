using System;
using Remains.Content;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    // The hidden item menu's prefab references (Editor: DebugItemsAuthoring).
    public sealed class DebugItemsView : MonoBehaviour
    {
        [InspectorName("Панель")] public GameObject Panel;
        [InspectorName("Поиск")] public InputField Search;
        [InspectorName("Содержимое списка")] public RectTransform Content;
        [InspectorName("Образец строки")] public DebugItemRow Row;
        [InspectorName("Вкладки: все, затем категории")] public Button[] Tabs;
        [InspectorName("Закрыть")] public Button Close;
        [InspectorName("Сводка")] public Text Status;
        public void Validate()
        {
            if(Panel==null || Search==null || Content==null || Row==null || Close==null || Status==null || Tabs==null ||
                Tabs.Length!=Remains.Domain.DebugItems.Categories.Length+1 || Array.Exists(Tabs,t=>t==null))
                throw new InvalidOperationException($"Меню выдачи {name}: не назначены ссылки.");
            Row.Validate();
        }
        public static DebugItemsView Create(Transform parent)
        {
            var prefab=GameAssets.Load<DebugItemsView>("Presentation/DebugItems");
            if(prefab==null) throw new InvalidOperationException("Нет префаба Presentation/DebugItems.");
            prefab.Validate();return Instantiate(prefab,parent,false);
        }
    }
}
