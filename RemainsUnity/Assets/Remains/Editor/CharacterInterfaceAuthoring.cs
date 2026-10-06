using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Editor
{
    public static class CharacterInterfaceAuthoring
    {
        private static HudElements ui;
        [MenuItem("Remains/Подготовить редактор и библиотеку персонажей")]
        public static void Run()
        {
            ui=new HudElements(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            PreparePortraitSource();CreateLibrary();CreateEditor();UpdateMenu();AssetDatabase.SaveAssets();
            CharacterInterfaceChecks.Run();CharacterInterfaceChecks.Render();MainMenuRender.Render();PrototypeBuild.Build();
        }
        private static GameObject Window(string name)
        {
            var root=ui.Panel(null,name,Vector2.zero,new Vector2(1000,640));
            var rect=(RectTransform)root.transform;rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);
            root.GetComponent<Image>().color=Color.clear;
            var blocker=ui.Panel(root.transform,"Блокировка фона",Vector2.zero,Vector2.zero);
            var blockerRect=(RectTransform)blocker.transform;blockerRect.anchorMin=new Vector2(-10,-10);blockerRect.anchorMax=new Vector2(11,11);blockerRect.offsetMin=blockerRect.offsetMax=Vector2.zero;
            blocker.GetComponent<Image>().color=new Color(0,0,0,.7f);
            var surface=ui.Panel(root.transform,"Подложка окна",Vector2.zero,Vector2.zero);
            var surfaceRect=(RectTransform)surface.transform;surfaceRect.anchorMin=Vector2.zero;surfaceRect.anchorMax=Vector2.one;surfaceRect.offsetMin=surfaceRect.offsetMax=Vector2.zero;
            surface.GetComponent<Image>().color=new Color(.025f,.055f,.045f,1);surface.GetComponent<Image>().raycastTarget=false;
            return root;
        }
        private static Text Label(Transform parent,string name,string text,Vector2 point,Vector2 size,int fontSize=18)
        {var label=ui.Label(parent,text,point,size);label.name=name;label.fontSize=fontSize;return label;}
        private static Button Button(Transform parent,string text,Vector2 point,Vector2 size)
        {
            // Reuse the saved original texture hierarchy, not a transient Sprite.Create result.
            var source=Resources.Load<MainMenuView>("Presentation/MainMenu");
            if(source==null || source.Host==null) throw new System.InvalidOperationException("Нет подготовленной оригинальной кнопки меню.");
            var button=Object.Instantiate(source.Host,parent,false);button.name=text;button.onClick=new Button.ButtonClickedEvent();
            HudElements.Rect(button.GetComponent<RectTransform>(),point,size);button.interactable=true;
            foreach(var graphic in button.GetComponentsInChildren<Graphic>(true))
            {
                if(graphic.transform==button.transform) continue;
                var rect=graphic.rectTransform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            }
            var label=button.GetComponentInChildren<Text>();label.text=text;label.fontSize=18;
            return button;
        }
        private static void PreparePortraitSource()
        {
            const string path="Assets/Remains/Resources/Presentation/PersonalInventory.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach(var part in root.GetComponentsInChildren<PortraitPart>(true))
                    if(part.GetComponent<CanvasRenderer>()==null) part.gameObject.AddComponent<CanvasRenderer>();
                root.GetComponent<PersonalInventoryView>().Validate();PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
        }
        private static InputField Input(Transform parent,string name,Vector2 point,Vector2 size,int limit)
        {
            var root=ui.Panel(parent,name,point,size);root.GetComponent<Image>().color=new Color(.08f,.15f,.12f,1);
            var text=Label(root.transform,name+" · текст","",new Vector2(10,-6),size-new Vector2(20,12));
            var input=root.AddComponent<InputField>();input.textComponent=text;input.characterLimit=limit;return input;
        }
        private static CharacterPortraitView Portrait(Transform parent,Vector2 point,Vector2 size,bool weapon)
        {
            var root=new GameObject("Превью персонажа",typeof(RectTransform));root.transform.SetParent(parent,false);
            HudElements.Rect((RectTransform)root.transform,point,size);
            var view=root.AddComponent<CharacterPortraitView>();
            var inventory=Resources.Load<PersonalInventoryView>("Presentation/PersonalInventory");
            if(inventory==null) throw new System.InvalidOperationException("Нет подготовленного портрета инвентаря.");
            var pony=Object.Instantiate(inventory.Portrait,root.transform,false);pony.name="Составной портрет";pony.SetActive(true);
            HudElements.Rect((RectTransform)pony.transform,Vector2.zero,new Vector2(size.x-(weapon?75:0),size.y));
            view.Pony=pony;view.Parts=pony.GetComponentsInChildren<PortraitPart>(true);
            var icon=ui.Panel(root.transform,"Последнее оружие",new Vector2(size.x-76,-size.y*.35f),new Vector2(72,50)).GetComponent<Image>();
            icon.color=Color.white;icon.raycastTarget=false;icon.preserveAspect=true;icon.enabled=false;view.Weapon=icon;
            view.Validate();return view;
        }
        private static void CreateLibrary()
        {
            var root=Window("Библиотека персонажей");
            try
            {
                var view=root.AddComponent<CharacterLibraryView>();
                Label(root.transform,"Заголовок","Персонажи",new Vector2(24,-18),new Vector2(700,42),28);
                view.Status=Label(root.transform,"Состояние списка","Выберите персонажа",new Vector2(24,-62),new Vector2(950,30));
                view.Cards=new Button[6];view.Portraits=new CharacterPortraitView[6];view.Names=new Text[6];view.Times=new Text[6];
                for(int i=0;i<6;i++)
                {
                    var card=Button(root.transform,"",new Vector2(24+i%3*320,-102-i/3*230),new Vector2(308,218));card.name="Карточка персонажа "+(i+1);
                    Object.DestroyImmediate(card.GetComponentInChildren<Text>().gameObject);
                    view.Cards[i]=card;view.Portraits[i]=Portrait(card.transform,new Vector2(10,-8),new Vector2(288,129),true);
                    view.Names[i]=Label(card.transform,"Имя","Имя персонажа",new Vector2(12,-142),new Vector2(284,44),16);view.Names[i].alignment=TextAnchor.MiddleCenter;
                    view.Times[i]=Label(card.transform,"Время игры","Время ещё не учтено",new Vector2(12,-190),new Vector2(284,25),16);view.Times[i].alignment=TextAnchor.MiddleCenter;
                }
                view.Create=Button(root.transform,"Создать персонажа",new Vector2(24,-583),new Vector2(240,40));
                view.Previous=Button(root.transform,"←",new Vector2(350,-583),new Vector2(70,40));
                view.Page=Label(root.transform,"Страница","1 / 1",new Vector2(428,-589),new Vector2(130,30));view.Page.alignment=TextAnchor.MiddleCenter;
                view.Next=Button(root.transform,"→",new Vector2(566,-583),new Vector2(70,40));
                view.Close=Button(root.transform,"Назад",new Vector2(786,-583),new Vector2(190,40));
                view.Validate();PrefabUtility.SaveAsPrefabAsset(root,"Assets/Remains/Resources/Presentation/CharacterLibrary.prefab");
            }
            finally {Object.DestroyImmediate(root);}
        }
        private static void CreateEditor()
        {
            var root=Window("Редактор персонажа");
            try
            {
                var view=root.AddComponent<CharacterEditorView>();
                Label(root.transform,"Заголовок","Создание персонажа",new Vector2(24,-18),new Vector2(700,42),28);
                Label(root.transform,"Подпись имени","Имя",new Vector2(24,-70),new Vector2(66,32));
                view.Name=Input(root.transform,"Имя персонажа",new Vector2(90,-66),new Vector2(294,40),32);
                view.Portrait=Portrait(root.transform,new Vector2(24,-116),new Vector2(360,346),false);
                view.HairPrevious=Button(root.transform,"←",new Vector2(24,-478),new Vector2(60,36));
                view.HairLabel=Label(root.transform,"Форма гривы","Причёска 1 / 5",new Vector2(90,-481),new Vector2(220,30));view.HairLabel.alignment=TextAnchor.MiddleCenter;
                view.HairNext=Button(root.transform,"→",new Vector2(324,-478),new Vector2(60,36));
                view.EyePrevious=Button(root.transform,"←",new Vector2(24,-524),new Vector2(60,36));
                view.EyeLabel=Label(root.transform,"Форма глаз","Глаза 1 / 6",new Vector2(90,-527),new Vector2(220,30));view.EyeLabel.alignment=TextAnchor.MiddleCenter;
                view.EyeNext=Button(root.transform,"→",new Vector2(324,-524),new Vector2(60,36));
                string[] names={"Шерсть","Грива","Второй цвет гривы","Глаза","Магия"};
                view.Colors=new Button[5];view.ColorSamples=new Image[5];
                for(int i=0;i<5;i++)
                {
                    var button=Button(root.transform,names[i],new Vector2(425,-72-i*45),new Vector2(245,38));
                    var label=button.GetComponentInChildren<Text>();HudElements.Rect(label.rectTransform,new Vector2(8,-5),new Vector2(185,28));label.alignment=TextAnchor.MiddleLeft;
                    var sample=ui.Panel(button.transform,"Образец цвета",new Vector2(207,-7),new Vector2(28,24)).GetComponent<Image>();sample.raycastTarget=false;
                    view.Colors[i]=button;view.ColorSamples[i]=sample;
                }
                var toggleRoot=ui.Panel(root.transform,"Включение второго цвета",new Vector2(425,-307),new Vector2(245,38));toggleRoot.GetComponent<Image>().color=Color.clear;
                var background=ui.Panel(toggleRoot.transform,"Флажок",new Vector2(0,-6),new Vector2(26,26)).GetComponent<Image>();background.color=new Color(.15f,.3f,.2f);
                var check=ui.Panel(background.transform,"Отметка",new Vector2(5,-5),new Vector2(16,16)).GetComponent<Image>();check.color=new Color(.65f,1,.75f);
                var toggle=toggleRoot.AddComponent<Toggle>();toggle.targetGraphic=background;toggle.graphic=check;view.SecondaryEnabled=toggle;
                Label(toggleRoot.transform,"Подпись флажка","Второй цвет гривы",new Vector2(35,-7),new Vector2(210,28));
                view.ColorLabel=Label(root.transform,"Выбранная область","Грива",new Vector2(712,-70),new Vector2(260,32),22);
                view.Hex=Input(root.transform,"Цвет RGB",new Vector2(712,-112),new Vector2(244,38),6);
                view.Hex.contentType=InputField.ContentType.Alphanumeric;
                view.Channels=new Slider[3];view.ChannelValues=new Text[3];
                string[] channels={"R","G","B"};
                for(int i=0;i<3;i++)
                {
                    float y=-175-i*52;
                    Label(root.transform,"Канал "+channels[i],channels[i],new Vector2(712,y),new Vector2(25,28));
                    var rail=ui.Panel(root.transform,"Канал "+channels[i]+" · ползунок",new Vector2(745,y-7),new Vector2(155,16));rail.GetComponent<Image>().color=new Color(.15f,.3f,.2f);
                    var handle=ui.Panel(rail.transform,"Ручка",Vector2.zero,new Vector2(14,26)).GetComponent<Image>();handle.color=new Color(.65f,1,.75f);
                    handle.rectTransform.anchorMin=handle.rectTransform.anchorMax=new Vector2(0,.5f);handle.rectTransform.pivot=new Vector2(.5f,.5f);handle.rectTransform.anchoredPosition=Vector2.zero;
                    var slider=rail.AddComponent<Slider>();slider.handleRect=handle.rectTransform;slider.targetGraphic=handle;slider.minValue=0;slider.maxValue=255;slider.wholeNumbers=true;view.Channels[i]=slider;
                    view.ChannelValues[i]=Label(root.transform,"Значение "+channels[i],"0",new Vector2(918,y),new Vector2(55,28));
                }
                view.Swatches=new Button[12];
                for(int i=0;i<12;i++)
                {
                    var swatch=ui.Panel(root.transform,"Цвет "+CharacterEditorPanel.Palette[i].ToString("X6"),new Vector2(712+i%4*62,-353-i/4*48),new Vector2(52,38));
                    var image=swatch.GetComponent<Image>();image.color=Content.PonyAppearance.FromRgb(CharacterEditorPanel.Palette[i]);
                    var button=swatch.AddComponent<Button>();button.targetGraphic=image;view.Swatches[i]=button;
                }
                view.Reset=Button(root.transform,"Исходная внешность",new Vector2(425,-480),new Vector2(245,38));
                view.Status=Label(root.transform,"Состояние создания","",new Vector2(425,-527),new Vector2(551,50),16);
                view.Cancel=Button(root.transform,"Отмена",new Vector2(566,-585),new Vector2(190,38));
                view.Create=Button(root.transform,"Создать",new Vector2(786,-585),new Vector2(190,38));
                view.Validate();PrefabUtility.SaveAsPrefabAsset(root,"Assets/Remains/Resources/Presentation/CharacterEditor.prefab");
            }
            finally {Object.DestroyImmediate(root);}
        }
        private static void UpdateMenu()
        {
            const string path="Assets/Remains/Resources/Presentation/MainMenu.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var view=root.GetComponent<MainMenuView>();var field=view.Character.GetComponent<RectTransform>();
                field.sizeDelta=new Vector2(242,45);view.Character.readOnly=true;view.Character.interactable=false;
                view.Character.text="";view.Character.textComponent.color=new Color(.65f,1,.75f);
                view.Character.textComponent.fontSize=14;
                var text=view.Character.textComponent.rectTransform;HudElements.Rect(text,new Vector2(10,-4),new Vector2(222,37));
                if(view.Character.placeholder==null)
                {
                    var hint=Label(view.Character.transform,"Подсказка выбора","Выберите персонажа",new Vector2(10,-4),new Vector2(222,37),14);
                    hint.color=new Color(.45f,.65f,.53f);view.Character.placeholder=hint;
                }
                if(view.Characters==null) view.Characters=Button(field.parent,"Персонажи",field.anchoredPosition+Vector2.right*254,new Vector2(146,45));
                ApplyMenuButtonArtwork(view);
                view.Validate();PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
        }
        public static void RefineMenuButtonArtwork()
        {
            const string path="Assets/Remains/Resources/Presentation/MainMenu.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var view=root.GetComponent<MainMenuView>();view.Validate();
                ApplyMenuButtonArtwork(view);PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();
        }
        private static void ApplyMenuButtonArtwork(MainMenuView view)
        {
            var source=view.Host.GetComponent<Image>();var image=view.Characters.GetComponent<Image>();
            if(source==null || source.sprite==null || image==null)
                throw new System.InvalidOperationException("Меню персонажей: нет сохранённой оригинальной графики кнопки запуска или Image кнопки выбора.");
            image.sprite=source.sprite;image.type=source.type;image.color=source.color;
            image.pixelsPerUnitMultiplier=source.pixelsPerUnitMultiplier;
        }
    }
}
