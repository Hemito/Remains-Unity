using System;
using UnityEngine;
using UnityEngine.UI;
namespace Remains.Presentation
{
    public sealed class WorldManagementView : MonoBehaviour
    {
        [InspectorName("Название мира")] public InputField Name;
        [InspectorName("Сообщение / подтверждение")] public Text Status;
        [InspectorName("Сохранить имя")] public Button Rename;
        [InspectorName("Удалить мир")] public Button Delete;
        [InspectorName("Закрыть")] public Button Close;
        public void Validate()
        {
            if(Name==null || Status==null || Rename==null || Delete==null || Close==null || Name.textComponent==null)
                throw new InvalidOperationException($"Управление миром {name}: отсутствуют ссылки элементов.");
            if(Rename==Delete || Rename==Close || Delete==Close)
                throw new InvalidOperationException($"Управление миром {name}: кнопки действий должны быть разными.");
            foreach(var component in new Component[]{Name,Status,Rename,Delete,Close})
                if(!component.transform.IsChildOf(transform)) throw new InvalidOperationException($"Управление миром {name}: элемент {component.name} вне окна.");
        }
        public static WorldManagementView Create(Transform parent)
        {
            var prefab=Resources.Load<WorldManagementView>("Presentation/WorldManagement");
            if(prefab==null) throw new InvalidOperationException("Нет префаба Presentation/WorldManagement.");
            prefab.Validate();return Instantiate(prefab,parent,false);
        }
    }
}
