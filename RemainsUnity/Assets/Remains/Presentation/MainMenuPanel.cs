using System;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    public sealed class MainMenuPanel
    {
        public GameObject Root {get;}
        public GameObject Controls {get;}
        public InputField Character {get;}
        public InputField Address {get;}
        public Button Host {get;}
        public Button Worlds {get;}
        public Text World {get;}
        public Text Notice {get;}
        private readonly Sprite glow;
        private readonly HudElements ui;
        public MainMenuPanel(Transform parent,HudElements ui,Action host,Action join,Action worlds)
        {
            this.ui=ui;
            Root=ui.Panel(parent,"Главное меню",Vector2.zero,new Vector2(1280,720));
            var full=Root.GetComponent<RectTransform>();full.anchorMin=Vector2.zero;full.anchorMax=Vector2.one;full.offsetMin=full.offsetMax=Vector2.zero;
            Root.GetComponent<Image>().color=Color.black;
            Artwork("sky");Artwork("city");
            var title=ui.Label(Root.transform,"REMAINS",new Vector2(65,-60),new Vector2(630,90));title.fontSize=64;title.color=new Color(.78f,.93f,.82f);
            var subtitle=ui.Label(Root.transform,"Совместная игра",new Vector2(70,-151),new Vector2(500,45));subtitle.fontSize=26;
            glow=LoadSprite("button-glow",new Vector4(25,5,25,5));
            Controls=ui.Panel(Root.transform,"Игра и подключение",new Vector2(775,-55),new Vector2(440,580));
            Controls.GetComponent<Image>().color=new Color(.015f,.025f,.02f,.90f);
            ui.Label(Controls.transform,"Персонаж",new Vector2(20,-15),new Vector2(400,28));
            Character=ui.Input(Controls.transform,new Vector2(20,-46));Character.characterLimit=32;
            ui.Label(Controls.transform,"Мир для своей игры",new Vector2(20,-111),new Vector2(400,28));
            World=ui.Label(Controls.transform,"Будет создан новый мир",new Vector2(20,-145),new Vector2(400,70));World.fontSize=16;
            Worlds=Button(Controls.transform,"Выбрать сохранённый мир",new Vector2(20,-220),new Vector2(400,44),worlds);
            Host=Button(Controls.transform,"Начать игру / создать сессию",new Vector2(20,-276),new Vector2(400,50),host);
            ui.Label(Controls.transform,"Подключение к другу · адрес сервера",new Vector2(20,-355),new Vector2(400,30));
            Address=ui.Input(Controls.transform,new Vector2(20,-391));
            Button(Controls.transform,"Подключиться",new Vector2(20,-449),new Vector2(400,50),join);
            var hint=ui.Label(Controls.transform,"Один ПК: 127.0.0.1 · локальная сеть: IP хозяина",new Vector2(20,-520),new Vector2(400,45));hint.fontSize=15;
            Button(Root.transform,"Выйти из игры",new Vector2(65,-632),new Vector2(300,44),()=>Application.Quit());
            Notice=ui.Label(Root.transform,"",new Vector2(390,-652),new Vector2(825,55));Notice.fontSize=16;
        }
        private Button Button(Transform parent,string label,Vector2 position,Vector2 size,Action action)
        {
            var result=ui.Button(parent,label,position,size,action);var image=result.GetComponent<Image>();image.sprite=glow;image.type=Image.Type.Sliced;image.color=Color.white;
            result.targetGraphic=image;
            var colors=result.colors;colors.normalColor=new Color(.20f,.25f,.23f,.65f);colors.highlightedColor=new Color(.55f,.85f,.70f,.85f);
            colors.selectedColor=colors.highlightedColor;colors.pressedColor=Color.white;colors.disabledColor=new Color(.12f,.12f,.12f,.3f);colors.fadeDuration=.12f;result.colors=colors;
            return result;
        }
        private void Artwork(string key)
        {
            var obj=new GameObject("Оригинал меню "+key,typeof(RectTransform));obj.transform.SetParent(Root.transform,false);
            var image=obj.AddComponent<Image>();image.sprite=LoadSprite(key,Vector4.zero);image.raycastTarget=false;
            var rect=obj.GetComponent<RectTransform>();rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            var fit=obj.AddComponent<AspectRatioFitter>();fit.aspectMode=AspectRatioFitter.AspectMode.EnvelopeParent;fit.aspectRatio=image.sprite.rect.width/image.sprite.rect.height;
        }
        private static Sprite LoadSprite(string key,Vector4 border)
        {
            var texture=Resources.Load<Texture2D>("Original/Menu/"+key);
            if(texture==null) throw new InvalidOperationException("Нет оригинальной графики меню: "+key);
            return Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect,border);
        }
    }
}
