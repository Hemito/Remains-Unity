using System;
using UnityEngine;
using UnityEngine.UI;
namespace Remains.Presentation
{
    public sealed class PersonalInventoryView : MonoBehaviour
    {
        [InspectorName("Строки предметов")] public Button[] Rows;
        [InspectorName("Иконки строк")] public Image[] Icons;
        [InspectorName("Кнопки оружия")] public Button[] Weapons;
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
        [InspectorName("Заголовок")] public Text Title;
        [InspectorName("Описание предмета")] public Text Detail;
        [InspectorName("Состояние действия")] public Text ActionStatus;
        public void Validate()
        {
            if(Rows==null || Icons==null || Rows.Length==0 || Rows.Length!=Icons.Length || Weapons==null || WeaponIds==null || Weapons.Length!=WeaponIds.Length ||
                DetailIcon==null || WeaponPreview==null || WeaponDetail==null || Portrait==null || PortraitParts==null || PortraitParts.Length==0 || Previous==null || Next==null || Deposit==null || Use==null || Title==null || Detail==null || ActionStatus==null)
                throw new InvalidOperationException("Личный инвентарь: не заполнены ссылки или не совпадают массивы элементов.");
            if(Array.Exists(Rows,x=>x==null) || Array.Exists(Icons,x=>x==null) || Array.Exists(Weapons,x=>x==null) || Array.Exists(WeaponIds,string.IsNullOrWhiteSpace) || Array.Exists(PortraitParts,x=>x==null))
                throw new InvalidOperationException("Личный инвентарь: пустой элемент списка, оружия или портрета.");
            foreach(var part in PortraitParts) part.Validate();
        }
    }
}
