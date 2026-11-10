using System;
using System.Collections.Generic;
using Remains.Content;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    public sealed class LanguageSettingsPanel : IDisposable
    {
        private readonly GameObject root;
        private readonly Dictionary<string,Button> choices=new(StringComparer.Ordinal);
        private readonly Action stateChanged;
        public bool IsOpen=>root.activeSelf;
        public RectTransform ChoiceRect(string code)=>choices[code].GetComponent<RectTransform>();
        public LanguageSettingsPanel(Transform parent,HudElements ui,Action stateChanged=null)
        {
            GameLocalization.Initialize();this.stateChanged=stateChanged;
            root=new GameObject("Выбор языка",typeof(RectTransform),typeof(Image));root.transform.SetParent(parent,false);
            var backdrop=(RectTransform)root.transform;backdrop.anchorMin=Vector2.zero;backdrop.anchorMax=Vector2.one;backdrop.offsetMin=backdrop.offsetMax=Vector2.zero;
            root.GetComponent<Image>().color=new Color(0,0,0,.65f);
            var panel=ui.Panel(root.transform,"Язык интерфейса",Vector2.zero,new Vector2(520,450));
            var rect=(RectTransform)panel.transform;rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);
            var title=ui.Label(panel.transform,"Язык интерфейса",new Vector2(22,-18),new Vector2(476,36));title.fontSize=25;
            LocalizedUiText.Bind(title);
            int index=0;
            foreach(var language in GameLocalization.Languages)
            {
                string code=language.code;
                int column=index%2,row=index/2;
                var button=ui.Button(panel.transform,language.nativeName,new Vector2(22+244*column,-70-44*row),new Vector2(232,36),()=>Select(code));
                choices.Add(code,button);index++;
            }
            float bottom=86+Mathf.Ceil(index/2f)*44;
            rect.sizeDelta=new Vector2(520,bottom+72);
            var close=ui.Button(panel.transform,"Закрыть",new Vector2(22,-bottom),new Vector2(476,36),Close);
            LocalizedUiText.Bind(close.GetComponentInChildren<Text>(true));
            root.SetActive(false);GameLocalization.Changed+=Refresh;
        }
        public void Open()
        {root.SetActive(true);root.transform.SetAsLastSibling();Refresh();stateChanged?.Invoke();}
        public void Close()
        {root.SetActive(false);stateChanged?.Invoke();}
        private void Select(string code)
        {GameLocalization.Select(code);Refresh();}
        private void Refresh()
        {
            foreach(var pair in choices)
            {OriginalButtonStyle.SetSelected(pair.Value,pair.Key==GameLocalization.CurrentLanguage);pair.Value.GetComponentInChildren<Text>(true).font=LocalizationFonts.ForLanguage(pair.Key);}
        }
        public void RefreshFonts()=>Refresh();
        public void Dispose()=>GameLocalization.Changed-=Refresh;
    }
}
