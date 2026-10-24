using System;
using System.Collections.Generic;
using Remains.Domain;
using UnityEngine;
using UnityEngine.UI;
using Remains.Content;

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
            => ShowChoice(index,worldName,holder,revision,null,null,relation,selected,interactable);
        public void ShowChoice(int index,string worldName,string holder,long revision,string branch,string snapshot,string relation,bool selected,bool interactable)
        {
            var button=Choices[index];
            var label=button.GetComponentInChildren<Text>();
            label.text=ChoiceText(worldName,holder,revision,branch,snapshot,relation);
            button.interactable=interactable;OriginalButtonStyle.SetSelected(button,selected);
        }
        public static string ChoiceText(string worldName,string holder,long revision,string branch,string snapshot,string relation)
            => $"{worldName}\nКопия у {holder} · состояние №{revision}\nВетвь {branch??"не указана"} · снимок {snapshot??"не указан"}\n{relation}";
        // Abbreviations distinguish all advertised values, including deliberately shared prefixes.
        // The panel calls this only when offers/selection change, never in an Update loop.
        public static string IdentityLabel(string id,IReadOnlyList<string> advertised)
        {
            if(string.IsNullOrEmpty(id)) return "нет данных";
            if(advertised==null) throw new ArgumentNullException(nameof(advertised));
            int length=Math.Min(12,id.Length);
            while(length<id.Length)
            {
                string prefix=id.Substring(0,length);
                bool shared=false;
                foreach(string other in advertised)
                    if(other!=null && other!=id && other.StartsWith(prefix,StringComparison.Ordinal)) {shared=true;break;}
                if(!shared) break;
                length++;
            }
            return id.Substring(0,length);
        }
        public static string RelationText(WorldRelation relation,bool selected) => selected?"Выбрана для запуска":relation switch {
            WorldRelation.Identical=>"Такой же снимок, как выбранный",
            WorldRelation.Newer=>"Продолжение выбранной копии",
            WorldRelation.Older=>"Предыдущая копия выбранного продолжения",
            WorldRelation.Divergent=>"Разошедшаяся ветвь — прогресс не объединяется",
            WorldRelation.DifferentWorld=>"Другой мир",
            _=>"Связь копий не подтверждена"
        };
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
            var prefab=GameAssets.Load<WorldLobbyView>("Presentation/WorldLobby");
            if(prefab==null) throw new InvalidOperationException("Нет префаба Presentation/WorldLobby.");
            prefab.Validate();var view=Instantiate(prefab,parent,false);
            view.Contents.SetActive(showChoices);return view;
        }
    }
}
