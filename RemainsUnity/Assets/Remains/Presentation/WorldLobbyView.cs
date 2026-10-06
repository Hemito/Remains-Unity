using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    public sealed class WorldLobbyView : MonoBehaviour
    {
        [InspectorName("Обычный список")] public GameObject Contents;
        [InspectorName("Участники и подсказка")] public Text Instruction;
        [InspectorName("Копии мира")] public Button[] Choices;
        [InspectorName("Страница")] public Text Page;
        [InspectorName("Предыдущая страница")] public Button Previous;
        [InspectorName("Следующая страница")] public Button Next;
        [InspectorName("Новый мир")] public Button NewWorld;
        [InspectorName("Состояние загрузки / ошибка")] public Text Status;
        [InspectorName("Начать игру")] public Button StartGame;
        [InspectorName("Отключиться")] public Button Disconnect;
        public void ShowChoice(int index,string worldName,string holder,long revision,string relation,bool selected,bool interactable)
        {
            var button=Choices[index];
            button.GetComponentInChildren<Text>().text=$"{worldName}\nКопия у {holder} · ревизия {revision}\n{relation}";
            button.interactable=interactable;OriginalButtonStyle.SetSelected(button,selected);
        }
        public void ShowWaiting(string instruction,string status)
        {
            Contents.SetActive(true);
            foreach(var button in Choices) button.gameObject.SetActive(false);
            Previous.gameObject.SetActive(false);Page.gameObject.SetActive(false);Next.gameObject.SetActive(false);
            NewWorld.gameObject.SetActive(false);StartGame.gameObject.SetActive(false);
            Instruction.text=instruction;Status.text=status??"";Disconnect.gameObject.SetActive(true);
        }
        public void Validate()
        {
            if(Contents==null || Instruction==null || Choices==null || Choices.Length==0 || Page==null || Status==null)
                throw new InvalidOperationException($"Лобби {name}: не заполнены ссылки на список и подписи.");
            var unique=new HashSet<Button>();
            foreach(var button in Choices) Check(button);
            foreach(var button in new[]{Previous,Next,NewWorld,StartGame,Disconnect}) Check(button);
            void Check(Button button)
            {
                if(button==null || !unique.Add(button) || !button.transform.IsChildOf(Contents.transform) || button.GetComponentInChildren<Text>(true)==null)
                    throw new InvalidOperationException($"Лобби {name}: отсутствующая, внешняя или повторная кнопка.");
            }
        }
        public static WorldLobbyView Create(Transform parent,bool showChoices=true)
        {
            var prefab=Resources.Load<WorldLobbyView>("Presentation/WorldLobby");
            if(prefab==null) throw new InvalidOperationException("Нет префаба Presentation/WorldLobby.");
            prefab.Validate();var view=Instantiate(prefab,parent,false);
            view.Contents.SetActive(showChoices);return view;
        }
    }
}
