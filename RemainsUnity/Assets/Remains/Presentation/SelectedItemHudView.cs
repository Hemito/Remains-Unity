using System;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    // Saved artwork/layout only; Counter retains the established CombatHud API.
    public sealed class SelectedItemHudView : MonoBehaviour
    {
        [InspectorName("Изображение выбранной вещи")] public Image Icon;
        [InspectorName("Иконка боеприпасов")] public Image AmmoIcon;
        [InspectorName("Название выбранной вещи")] public Text ItemName;
        [InspectorName("Количество / боезапас")] public Text Counter;
        [InspectorName("Действие / причина недоступности")] public Text ActionState;

        public void Validate()
        {
            if(Icon==null || AmmoIcon==null || ItemName==null || Counter==null || ActionState==null)
                throw new InvalidOperationException($"HUD выбранной вещи {name}: отсутствуют ссылки Icon/AmmoIcon/ItemName/Counter/ActionState.");
            if(Icon==AmmoIcon || ItemName==Counter || ItemName==ActionState || Counter==ActionState)
                throw new InvalidOperationException($"HUD выбранной вещи {name}: изображения и текстовые поля должны иметь отдельные сохранённые ссылки.");
        }
    }
}
