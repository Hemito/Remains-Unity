using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
namespace Remains.Presentation
{
    public sealed class PersonalInventoryView : MonoBehaviour, IScrollHandler
    {
        [InspectorName("Вкладки: все / оружие / боеприпасы / предметы / броня")] public Button[] Categories;
        [InspectorName("Строки предметов")] public Button[] Rows;
        [InspectorName("Иконки строк")] public Image[] Icons;
        [InspectorName("Назначения клавиш")] public Button[] Weapons;
        [InspectorName("ID оружия")] public string[] WeaponIds;
        [InspectorName("Иконка выбранного предмета")] public Image DetailIcon;
        [InspectorName("Широкий просмотр оружия")] public Image WeaponPreview;
        [InspectorName("Описание выбранного оружия")] public Text WeaponDetail;
        [InspectorName("Портрет")] public GameObject Portrait;
        [InspectorName("Части портрета")] public PortraitPart[] PortraitParts;
        [InspectorName("Предыдущая страница")] public Button Previous;
        [InspectorName("Следующая страница")] public Button Next;
        [InspectorName("Передать в контейнер")] public Button Deposit;
        [InspectorName("Использовать")] public Button Use;
        [InspectorName("Описание предмета")] public Text Detail;
        [InspectorName("Состояние действия")] public Text ActionStatus;
        public event Action<int> ScrollRows;
        [InspectorName("Прокрутка списка")] public Scrollbar Scrollbar;
        public void OnScroll(PointerEventData eventData)
        {
            if(!isActiveAndEnabled || eventData==null || eventData.scrollDelta.y==0) return;
            if(ScrollRows==null) {Debug.LogWarning("Личный инвентарь: не подключена прокрутка списка.",this);return;}
            ScrollRows.Invoke(eventData.scrollDelta.y<0?1:-1);eventData.Use();
        }
        public void Validate()
        {
            if(Scrollbar==null || Scrollbar.handleRect==null || Scrollbar.direction!=UnityEngine.UI.Scrollbar.Direction.TopToBottom)
                throw new InvalidOperationException("Личный инвентарь: нужна вертикальная полоса прокрутки сверху вниз с ползунком.");
            if(Categories==null || Categories.Length!=Enum.GetValues(typeof(InventoryCategory)).Length || Array.Exists(Categories,x=>x==null))
                throw new InvalidOperationException("Личный инвентарь: не назначены все вкладки, включая пустую вкладку брони.");
            if(Rows==null || Icons==null || Rows.Length==0 || Rows.Length!=Icons.Length || Weapons==null || WeaponIds==null || Weapons.Length!=WeaponIds.Length ||
                DetailIcon==null || WeaponPreview==null || WeaponDetail==null || Portrait==null || PortraitParts==null || PortraitParts.Length==0 || Previous==null || Next==null || Deposit==null || Use==null || Detail==null || ActionStatus==null)
                throw new InvalidOperationException("Личный инвентарь: не заполнены ссылки или не совпадают массивы элементов.");
            if(Array.Exists(Rows,x=>x==null) || Array.Exists(Icons,x=>x==null) || Array.Exists(Weapons,x=>x==null) || Array.Exists(WeaponIds,string.IsNullOrWhiteSpace) || Array.Exists(PortraitParts,x=>x==null))
                throw new InvalidOperationException("Личный инвентарь: пустой элемент списка, оружия или портрета.");
            foreach(var part in PortraitParts) part.Validate();
        }
    }
}
