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
        private readonly Button menuSetting,inventorySetting;
        private readonly Func<bool> otherMenus;
        private string actorVersion;
        public bool IsOpen=>root.activeSelf;
        private readonly Transform layer;
        public RectTransform ManualPauseRect {get;}
        public RectTransform ResumeRect {get;}
        public RectTransform SoundRect {get;}
        public RectTransform MenuSettingRect=>menuSetting.GetComponent<RectTransform>();
        public RectTransform InventorySettingRect=>inventorySetting.GetComponent<RectTransform>();
        public PauseMenu(PauseMenuView view,CoopSession session,Func<bool> inventory,Action settings,Func<bool> otherMenus=null)
        {
            this.session=session;this.inventory=inventory;this.otherMenus=otherMenus??(()=>false);
            view.Validate();root=view.Panel;layer=view.transform;status=view.Status;
            menuSetting=view.MenuSetting;inventorySetting=view.InventorySetting;
            ManualPauseRect=view.ManualPause.GetComponent<RectTransform>();ResumeRect=view.Resume.GetComponent<RectTransform>();SoundRect=view.Sound.GetComponent<RectTransform>();
            view.ManualPause.onClick.AddListener(()=>session.Pause.SetManual(true));
            view.Resume.onClick.AddListener(()=>session.Pause.SetManual(false));
            menuSetting.onClick.AddListener(()=>session.Pause.SetSettings(!session.Pause.PauseOnMenu,session.Pause.PauseOnInventory));
            inventorySetting.onClick.AddListener(()=>session.Pause.SetSettings(session.Pause.PauseOnMenu,!session.Pause.PauseOnInventory));
            view.Close.onClick.AddListener(Toggle);view.Sound.onClick.AddListener(()=>settings());
            root.SetActive(false);session.Changed+=Refresh;session.Pause.Changed+=Refresh;Refresh();
        }
        public void Toggle()
        {
            if(session.LocalActor==null) return;
            root.SetActive(!root.activeSelf);if(root.activeSelf) layer.SetAsLastSibling();PublishIntent();Refresh();
        }
        public void PublishIntent()=>session.Pause.SetIntent(IsOpen || otherMenus(),inventory());
        private void Refresh()
        {
            var actor=session.LocalActor;
            string version=actor==null?null:$"{actor.id}:{actor.generation}:{actor.lifecycleSequence}";
            if(version!=actorVersion) {actorVersion=version;root.SetActive(false);}
            status.text=actor!=null && session.Pause.IsPaused(actor.level)?"Уровень остановлен · Escape — меню":"";
            menuSetting.gameObject.SetActive(NetworkServer.active);inventorySetting.gameObject.SetActive(NetworkServer.active);
            menuSetting.GetComponentInChildren<Text>().text="Хост: пауза при меню — "+(session.Pause.PauseOnMenu?"включена":"выключена");
            inventorySetting.GetComponentInChildren<Text>().text="Хост: пауза при инвентаре — "+(session.Pause.PauseOnInventory?"включена":"выключена");
        }
    }
}
