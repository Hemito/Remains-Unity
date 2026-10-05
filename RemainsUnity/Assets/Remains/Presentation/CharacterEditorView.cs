using System;
using UnityEngine;
using UnityEngine.UI;
namespace Remains.Presentation
{
    public sealed class CharacterEditorView : MonoBehaviour
    {
        [Header("Имя и внешний вид")]
        [InspectorName("Имя персонажа")] public InputField Name;
        [InspectorName("Превью персонажа")] public CharacterPortraitView Portrait;
        [InspectorName("Предыдущая причёска")] public Button HairPrevious;
        [InspectorName("Следующая причёска")] public Button HairNext;
        [InspectorName("Предыдущие глаза")] public Button EyePrevious;
        [InspectorName("Следующие глаза")] public Button EyeNext;
        [InspectorName("Номер причёски")] public Text HairLabel;
        [InspectorName("Номер глаз")] public Text EyeLabel;
        [Header("Цвета")]
        [InspectorName("Цветовые области")] public Button[] Colors;
        [InspectorName("Образцы областей")] public Image[] ColorSamples;
        [InspectorName("Выбранная область")] public Text ColorLabel;
        [InspectorName("Цвет RGB")] public InputField Hex;
        [InspectorName("Палитра цветов")] public Button[] Swatches;
        [InspectorName("Каналы RGB")] public Slider[] Channels;
        [InspectorName("Значения RGB")] public Text[] ChannelValues;
        [InspectorName("Включение второго цвета")] public Toggle SecondaryEnabled;
        [Header("Действия")]
        [InspectorName("Создать")] public Button Create;
        [InspectorName("Отмена")] public Button Cancel;
        [InspectorName("Исходная внешность")] public Button Reset;
        [InspectorName("Состояние создания")] public Text Status;
        public void Validate()
        {
            if(Name==null || Hex==null || Portrait==null || HairLabel==null || EyeLabel==null || ColorLabel==null || Status==null ||
                HairPrevious==null || HairNext==null || EyePrevious==null || EyeNext==null || Create==null || Cancel==null || Reset==null ||
                Colors?.Length!=5 || ColorSamples?.Length!=5 || Swatches?.Length!=12 || Channels?.Length!=3 || ChannelValues?.Length!=3 || SecondaryEnabled==null)
                throw new InvalidOperationException("Редактор персонажа: не заполнены элементы интерфейса.");
            for(int i=0;i<5;i++) if(Colors[i]==null || ColorSamples[i]==null) throw new InvalidOperationException("Нет выбора цвета: "+i);
            for(int i=0;i<3;i++) if(Channels[i]==null || ChannelValues[i]==null) throw new InvalidOperationException("Нет цветового канала: "+i);
            foreach(var swatch in Swatches) if(swatch==null) throw new InvalidOperationException("Нет образца цвета.");
            Portrait.Validate();
        }
        public static CharacterEditorView CreateView(Transform parent)
        {
            var prefab=Resources.Load<CharacterEditorView>("Presentation/CharacterEditor");
            if(prefab==null) throw new InvalidOperationException("Нет префаба Presentation/CharacterEditor.");
            prefab.Validate();return Instantiate(prefab,parent,false);
        }
    }
}
