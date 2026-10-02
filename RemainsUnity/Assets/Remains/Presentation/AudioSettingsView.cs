using System;
using UnityEngine;
using UnityEngine.UI;
namespace Remains.Presentation
{
    public sealed class AudioSettingsView : MonoBehaviour
    {
        [InspectorName("Общая громкость")] public Slider Master;
        [InspectorName("Музыка")] public Slider Music;
        [InspectorName("Игровые звуки")] public Slider Effects;
        [InspectorName("Подпись общей громкости")] public Text MasterLabel;
        [InspectorName("Подпись музыки")] public Text MusicLabel;
        [InspectorName("Подпись игровых звуков")] public Text EffectsLabel;
        [InspectorName("Сообщение")] public Text Notice;
        [InspectorName("Сохранить")] public Button Save;
        [InspectorName("Отмена")] public Button Cancel;
        public void Validate()
        {
            if(Master==null || Music==null || Effects==null || MasterLabel==null || MusicLabel==null || EffectsLabel==null || Notice==null || Save==null || Cancel==null)
                throw new InvalidOperationException($"Настройки звука {name}: не заполнены ссылки элементов.");
            if(Master==Music || Master==Effects || Music==Effects || Save==Cancel)
                throw new InvalidOperationException($"Настройки звука {name}: повторные ссылки регуляторов или кнопок.");
            foreach(var slider in new[]{Master,Music,Effects})
                if(!slider.transform.IsChildOf(transform) || slider.handleRect==null || slider.minValue!=0 || slider.maxValue!=100 || !slider.wholeNumbers)
                    throw new InvalidOperationException($"Настройки звука {name}: регулятор {slider.name} должен иметь рукоятку и целый диапазон 0–100.");
        }
        public static AudioSettingsView Create(Transform parent)
        {
            var prefab=Resources.Load<AudioSettingsView>("Presentation/AudioSettings");
            if(prefab==null) throw new InvalidOperationException("Нет префаба Presentation/AudioSettings.");
            prefab.Validate();return Instantiate(prefab,parent,false);
        }
    }
}
