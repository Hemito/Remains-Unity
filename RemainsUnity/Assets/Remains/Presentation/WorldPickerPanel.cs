using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Domain;
using Remains.Persistence;
using UnityEngine;

namespace Remains.Presentation
{
    public sealed class WorldPickerPanel : MonoBehaviour
    {
        private WorldPickerView view;
        private BackgroundSaveOperation operation;
        private SaveStore store;
        private WorldCatalogResult catalog;
        private Dictionary<string,string> names;
        private WorldCopyLabels labels;
        private Dictionary<string,RectTransform> choices;
        private Action changed;
        private Action<string> notice;
        private int page;
        public bool Ready=>catalog!=null && !operation.Busy;
        public bool Busy=>operation.Busy;
        public void Initialize(WorldPickerView value,SaveStore saves,int initialPage,
            Dictionary<string,RectTransform> rects,Action saved,Action<string> message)
        {
            view=value;store=saves;page=initialPage;choices=rects;changed=saved;notice=message;
            operation=gameObject.AddComponent<BackgroundSaveOperation>();
            view.Close.onClick.AddListener(()=>Destroy(gameObject));
            view.Previous.onClick.AddListener(()=>{page--;Render();});
            view.Next.onClick.AddListener(()=>{page++;Render();});Reload(showSelected:true);
        }
        private void Loading(string text)
        {
            choices.Clear();view.Empty.text=text;view.Empty.gameObject.SetActive(true);
            view.Warning.gameObject.SetActive(false);
            foreach(var button in view.Choices) button.gameObject.SetActive(false);
            foreach(var button in view.Manage) button.gameObject.SetActive(false);
            view.Previous.interactable=false;view.Next.interactable=false;view.Page.text="";
        }
        private void Reload(bool showSelected=false)
        {
            catalog=null;Loading(Remains.Content.GameLocalization.UiText("source.WorldPickerPanel.58f4ad70bc6479eb", "Чтение списка миров…", "RemainsUnity/Assets/Remains/Presentation/WorldPickerPanel.cs"));
            operation.Run(token=>(store.Catalog.Read(token),new WorldLibrary(store).Names()),result=>
            {
                catalog=result.Item1;names=result.Item2;
                if(showSelected && catalog.selected!=null)
                {
                    int selected=catalog.entries.FindIndex(entry=>entry.summary.snapshot==catalog.selected.snapshot);
                    if(selected>=0) page=selected/view.Choices.Length;
                }
                labels=new WorldCopyLabels(catalog.entries.Select(entry=>entry.summary.branchId).Append(catalog.selected?.branchId),
                    catalog.entries.Select(entry=>entry.summary.snapshot).Append(catalog.selected?.snapshot));
                foreach(string warning in catalog.warnings) Debug.LogWarning(warning);
                Render();
            },error=>
            {Loading(Remains.Content.GameLocalization.UiText("source.WorldPickerPanel.b240a25f974e864d", "Не удалось прочитать список миров. Подробности — в журнале игры.", "RemainsUnity/Assets/Remains/Presentation/WorldPickerPanel.cs"));Debug.LogError("Список миров "+store.WorldDirectoryPath+": "+error);});
        }
        private void Render()
        {
            choices.Clear();int capacity=view.Choices.Length;
            int pages=Math.Max(1,(catalog.entries.Count+capacity-1)/capacity);page=Math.Clamp(page,0,pages-1);
            for(int row=0;row<capacity;row++)
            {
                int index=page*capacity+row;bool visible=index<catalog.entries.Count;
                var choice=view.Choices[row];var manage=view.Manage[row];
                choice.onClick.RemoveAllListeners();manage.onClick.RemoveAllListeners();
                choice.gameObject.SetActive(visible);manage.gameObject.SetActive(visible);
                if(!visible) continue;
                var entry=catalog.entries[index];var summary=entry.summary;
                string relation=catalog.selected==null?Remains.Content.GameLocalization.UiText("source.WorldPickerPanel.212cbcfc7cf1ad8b", "Сохранённая копия", "RemainsUnity/Assets/Remains/Presentation/WorldPickerPanel.cs"):WorldHistory.Description(summary.CompareTo(catalog.selected));
                view.ShowChoice(row,WorldLibrary.DisplayName(names,summary.worldId),labels.Branch(summary.branchId),labels.Snapshot(summary.snapshot),
                    summary.revision,relation,catalog.selected?.snapshot==summary.snapshot);
                choice.interactable=true;manage.interactable=true;choices[summary.snapshot]=choice.GetComponent<RectTransform>();
                choice.onClick.AddListener(()=>Select(entry));
                manage.onClick.AddListener(()=>WorldManagementPanel.Open(transform.parent,store,summary.worldId,()=>{Reload();changed();},WorldLibrary.DisplayName(names,summary.worldId)));
            }
            view.Empty.text=Remains.Content.GameLocalization.UiText("source.WorldPickerPanel.224363493cb1ffa8", "Сохранённых миров нет.", "RemainsUnity/Assets/Remains/Presentation/WorldPickerPanel.cs");view.Empty.gameObject.SetActive(catalog.entries.Count==0);
            view.Warning.text=Remains.Content.GameLocalization.UiText("source.WorldPickerPanel.c8a73d19ca9d18f6", "Список собран с предупреждениями. Подробности — в журнале игры.", "RemainsUnity/Assets/Remains/Presentation/WorldPickerPanel.cs");
            view.Warning.gameObject.SetActive(catalog.warnings.Count>0);
            view.Page.text=$"{page+1} / {pages}";view.Previous.interactable=page>0;view.Next.interactable=page+1<pages;
            view.Close.interactable=true;
        }
        private void Select(WorldCatalogEntry entry)
        {
            if(operation.Busy) return;
            Loading(Remains.Content.GameLocalization.UiText("source.WorldPickerPanel.8a7bf6ec3eea7fac", "Проверка и выбор сохранения…", "RemainsUnity/Assets/Remains/Presentation/WorldPickerPanel.cs"));view.Close.interactable=false;
            operation.Run(_=>{var copy=entry.Load(store.WorldDirectoryPath);store.SelectContinuation(copy);return copy.Id;},_=>
            {changed();notice("");Destroy(gameObject);},error=>
            {
                notice(Remains.Content.GameLocalization.UiText("source.WorldPickerPanel.85a46d11a786a9c3", "Не удалось выбрать мир: сохранение недоступно или повреждено.\nПодробности — в журнале игры. Можно выбрать другой мир.", "RemainsUnity/Assets/Remains/Presentation/WorldPickerPanel.cs"));
                Debug.LogError($"Выбор мира {entry.summary.worldId}, снимок {entry.summary.snapshot}: {error}");Destroy(gameObject);
            });
        }
    }
}
