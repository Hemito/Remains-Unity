using System;
using Mirror;
using Remains.Gameplay;
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
        private readonly Text controls;
        private KeyboardBindings keys=KeyboardBindings.Defaults;
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
        public PauseMenu(PauseMenuView view,CoopSession session,Func<bool> inventory,Action settings,Func<bool> otherMenus=null,Action controlSettings=null,Action languageSettings=null)
        {
            this.session=session;this.inventory=inventory;this.otherMenus=otherMenus??(()=>false);
            view.Validate();root=view.Panel;layer=view.transform;status=view.Status;controls=view.Controls;
            menuSetting=view.MenuSetting;inventorySetting=view.InventorySetting;
            ManualPauseRect=view.ManualPause.GetComponent<RectTransform>();ResumeRect=view.Resume.GetComponent<RectTransform>();SoundRect=view.Sound.GetComponent<RectTransform>();
            view.ManualPause.onClick.AddListener(()=>session.Pause.SetManual(true));
            view.Resume.onClick.AddListener(()=>session.Pause.SetManual(false));
            menuSetting.onClick.AddListener(()=>session.Pause.SetSettings(!session.Pause.PauseOnMenu,session.Pause.PauseOnInventory));
            inventorySetting.onClick.AddListener(()=>session.Pause.SetSettings(session.Pause.PauseOnMenu,!session.Pause.PauseOnInventory));
            if(controlSettings!=null) ControlSettingsPanel.AddEntry(view.Sound,controlSettings,languageSettings);
            foreach(var button in new[]{view.ManualPause,view.Resume,view.Close,view.MenuSetting,view.InventorySetting})
                LocalizedUiText.BindTree(button.transform);
            view.Close.onClick.AddListener(Toggle);view.Sound.onClick.AddListener(()=>settings());
            root.SetActive(false);session.Changed+=Refresh;session.Pause.Changed+=Refresh;Refresh();
        }
        public void Toggle()
        {
            if(session.LocalActor==null) return;
            root.SetActive(!root.activeSelf);if(root.activeSelf) layer.SetAsLastSibling();PublishIntent();Refresh();
        }
        public void PublishIntent()=>session.Pause.SetIntent(IsOpen || otherMenus(),inventory());
        public void SetBindings(KeyboardBindings value)
        {
            keys=value;
            string left=keys.Label(PlayerBinding.Left),right=keys.Label(PlayerBinding.Right),up=keys.Label(PlayerBinding.Up),
                down=keys.Label(PlayerBinding.Down),sprint=keys.Label(PlayerBinding.Sprint),interact=keys.Label(PlayerBinding.Interact),
                strike=keys.Label(PlayerBinding.Strike);
            controls.text=$"{left}/{right} — ходьба · {sprint} — бег\n"+
                $"{up}/{down} — лестница · {down}/{up} — присесть/встать\n"+
                $"{down} дважды — спуск сквозь настил\n"+
                $"{keys.Label(PlayerBinding.Jump)} — прыжок (держать — выше)\n"+
                "Мышь — прицел · ЛКМ — атака/бросок\n"+
                $"{keys.Label(PlayerBinding.Telekinesis)} держать — телекинез\n"+
                $"{interact} — открыть/подобрать · держать — помощь/ремонт\n"+
                $"{keys.Label(PlayerBinding.Reload)} — перезарядка · {keys.Label(PlayerBinding.Heal)} — аптечка\n"+
                $"{keys.Label(PlayerBinding.Grenade)} — граната · {strike} / {sprint}+{strike} — удар/пинок\n"+
                $"{keys.Label(PlayerBinding.Inventory)} — инвентарь и быстрые ячейки\n"+
                $"{sprint}+ЛКМ — стопка обычной вещи\n"+
                (keys.Label(PlayerBinding.Menu)=="Esc"?"Esc — меню / закрыть окно":$"{keys.Label(PlayerBinding.Menu)} — меню · Esc — закрыть окно");
            Refresh();
        }
        private void Refresh()
        {
            var actor=session.LocalActor;
            string version=actor==null?null:$"{actor.id}:{actor.generation}:{actor.lifecycleSequence}";
            if(version!=actorVersion) {actorVersion=version;root.SetActive(false);}
            status.text=actor!=null && session.Pause.IsPaused(actor.level)?$"Уровень остановлен · {keys.Label(PlayerBinding.Menu)} — меню":"";
            menuSetting.gameObject.SetActive(NetworkServer.active);inventorySetting.gameObject.SetActive(NetworkServer.active);
            menuSetting.GetComponentInChildren<Text>().text="Хост: пауза при меню — "+(session.Pause.PauseOnMenu?"включена":"выключена");
            inventorySetting.GetComponentInChildren<Text>().text="Хост: пауза при инвентаре — "+(session.Pause.PauseOnInventory?"включена":"выключена");
        }
    }
}
