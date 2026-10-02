using System;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    // The shell retains its native aspect; each page occupies the screen opening.
    public sealed class InventoryWindow
    {
        private readonly GameObject personal,shared;
        private readonly Button personalTab,sharedTab;
        public RectTransform PersonalTab=>personalTab.GetComponent<RectTransform>();
        public RectTransform SharedTab=>sharedTab.GetComponent<RectTransform>();
        public InventoryWindow(GameObject root,GameObject personal,GameObject shared,HudElements ui,Action close)
        {
            this.personal=personal;this.shared=shared;
            var texture=Resources.Load<Texture2D>("Original/Inventory/shell");
            if(texture==null) throw new InvalidOperationException("Нет корпуса инвентаря Original/Inventory/shell.");
            var background=root.GetComponent<Image>();
            background.sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),100);
            background.color=Color.white;background.preserveAspect=true;
            var rect=root.GetComponent<RectTransform>();rect.sizeDelta=new Vector2(1260,1260f*texture.height/texture.width);
            rect.anchoredPosition=Vector2.zero;
            HudElements.Rect(personal.GetComponent<RectTransform>(),new Vector2(221,-133),new Vector2(820,530));
            personal.transform.localScale=Vector3.one*.77f;
            personal.GetComponent<Image>().color=Color.clear;
            HudElements.Rect(shared.GetComponent<RectTransform>(),new Vector2(385,-132),new Vector2(330,435));
            shared.transform.localScale=Vector3.one*.9f;
            shared.GetComponent<Image>().color=Color.clear;
            personalTab=ui.Button(root.transform,"Личные вещи",new Vector2(955,-185),new Vector2(245,44),()=>Select(false));
            sharedTab=ui.Button(root.transform,"Общий контейнер",new Vector2(955,-243),new Vector2(245,44),()=>Select(true));
            ui.Button(root.transform,"Закрыть · I",new Vector2(955,-410),new Vector2(245,44),close);
            Select(false);
        }
        private void Select(bool container)
        {
            personal.SetActive(!container);shared.SetActive(container);
            OriginalButtonStyle.SetSelected(personalTab,!container);
            OriginalButtonStyle.SetSelected(sharedTab,container);
        }
    }
}
