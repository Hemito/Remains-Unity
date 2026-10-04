using System;
using System.Collections.Generic;
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
            view.Next.onClick.AddListener(()=>{page++;Render();});Reload();
        }
        private void Loading(string text)
        {
            choices.Clear();view.Empty.text=text;view.Empty.gameObject.SetActive(true);
            view.Warning.gameObject.SetActive(false);
            foreach(var button in view.Choices) button.gameObject.SetActive(false);
            foreach(var button in view.Manage) button.gameObject.SetActive(false);
            view.Previous.interactable=false;view.Next.interactable=false;view.Page.text="";
        }
        private void Reload()
        {
            catalog=null;Loading("Чтение списка миров…");
            operation.Run(token=>(store.Catalog.Read(token),new WorldLibrary(store).Names()),result=>
            {
                catalog=result.Item1;names=result.Item2;
                foreach(string warning in catalog.warnings) Debug.LogWarning(warning);
                Render();
            },error=>
            {Loading("Не удалось прочитать список миров. Подробности — в журнале игры.");Debug.LogError("Список миров "+store.DirectoryPath+": "+error);});
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
                string relation=catalog.selected==null?"Сохранённая копия":WorldHistory.Description(summary.CompareTo(catalog.selected));
                view.ShowChoice(row,WorldLibrary.DisplayName(names,summary.worldId),summary.branchId??"",summary.revision,relation,catalog.selected?.snapshot==summary.snapshot);
                choice.interactable=true;manage.interactable=true;choices[summary.snapshot]=choice.GetComponent<RectTransform>();
                choice.onClick.AddListener(()=>Select(entry));
                manage.onClick.AddListener(()=>WorldManagementPanel.Open(transform.parent,store,summary.worldId,()=>{Reload();changed();},WorldLibrary.DisplayName(names,summary.worldId)));
            }
            view.Empty.text="Сохранённых миров нет.";view.Empty.gameObject.SetActive(catalog.entries.Count==0);
            view.Warning.text="Список собран с предупреждениями. Подробности — в журнале игры.";
            view.Warning.gameObject.SetActive(catalog.warnings.Count>0);
            view.Page.text=$"{page+1} / {pages}";view.Previous.interactable=page>0;view.Next.interactable=page+1<pages;
            view.Close.interactable=true;
        }
        private void Select(WorldCatalogEntry entry)
        {
            if(operation.Busy) return;
            Loading("Проверка и выбор сохранения…");view.Close.interactable=false;
            operation.Run(_=>{var copy=entry.Load(store.DirectoryPath);store.SelectContinuation(copy);return copy.Id;},_=>
            {changed();notice("");Destroy(gameObject);},error=>
            {
                notice("Не удалось выбрать мир: сохранение недоступно или повреждено.\nПодробности — в журнале игры. Можно выбрать другой мир.");
                Debug.LogError($"Выбор мира {entry.summary.worldId}, снимок {entry.summary.snapshot}: {error}");Destroy(gameObject);
            });
        }
    }
}
