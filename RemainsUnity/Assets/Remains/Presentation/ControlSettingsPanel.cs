using System;
using Remains.Gameplay;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Remains.Presentation
{
    public sealed class ControlSettingsPanel
    {
        private const string Instructions="Выберите действие и нажмите клавишу. Esc отменяет выбор и закрывает окно.";
        private readonly InputPreferences preferences;
        private readonly GameObject root;
        private readonly Text[] labels=new Text[KeyboardBindings.Count];
        private readonly Text status;
        private KeyboardBindings draft;
        private PlayerBinding? capturing;
        private int captureFrame,closedFrame=-1;
        public bool IsOpen=>root.activeSelf;
        public bool ConsumesKeyboard=>IsOpen || closedFrame==Time.frameCount;

        public ControlSettingsPanel(Transform parent,HudElements ui,InputPreferences preferences)
        {
            this.preferences=preferences;
            root=new GameObject("Настройки управления",typeof(RectTransform),typeof(Image));
            root.transform.SetParent(parent,false);
            var backdrop=(RectTransform)root.transform;
            backdrop.anchorMin=Vector2.zero;backdrop.anchorMax=Vector2.one;backdrop.offsetMin=backdrop.offsetMax=Vector2.zero;
            root.GetComponent<Image>().color=new Color(0,0,0,.68f);
            var panel=ui.Panel(backdrop,"Клавиатура",Vector2.zero,new Vector2(830,582)).GetComponent<RectTransform>();
            panel.anchorMin=panel.anchorMax=panel.pivot=new Vector2(.5f,.5f);
            panel.GetComponent<Image>().color=new Color(.04f,.08f,.07f,1);
            var title=ui.Label(panel,"Управление",new Vector2(24,-16),new Vector2(780,32));title.fontSize=25;
            ui.Label(panel,"ДЕЙСТВИЯ",new Vector2(24,-54),new Vector2(364,25)).fontSize=16;
            ui.Label(panel,"БЫСТРЫЕ ЯЧЕЙКИ",new Vector2(428,-54),new Vector2(364,25)).fontSize=16;
            int columns=(int)PlayerBinding.Slot1;
            for(int i=0;i<KeyboardBindings.Count;i++)
            {
                var action=(PlayerBinding)i;
                bool shortcut=i>=columns;
                float x=shortcut?428:24;
                float y=-83-(shortcut?i-columns:i)*29;
                ui.Label(panel,KeyboardBindings.ActionName(action),new Vector2(x,y),new Vector2(240,26)).fontSize=16;
                var button=CreateButton(ui,panel,"",new Vector2(x+243,y),new Vector2(137,26),()=>Capture(action));
                button.name=action.ToString();labels[i]=button.GetComponentInChildren<Text>();
                labels[i].fontSize=16;
                HudElements.Rect(labels[i].rectTransform,new Vector2(5,-1),new Vector2(127,24));
            }
            status=ui.Label(panel,"",new Vector2(24,-498),new Vector2(782,33));status.fontSize=15;
            CreateButton(ui,panel,"Стандартные",new Vector2(24,-537),new Vector2(187,35),Reset);
            CreateButton(ui,panel,"Отмена",new Vector2(442,-537),new Vector2(173,35),Cancel);
            CreateButton(ui,panel,"Сохранить",new Vector2(632,-537),new Vector2(174,35),Save);
            root.SetActive(false);
        }

        public void Open()
        {
            if(IsOpen) return;
            draft=preferences.Current;capturing=null;status.text=preferences.LoadWarning??Instructions;
            root.SetActive(true);root.transform.SetAsLastSibling();RefreshLabels();
            EventSystem.current?.SetSelectedGameObject(null);
        }
        public void Cancel()
        {
            if(!IsOpen) return;
            capturing=null;closedFrame=Time.frameCount;root.SetActive(false);
        }
        public void ReadKeyboard(Keyboard keyboard)
        {
            if(!IsOpen || keyboard==null) return;
            if(!capturing.HasValue)
            {
                if(preferences.Current.Pressed(keyboard,PlayerBinding.Menu)) Cancel();
                return;
            }
            if(Time.frameCount==captureFrame) return;
            var action=capturing.Value;
            if(keyboard.escapeKey.wasPressedThisFrame && action!=PlayerBinding.Menu)
            {capturing=null;status.text=Instructions;RefreshLabels();return;}
            foreach(var key in keyboard.allKeys)
            {
                if(!key.wasPressedThisFrame) continue;
                if(!draft.TryAssign(action,key.keyCode,out var updated,out string error))
                {status.text=error+" Выберите другую клавишу.";return;}
                draft=updated;capturing=null;status.text="Назначение изменено. Нажмите «Сохранить», чтобы применить.";RefreshLabels();return;
            }
        }
        private void Capture(PlayerBinding action)
        {
            capturing=action;captureFrame=Time.frameCount;
            status.text=action==PlayerBinding.Menu?"Нажмите новую клавишу меню. Esc можно назначить снова.":
                $"Нажмите клавишу для «{KeyboardBindings.ActionName(action)}». Esc отменяет выбор.";
            RefreshLabels();EventSystem.current?.SetSelectedGameObject(null);
        }
        private void Reset()
        {
            draft=KeyboardBindings.Defaults;capturing=null;status.text="Стандартные клавиши восстановлены. Нажмите «Сохранить», чтобы применить.";RefreshLabels();
        }
        private void Save()
        {
            try {preferences.Save(draft);Cancel();}
            catch(Exception failure) {status.text="Не удалось сохранить настройки. "+failure.Message;Debug.LogError(status.text);}
        }
        private void RefreshLabels()
        {
            for(int i=0;i<labels.Length;i++)
            {
                var action=(PlayerBinding)i;
                labels[i].text=capturing==action?"Нажмите…":draft.Label(action);
            }
        }
        private static Button CreateButton(HudElements ui,Transform parent,string text,Vector2 position,Vector2 size,Action action)
        {
            var button=ui.Button(parent,text,position,size,action);
            button.navigation=new Navigation {mode=Navigation.Mode.None};
            return button;
        }
        // The existing audio row becomes two buttons, preserving its position and authored visual style.
        internal static void AddEntry(Button audio,Action open)
        {
            var rect=(RectTransform)audio.transform;
            var controls=UnityEngine.Object.Instantiate(audio,rect.parent,false);
            var other=(RectTransform)controls.transform;
            float width=(rect.sizeDelta.x-10)*.5f;
            rect.sizeDelta=new Vector2(width,rect.sizeDelta.y);other.sizeDelta=rect.sizeDelta;
            other.anchoredPosition=rect.anchoredPosition+Vector2.right*(width+10);
            controls.name="Настройки управления";
            foreach(var (button,text) in new[]{(audio,"Звук"),(controls,"Управление")})
            {
                var label=button.GetComponentInChildren<Text>(true);label.text=text;
                var child=label.rectTransform;child.anchorMin=Vector2.zero;child.anchorMax=Vector2.one;
                child.offsetMin=new Vector2(5,2);child.offsetMax=new Vector2(-5,-2);
            }
            controls.onClick.AddListener(()=>open());
        }
    }
}
