using System;
using UnityEngine;
using UnityEngine.UI;
namespace Remains.Presentation
{
    public sealed class MainMenuView : MonoBehaviour
    {
        [InspectorName("Панель подключения")] public GameObject Controls;
        [InspectorName("Имя персонажа")] public InputField Character;
        [InspectorName("Адрес сервера")] public InputField Address;
        [InspectorName("Создать сессию")] public Button Host;
        [InspectorName("Сохранённые миры")] public Button Worlds;
        [InspectorName("Подключиться")] public Button Join;
        [InspectorName("Настройки звука")] public Button Settings;
        [InspectorName("Новая игра")] public Button NewGame;
        [InspectorName("Выход")] public Button Exit;
        [InspectorName("Описание мира")] public Text World;
        [InspectorName("Уведомление")] public Text Notice;
        public void Validate()
        {
            if(Controls==null || Character==null || Address==null || Host==null || Worlds==null || Join==null || Settings==null ||
                NewGame==null || Exit==null || World==null || Notice==null)
                throw new InvalidOperationException($"Главное меню {name}: отсутствуют ссылки на элементы интерфейса.");
        }
    }
}
