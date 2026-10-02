using System;
using Remains.Persistence;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    public static class WorldManagementPanel
    {
        public static void Open(Transform parent,SaveStore store,string worldId,Action changed)
        {
            var library=new WorldLibrary(store);
            var view=WorldManagementView.Create(parent);var overlay=view.gameObject;
            var name=view.Name;name.text=library.Name(worldId);var status=view.Status;
            void Apply(Action action)
            {
                try {action();UnityEngine.Object.Destroy(overlay);changed();}
                catch(Exception error) {status.text="Не удалось изменить сохранение: "+error.Message;Debug.LogError(status.text);}
            }
            view.Rename.onClick.AddListener(()=>Apply(()=>library.Rename(worldId,name.text)));
            bool confirm=false;Button delete=view.Delete;
            delete.onClick.AddListener(()=>
            {
                if(!confirm)
                {
                    confirm=true;status.text="Удалить этот мир со всеми локальными ветвями? Из списка он исчезнет; резервная копия останется в папке DeletedWorlds. Персонаж не удаляется.";
                    delete.GetComponentInChildren<Text>().text="Да, удалить мир";return;
                }
                Apply(()=>library.Delete(worldId));
            });
            view.Close.onClick.AddListener(()=>UnityEngine.Object.Destroy(overlay));
        }
    }
}
