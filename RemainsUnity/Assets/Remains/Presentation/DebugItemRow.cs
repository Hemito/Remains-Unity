using System;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    public sealed class DebugItemRow : MonoBehaviour
    {
        [InspectorName("Кнопка")] public Button Button;
        [InspectorName("Иконка")] public Image Icon;
        [InspectorName("Название")] public Text Name;
        public void Validate()
        {
            if(Button==null || Icon==null || Name==null) throw new InvalidOperationException($"Строка выдачи {name}: не назначены ссылки.");
        }
    }
}
