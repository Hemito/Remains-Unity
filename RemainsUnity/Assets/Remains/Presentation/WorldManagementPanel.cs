using System;
using Remains.Persistence;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    public static class WorldManagementPanel
    {
        public static void Open(Transform parent,SaveStore store,string worldId,Action changed,string displayName=null)
        {
            var library=new WorldLibrary(store);
            var view=WorldManagementView.Create(parent);var overlay=view.gameObject;
            string savedName=displayName??library.Name(worldId);
            var name=view.Name;name.text=savedName;var status=view.Status;
            var operation=overlay.AddComponent<BackgroundSaveOperation>();
            void SetBusy(bool busy)
            {name.interactable=!busy;view.Rename.interactable=!busy;view.Delete.interactable=!busy;view.Close.interactable=!busy;}
            void Apply(Action action)
            {
                if(operation.Busy) return;
                SetBusy(true);status.text="Сохранение изменений…";
                operation.Run(_=>{action();return true;},_=>{UnityEngine.Object.Destroy(overlay);changed();},error=>
                {SetBusy(false);status.text="Не удалось изменить сохранение: "+error.Message;Debug.LogError("Управление миром "+worldId+": "+error);});
            }
            view.Rename.onClick.AddListener(()=>{string proposed=name.text;Apply(()=>library.Rename(worldId,proposed));});
            bool confirm=false;Button delete=view.Delete;
            delete.onClick.AddListener(()=>
            {
                if(!confirm)
                {
                    string shortId=worldId.Substring(0,Math.Min(8,worldId.Length));
                    confirm=true;status.text=$"Удалить «{savedName}» (мир {shortId}) и все его локальные ветви?\nКопия останется в DeletedWorlds. Персонаж сохранится.";
                    delete.GetComponentInChildren<Text>().text="Да, удалить мир";return;
                }
                Apply(()=>library.Delete(worldId));
            });
            view.Close.onClick.AddListener(()=>UnityEngine.Object.Destroy(overlay));
        }
    }
}
