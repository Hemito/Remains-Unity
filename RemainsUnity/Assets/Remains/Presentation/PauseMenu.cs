using System;
using Mirror;
using Remains.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    public sealed class PauseMenu
    {
        private readonly CoopSession session;
        private readonly Func<bool> inventory;
        private readonly GameObject root;
        private readonly Text status;
        private readonly Button menuSetting,inventorySetting,exit;
        private string actorVersion;
        public bool IsOpen=>root.activeSelf;
        public RectTransform ManualPauseRect {get;}
        public RectTransform ResumeRect {get;}
        public RectTransform MenuSettingRect=>menuSetting.GetComponent<RectTransform>();
        public RectTransform InventorySettingRect=>inventorySetting.GetComponent<RectTransform>();
        public RectTransform ExitToHubRect=>exit.GetComponent<RectTransform>();
        public PauseMenu(Transform parent,HudElements ui,CoopSession session,Func<bool> inventory)
        {
            this.session=session;this.inventory=inventory;
            status=ui.Label(parent,"",new Vector2(790,-650),new Vector2(470,55));
            root=ui.Panel(parent,"Меню сессии",new Vector2(400,-145),new Vector2(680,420));
            ui.Label(root.transform,"Меню · Escape закрывает меню",new Vector2(20,-15),new Vector2(640,40));
            ManualPauseRect=ui.Button(root.transform,"Остановить этот уровень",new Vector2(20,-65),new Vector2(310,45),()=>session.Pause.SetManual(true)).GetComponent<RectTransform>();
            ResumeRect=ui.Button(root.transform,"Снять ручную паузу",new Vector2(350,-65),new Vector2(310,45),()=>session.Pause.SetManual(false)).GetComponent<RectTransform>();
            menuSetting=ui.Button(root.transform,"",new Vector2(20,-130),new Vector2(640,45),()=>session.Pause.SetSettings(!session.Pause.PauseOnMenu,session.Pause.PauseOnInventory));
            inventorySetting=ui.Button(root.transform,"",new Vector2(20,-190),new Vector2(640,45),()=>session.Pause.SetSettings(session.Pause.PauseOnMenu,!session.Pause.PauseOnInventory));
            exit=ui.Button(root.transform,"Вернуться в хаб",new Vector2(20,-265),new Vector2(310,45),()=>session.Travel.RequestHub());
            ui.Button(root.transform,"Закрыть меню",new Vector2(350,-265),new Vector2(310,45),Toggle);
            ui.Label(root.transform,"Автопауза закончится после закрытия всех меню/инвентарей\nв этом уровне. Остальные уровни продолжают работать.",new Vector2(20,-325),new Vector2(640,75));
            root.SetActive(false);session.Changed+=Refresh;session.Pause.Changed+=Refresh;Refresh();
        }
        public void Toggle()
        {
            if(session.LocalActor==null) return;
            root.SetActive(!root.activeSelf);PublishIntent();Refresh();
        }
        public void PublishIntent()=>session.Pause.SetIntent(IsOpen,inventory());
        private void Refresh()
        {
            var actor=session.LocalActor;
            string version=actor==null?null:$"{actor.id}:{actor.generation}:{actor.lifecycleSequence}";
            if(version!=actorVersion) {actorVersion=version;root.SetActive(false);}
            status.text=actor!=null && session.Pause.IsPaused(actor.level)?"Уровень остановлен · Escape — меню":"";
            menuSetting.gameObject.SetActive(NetworkServer.active);inventorySetting.gameObject.SetActive(NetworkServer.active);
            menuSetting.GetComponentInChildren<Text>().text="Хост: пауза при меню — "+(session.Pause.PauseOnMenu?"включена":"выключена");
            inventorySetting.GetComponentInChildren<Text>().text="Хост: пауза при инвентаре — "+(session.Pause.PauseOnInventory?"включена":"выключена");
            exit.interactable=actor!=null && actor.level!="hub" && !actor.down && !session.Travel.Pending && !session.Pause.IsPaused("hub");
        }
    }
}
