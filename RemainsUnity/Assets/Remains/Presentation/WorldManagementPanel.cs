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
                SetBusy(true);status.text=Remains.Content.GameLocalization.UiText("source.WorldManagementPanel.f74ff8db4c9634f9", "Сохранение изменений…", "RemainsUnity/Assets/Remains/Presentation/WorldManagementPanel.cs");
                operation.Run(_=>{action();return true;},_=>{UnityEngine.Object.Destroy(overlay);changed();},error=>
                {SetBusy(false);status.text=Remains.Content.GameLocalization.UiText("source.WorldManagementPanel.bb86cc73f729aa46", "Не удалось изменить сохранение: ", "RemainsUnity/Assets/Remains/Presentation/WorldManagementPanel.cs")+error.Message;Debug.LogError("Управление миром "+worldId+": "+error);});
            }
            view.Rename.onClick.AddListener(()=>{string proposed=name.text;Apply(()=>library.Rename(worldId,proposed));});
            bool confirm=false;Button delete=view.Delete;
            delete.onClick.AddListener(()=>
            {
                if(!confirm)
                {
                    string shortId=worldId.Substring(0,Math.Min(8,worldId.Length));
                    confirm=true;status.text=Remains.Content.GameLocalization.Format("source.WorldManagementPanel.faadea3e949cebd2", $"Удалить «{savedName}» (мир {shortId}) и все его локальные ветви?\nКопия останется в DeletedWorlds. Персонаж сохранится.", "RemainsUnity/Assets/Remains/Presentation/WorldManagementPanel.cs");
                    delete.GetComponentInChildren<Text>().text=Remains.Content.GameLocalization.UiText("source.WorldManagementPanel.50d91fe43b525c76", "Да, удалить мир", "RemainsUnity/Assets/Remains/Presentation/WorldManagementPanel.cs");return;
                }
                Apply(()=>library.Delete(worldId));
            });
            view.Close.onClick.AddListener(()=>UnityEngine.Object.Destroy(overlay));
        }
    }
}
