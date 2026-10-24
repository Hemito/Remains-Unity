using System;
using UnityEngine;
using UnityEngine.UI;
using Remains.Content;

namespace Remains.Presentation
{
    public sealed class SessionHudView : MonoBehaviour
    {
        [InspectorName("Локация и участники")] public Text Status;
        [InspectorName("Открыть инвентарь")] public Button Inventory;
        [InspectorName("Открыть меню")] public Button Menu;
        public void Validate()
        {
            if(Status==null || Inventory==null || Menu==null)
                throw new InvalidOperationException("Панель сессии: не назначены текст состояния или кнопки.");
            foreach(var button in new[]{Inventory,Menu})
                if(button.GetComponent<Image>()?.sprite==null)
                    throw new InvalidOperationException($"Панель сессии: у кнопки {button.name} отсутствует сохранённая графика.");
        }
        public static SessionHudView Create(Transform parent)
        {
            var prefab=GameAssets.Load<SessionHudView>("Presentation/SessionHud");
            if(prefab==null) throw new InvalidOperationException("Нет префаба панели сессии Presentation/SessionHud.");
            prefab.Validate();return Instantiate(prefab,parent,false);
        }
    }
}
