using System;
using Remains.Persistence;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    public static class WorldManagementPanel
    {
        public static void Open(Transform parent,HudElements ui,SaveStore store,string worldId,Action changed)
        {
            var library=new WorldLibrary(store);
            var overlay=ui.Panel(parent,"Управление сохранением",Vector2.zero,Vector2.zero);
            var cover=overlay.GetComponent<RectTransform>();cover.anchorMin=Vector2.zero;cover.anchorMax=Vector2.one;cover.offsetMin=cover.offsetMax=Vector2.zero;
            overlay.GetComponent<Image>().color=new Color(0,0,0,.65f);
            var root=ui.Panel(overlay.transform,"Управление миром",Vector2.zero,new Vector2(720,360));
            var rect=root.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);rect.anchoredPosition=Vector2.zero;
            root.GetComponent<Image>().color=new Color(.015f,.025f,.02f,1);
            ui.Label(root.transform,"Название мира",new Vector2(24,-20),new Vector2(660,35));
            var name=ui.Input(root.transform,new Vector2(24,-65));name.characterLimit=48;name.text=library.Name(worldId);
            var status=ui.Label(root.transform,"Имя меняется только в локальном списке. Персонаж сохраняется отдельно.",new Vector2(24,-130),new Vector2(660,90));status.fontSize=19;
            void Apply(Action action)
            {
                try {action();UnityEngine.Object.Destroy(overlay);changed();}
                catch(Exception error) {status.text="Не удалось изменить сохранение: "+error.Message;Debug.LogError(status.text);}
            }
            ui.Button(root.transform,"Сохранить имя",new Vector2(440,-65),new Vector2(250,44),()=>Apply(()=>library.Rename(worldId,name.text)));
            bool confirm=false;Button delete=null;
            delete=ui.Button(root.transform,"Удалить мир",new Vector2(24,-240),new Vector2(300,48),()=>
            {
                if(!confirm)
                {
                    confirm=true;status.text="Удалить этот мир со всеми локальными ветвями? Из списка он исчезнет; резервная копия останется в папке DeletedWorlds. Персонаж не удаляется.";
                    delete.GetComponentInChildren<Text>().text="Да, удалить мир";return;
                }
                Apply(()=>library.Delete(worldId));
            });
            ui.Button(root.transform,"Закрыть",new Vector2(440,-240),new Vector2(250,48),()=>UnityEngine.Object.Destroy(overlay));
        }
    }
}
