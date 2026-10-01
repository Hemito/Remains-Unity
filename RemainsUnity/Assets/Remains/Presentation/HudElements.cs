using System;
using UnityEngine;
using UnityEngine.UI;
namespace Remains.Presentation
{
    public sealed class HudElements
    {
        private readonly Font font;
        public HudElements(Font font) {this.font=font;}
        public GameObject Panel(Transform parent, string title, Vector2 position, Vector2 size)
        {
            var obj = new GameObject(title, typeof(RectTransform)); obj.transform.SetParent(parent,false);
            Rect(obj.GetComponent<RectTransform>(),position,size);
            obj.AddComponent<Image>().color = new Color(0.04f,0.08f,0.07f,0.95f);
            return obj;
        }
        public Text Label(Transform parent, string content, Vector2 position, Vector2 size)
        {
            var obj = new GameObject("Текст",typeof(RectTransform)); obj.transform.SetParent(parent,false);
            Rect(obj.GetComponent<RectTransform>(),position,size);
            var label = obj.AddComponent<Text>(); label.font=font; label.fontSize=18; label.text=content;
            label.color = new Color(0.65f,1,0.75f); label.raycastTarget=false;
            return label;
        }
        public Text BackedLabel(Transform parent,string content,Vector2 position,Vector2 size)
        {
            var panel=Panel(parent,"Подложка текста",position+new Vector2(-6,6),size+new Vector2(12,12));
            panel.GetComponent<Image>().raycastTarget=false;
            return Label(panel.transform,content,new Vector2(6,-6),size);
        }
        public Button Button(Transform parent, string content, Vector2 position, Vector2 size, Action action)
        {
            var obj=Panel(parent,content,position,size);
            var button=obj.AddComponent<Button>(); button.onClick.AddListener(()=>action());
            OriginalButtonStyle.Apply(button);
            var label=Label(obj.transform,content,new Vector2(5,-5),size-new Vector2(10,10));
            label.alignment=TextAnchor.MiddleCenter;
            return button;
        }
        public InputField Input(Transform parent,Vector2 position)
        {
            var obj=Panel(parent,"Адрес сервера",position,new Vector2(400,45));
            var label=Label(obj.transform,"127.0.0.1",new Vector2(10,-5),new Vector2(380,35));
            var input=obj.AddComponent<InputField>(); input.textComponent=label; input.text="127.0.0.1";
            return input;
        }
        public static void Rect(RectTransform rect,Vector2 position,Vector2 size)
        {
            rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1); rect.anchoredPosition=position; rect.sizeDelta=size;
        }
    }
}
