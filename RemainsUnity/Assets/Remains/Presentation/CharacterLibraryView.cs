using System;
using UnityEngine;
using UnityEngine.UI;
using Remains.Content;
namespace Remains.Presentation
{
    public sealed class CharacterLibraryView : MonoBehaviour
    {
        [Header("Карточки персонажей")]
        [InspectorName("Выбор персонажа")] public Button[] Cards;
        [InspectorName("Портреты")] public CharacterPortraitView[] Portraits;
        [InspectorName("Имена")] public Text[] Names;
        [InspectorName("Игровое время")] public Text[] Times;
        [Header("Действия и состояние")]
        [InspectorName("Создать персонажа")] public Button Create;
        [InspectorName("Назад")] public Button Close;
        [InspectorName("Предыдущая страница")] public Button Previous;
        [InspectorName("Следующая страница")] public Button Next;
        [InspectorName("Состояние списка")] public Text Status;
        [InspectorName("Номер страницы")] public Text Page;
        public void Validate()
        {
            if(Cards==null || Cards.Length==0 || Portraits?.Length!=Cards.Length || Names?.Length!=Cards.Length || Times?.Length!=Cards.Length ||
                Create==null || Close==null || Previous==null || Next==null || Status==null || Page==null)
                throw new InvalidOperationException("Список персонажей: отсутствуют ссылки интерфейса.");
            for(int i=0;i<Cards.Length;i++)
            {if(Cards[i]==null || Portraits[i]==null || Names[i]==null || Times[i]==null) throw new InvalidOperationException("Повреждена карточка персонажа: "+i);Portraits[i].Validate();}
        }
        public static CharacterLibraryView CreateView(Transform parent)
        {
            var prefab=GameAssets.Load<CharacterLibraryView>("Presentation/CharacterLibrary");
            if(prefab==null) throw new InvalidOperationException("Нет префаба Presentation/CharacterLibrary.");
            prefab.Validate();return Instantiate(prefab,parent,false);
        }
    }
}
