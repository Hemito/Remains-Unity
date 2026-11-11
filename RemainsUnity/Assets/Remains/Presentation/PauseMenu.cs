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
            controls.text=Remains.Content.GameLocalization.Format("source.PauseMenu.10309f4b595c1eb9", $"{left}/{right} — ходьба · {sprint} — бег\n", "RemainsUnity/Assets/Remains/Presentation/PauseMenu.cs")+
                Remains.Content.GameLocalization.Format("source.PauseMenu.53eca11017d25265", $"{up}/{down} — лестница · {down}/{up} — присесть/встать\n", "RemainsUnity/Assets/Remains/Presentation/PauseMenu.cs")+
                Remains.Content.GameLocalization.Format("source.PauseMenu.d7433a4c4029aba9", $"{down} дважды — спуск сквозь настил\n", "RemainsUnity/Assets/Remains/Presentation/PauseMenu.cs")+
                Remains.Content.GameLocalization.Format("source.PauseMenu.16f0bac21e0bb469", $"{keys.Label(PlayerBinding.Jump)} — прыжок (держать — выше)\n", "RemainsUnity/Assets/Remains/Presentation/PauseMenu.cs")+
                Remains.Content.GameLocalization.UiText("source.PauseMenu.eade222b283e834a", "Мышь — прицел · ЛКМ — атака/бросок\n", "RemainsUnity/Assets/Remains/Presentation/PauseMenu.cs")+
                Remains.Content.GameLocalization.Format("source.PauseMenu.308c1d7da7703825", $"{keys.Label(PlayerBinding.Telekinesis)} держать — телекинез\n", "RemainsUnity/Assets/Remains/Presentation/PauseMenu.cs")+
                Remains.Content.GameLocalization.Format("source.PauseMenu.1d295be9a370dc95", $"{interact} — открыть/подобрать · держать — помощь/ремонт\n", "RemainsUnity/Assets/Remains/Presentation/PauseMenu.cs")+
                Remains.Content.GameLocalization.Format("source.PauseMenu.c8b618633d89eb3d", $"{keys.Label(PlayerBinding.Reload)} — перезарядка · {keys.Label(PlayerBinding.Heal)} — аптечка\n", "RemainsUnity/Assets/Remains/Presentation/PauseMenu.cs")+
                Remains.Content.GameLocalization.Format("source.PauseMenu.4250496aed1ef60b", $"{keys.Label(PlayerBinding.Grenade)} — граната · {strike} / {sprint}+{strike} — удар/пинок\n", "RemainsUnity/Assets/Remains/Presentation/PauseMenu.cs")+
                Remains.Content.GameLocalization.Format("source.PauseMenu.5ceaacafaf414929", $"{keys.Label(PlayerBinding.Inventory)} — инвентарь и быстрые ячейки\n", "RemainsUnity/Assets/Remains/Presentation/PauseMenu.cs")+
                Remains.Content.GameLocalization.Format("source.PauseMenu.0b94c491dcee3bf9", $"{sprint}+ЛКМ — стопка обычной вещи\n", "RemainsUnity/Assets/Remains/Presentation/PauseMenu.cs")+
                (keys.Label(PlayerBinding.Menu)=="Esc"?Remains.Content.GameLocalization.UiText("source.PauseMenu.20310506f222f9d4", "Esc — меню / закрыть окно", "RemainsUnity/Assets/Remains/Presentation/PauseMenu.cs"):Remains.Content.GameLocalization.Format("source.PauseMenu.3cc5c3039864df99", $"{keys.Label(PlayerBinding.Menu)} — меню · Esc — закрыть окно", "RemainsUnity/Assets/Remains/Presentation/PauseMenu.cs"));
            Refresh();
        }
        private void Refresh()
        {
            var actor=session.LocalActor;
            string version=actor==null?null:$"{actor.id}:{actor.generation}:{actor.lifecycleSequence}";
            if(version!=actorVersion) {actorVersion=version;root.SetActive(false);}
            status.text=actor!=null && session.Pause.IsPaused(actor.level)?Remains.Content.GameLocalization.Format("source.PauseMenu.ddf92dd0970ec899", $"Уровень остановлен · {keys.Label(PlayerBinding.Menu)} — меню", "RemainsUnity/Assets/Remains/Presentation/PauseMenu.cs"):"";
            menuSetting.gameObject.SetActive(NetworkServer.active);inventorySetting.gameObject.SetActive(NetworkServer.active);
            menuSetting.GetComponentInChildren<Text>().text=Remains.Content.GameLocalization.UiText("source.PauseMenu.b823dc8f124f4877", "Хост: пауза при меню — ", "RemainsUnity/Assets/Remains/Presentation/PauseMenu.cs")+(session.Pause.PauseOnMenu?Remains.Content.GameLocalization.UiText("source.PauseMenu.a861092dcf475840", "включена", "RemainsUnity/Assets/Remains/Presentation/PauseMenu.cs"):Remains.Content.GameLocalization.UiText("source.PauseMenu.36735a542b8bcfab", "выключена", "RemainsUnity/Assets/Remains/Presentation/PauseMenu.cs"));
            inventorySetting.GetComponentInChildren<Text>().text=Remains.Content.GameLocalization.UiText("source.PauseMenu.6843e35cb2bc4322", "Хост: пауза при инвентаре — ", "RemainsUnity/Assets/Remains/Presentation/PauseMenu.cs")+(session.Pause.PauseOnInventory?Remains.Content.GameLocalization.UiText("source.PauseMenu.a861092dcf475840", "включена", "RemainsUnity/Assets/Remains/Presentation/PauseMenu.cs"):Remains.Content.GameLocalization.UiText("source.PauseMenu.36735a542b8bcfab", "выключена", "RemainsUnity/Assets/Remains/Presentation/PauseMenu.cs"));
        }
    }
}
