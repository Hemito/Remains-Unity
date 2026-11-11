using System;
using System.Collections.Generic;
using Remains.Persistence;
using UnityEngine;
using UnityEngine.UI;
namespace Remains.Presentation
{
    public sealed class CharacterLibraryPanel : MonoBehaviour
    {
        private CharacterLibraryView view;
        private CharacterLibrary library;
        private InventoryArtwork artwork;
        private BackgroundSaveOperation operation;
        private Func<string,bool> select;
        private Action closed;
        private Action<string> deleted;
        private string confirming;
        private List<CharacterLibraryEntry> entries;
        private string selected;
        private int page;
        private CharacterEditorPanel editor;
        public CharacterLibraryView View=>view;
        public CharacterEditorPanel Editor=>editor;
        public bool Ready=>entries!=null && !operation.Busy;
        public static CharacterLibraryPanel Open(Transform parent,CharacterLibrary library,InventoryArtwork artwork,string selected,Func<string,bool> select,Action closed,Action<string> deleted=null)
        {
            var view=CharacterLibraryView.CreateView(parent);var panel=view.gameObject.AddComponent<CharacterLibraryPanel>();
            panel.deleted=deleted;panel.Initialize(view,library,artwork,selected,select,closed);return panel;
        }
        public void Initialize(CharacterLibraryView value,CharacterLibrary saves,InventoryArtwork graphics,string current,Func<string,bool> choose,Action close)
        {
            view=value;library=saves;artwork=graphics;selected=current;select=choose;closed=close;operation=gameObject.AddComponent<BackgroundSaveOperation>();
            view.Close.onClick.AddListener(Close);view.Create.onClick.AddListener(Create);
            view.Previous.onClick.AddListener(()=>{page--;Render();});view.Next.onClick.AddListener(()=>{page++;Render();});Reload(showSelected:true);
        }
        private void Reload(bool showSelected=false)
        {
            entries=null;view.Status.text=Remains.Content.GameLocalization.UiText("source.CharacterLibraryPanel.a1f19450576c3223", "Чтение персонажей…", "RemainsUnity/Assets/Remains/Presentation/CharacterLibraryPanel.cs");view.Page.text="";
            foreach(var card in view.Cards) card.gameObject.SetActive(false);
            foreach(var button in view.Delete) button.gameObject.SetActive(false);
            view.Create.interactable=false;view.Previous.interactable=view.Next.interactable=false;
            operation.Run(token=>library.Read(token),result=>
            {
                entries=result;
                if(showSelected)
                {
                    int index=entries.FindIndex(entry=>entry.name==selected && entry.error==null);
                    if(index>=0) page=index/view.Cards.Length;
                }
                foreach(var entry in entries) if(entry.error!=null) Debug.LogWarning(entry.error);
                Render();
            },error=>
            {view.Status.text=Remains.Content.GameLocalization.UiText("source.CharacterLibraryPanel.8a76de2d10b570dc", "Не удалось прочитать персонажей: ", "RemainsUnity/Assets/Remains/Presentation/CharacterLibraryPanel.cs")+error.Message;Debug.LogError(error);});
        }
        public static string DescribeTime(CharacterTimeRecord time)
        {
            if(time==null) return Remains.Content.GameLocalization.UiText("source.CharacterLibraryPanel.a503c8d9b09fa5a7", "Время ещё не учтено", "RemainsUnity/Assets/Remains/Presentation/CharacterLibraryPanel.cs");
            var span=TimeSpan.FromSeconds(time.seconds);
            return Remains.Content.GameLocalization.Format("source.CharacterLibraryPanel.8ab0b10e606f296f", $"{(time.completeHistory?Remains.Content.GameLocalization.UiText("source.CharacterLibraryPanel.8edc541e884866db", "В игре", "RemainsUnity/Assets/Remains/Presentation/CharacterLibraryPanel.cs"):Remains.Content.GameLocalization.UiText("source.CharacterLibraryPanel.bbc75acb96ced58a", "Учтено", "RemainsUnity/Assets/Remains/Presentation/CharacterLibraryPanel.cs"))}: {(long)span.TotalHours} ч {span.Minutes:00} мин", "RemainsUnity/Assets/Remains/Presentation/CharacterLibraryPanel.cs");
        }
        private void Render()
        {
            int capacity=view.Cards.Length,pages=Math.Max(1,(entries.Count+capacity-1)/capacity);page=Math.Clamp(page,0,pages-1);
            for(int row=0;row<capacity;row++)
            {
                int index=page*capacity+row;var button=view.Cards[row];button.onClick.RemoveAllListeners();button.gameObject.SetActive(index<entries.Count);
                var remove=view.Delete[row];remove.onClick.RemoveAllListeners();remove.gameObject.SetActive(index<entries.Count);
                if(index>=entries.Count) continue;
                var entry=entries[index];view.Names[row].text=entry.name;view.Times[row].text=entry.error==null?DescribeTime(entry.time):Remains.Content.GameLocalization.UiText("source.CharacterLibraryPanel.9638cbe5246ca34a", "Ошибка профиля · журнал игры", "RemainsUnity/Assets/Remains/Presentation/CharacterLibraryPanel.cs");
                view.Portraits[row].Show(entry.actor,artwork);button.interactable=entry.error==null;
                OriginalButtonStyle.SetSelected(button,entry.name==selected);
                button.onClick.AddListener(()=>{confirming=null;if(select(entry.name)) Close();});
                remove.GetComponentInChildren<Text>().text=confirming==entry.name?Remains.Content.GameLocalization.UiText("source.CharacterLibraryPanel.54c48a6a1d37bc48", "Точно?", "RemainsUnity/Assets/Remains/Presentation/CharacterLibraryPanel.cs"):Remains.Content.GameLocalization.UiText("source.CharacterLibraryPanel.be99b13612015a48", "Удалить", "RemainsUnity/Assets/Remains/Presentation/CharacterLibraryPanel.cs");
                remove.onClick.AddListener(()=>Delete(entry.name));
            }
            view.Create.interactable=true;view.Status.text=entries.Count==0?Remains.Content.GameLocalization.UiText("source.CharacterLibraryPanel.0c34ac9569ae057d", "Создайте первого персонажа.", "RemainsUnity/Assets/Remains/Presentation/CharacterLibraryPanel.cs"):entries.Exists(entry=>entry.error!=null)?Remains.Content.GameLocalization.UiText("source.CharacterLibraryPanel.c31a75b8dd84544b", "Повреждённые профили недоступны. Подробности — в журнале игры.", "RemainsUnity/Assets/Remains/Presentation/CharacterLibraryPanel.cs"):Remains.Content.GameLocalization.UiText("source.CharacterLibraryPanel.2d36c0d74a097d81", "Выберите персонажа для своей игры или подключения к другу.", "RemainsUnity/Assets/Remains/Presentation/CharacterLibraryPanel.cs");
            view.Page.text=$"{page+1} / {pages}";view.Previous.interactable=page>0;view.Next.interactable=page+1<pages;
            if(confirming!=null) view.Status.text=Remains.Content.GameLocalization.Format("source.CharacterLibraryPanel.b7c11707a30af3c8", $"Удалить персонажа «{confirming}»? Копия останется в папке DeletedCharacters, миры не изменятся.", "RemainsUnity/Assets/Remains/Presentation/CharacterLibraryPanel.cs");
        }
        private void Delete(string name)
        {
            if(operation.Busy || editor!=null) return;
            if(confirming!=name) {confirming=name;Render();return;}
            confirming=null;view.Status.text=Remains.Content.GameLocalization.UiText("source.CharacterLibraryPanel.5988b5c3983b13d9", "Удаление персонажа…", "RemainsUnity/Assets/Remains/Presentation/CharacterLibraryPanel.cs");
            operation.Run(_=>library.Delete(name),_=>
            {
                if(selected==name) selected=null;
                deleted?.Invoke(name);Reload();
            },error=>
            {view.Status.text=Remains.Content.GameLocalization.UiText("source.CharacterLibraryPanel.5ff69b0f96e27142", "Не удалось удалить персонажа: ", "RemainsUnity/Assets/Remains/Presentation/CharacterLibraryPanel.cs")+error.Message;Debug.LogError($"Удаление персонажа {name}: {error}");Reload();});
        }
        private void Create()
        {
            if(operation.Busy || editor!=null) return;
            view.gameObject.SetActive(false);
            editor=CharacterEditorPanel.Open(transform.parent,library,artwork,entry=>
            {
                editor=null;view.gameObject.SetActive(true);selected=entry.name;
                select(entry.name);Reload();
            },()=>{editor=null;view.gameObject.SetActive(true);});
        }
        public void Close()
        {
            if(editor!=null) {editor.Cancel();return;}
            closed();Destroy(gameObject);
        }
        private void OnDestroy() {if(editor!=null) Destroy(editor.gameObject);}
    }
}
