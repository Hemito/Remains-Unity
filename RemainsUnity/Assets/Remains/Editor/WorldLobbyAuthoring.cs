using System;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Editor
{
    public static class WorldLobbyAuthoring
    {
        public static void Run()
        {
            const string path="Assets/Remains/Resources/Presentation/WorldLobby.prefab";
            if(AssetDatabase.LoadMainAssetAtPath(path)!=null) throw new InvalidOperationException("Префаб лобби уже существует; миграция отменена.");
            var ui=new HudElements(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            var root=ui.Panel(null,"Выбор мира участниками",Vector2.zero,new Vector2(1140,680));
            try
            {
                var rect=root.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);
                var color=root.GetComponent<Image>().color;color.a=1;root.GetComponent<Image>().color=color;
                var view=root.AddComponent<WorldLobbyView>();
                view.Contents=new GameObject("Список и действия",typeof(RectTransform));view.Contents.transform.SetParent(root.transform,false);
                var contents=view.Contents.GetComponent<RectTransform>();contents.anchorMin=Vector2.zero;contents.anchorMax=Vector2.one;contents.offsetMin=contents.offsetMax=Vector2.zero;
                view.Instruction=ui.Label(contents,"Участников: 4. Выберите копию для продолжения.\nДругие ветви сохранятся; объединения прогресса нет.",new Vector2(20,-10),new Vector2(1100,50));
                view.Choices=new Button[5];
                for(int i=0;i<5;i++)
                {
                    view.Choices[i]=ui.Button(contents,"Название мира\nКопия у игрока · состояние №0\nВетвь · снимок\nСвязь с выбранной копией",new Vector2(20,-65-i*92),new Vector2(1100,90),()=>{});
                    var label=view.Choices[i].GetComponentInChildren<Text>();ConfigureIdentityLabel(label);
                    label.rectTransform.anchoredPosition=new Vector2(5,-3);label.rectTransform.sizeDelta=new Vector2(1090,84);
                }
                view.Previous=ui.Button(contents,"Назад",new Vector2(20,-535),new Vector2(150,35),()=>{});
                view.Page=ui.Label(contents,"1/1",new Vector2(190,-535),new Vector2(100,35));
                view.Next=ui.Button(contents,"Далее",new Vector2(300,-535),new Vector2(150,35),()=>{});
                view.NewWorld=ui.Button(contents,"Новый мир",new Vector2(480,-535),new Vector2(250,35),()=>{});
                view.Status=ui.Label(contents,"",new Vector2(20,-577),new Vector2(1100,40));
                view.StartGame=ui.Button(contents,"Начать с выбранной копией",new Vector2(20,-625),new Vector2(720,45),()=>{});
                view.Disconnect=ui.Button(contents,"Отключиться",new Vector2(780,-625),new Vector2(340,45),()=>{});
                var original=Resources.Load<MainMenuView>("Presentation/MainMenu").Worlds.GetComponent<Image>().sprite;
                foreach(var button in root.GetComponentsInChildren<Button>()) button.GetComponent<Image>().sprite=original;
                view.Validate();PrefabUtility.SaveAsPrefabAsset(root,path);AssetDatabase.SaveAssets();
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        // Explicit migration of existing saved UI; preserves buttons, callbacks and layout bindings.
        public static void RefineIdentityLabels()
        {
            const string path="Assets/Remains/Resources/Presentation/WorldLobby.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var view=root.GetComponent<WorldLobbyView>();view.Validate();
                foreach(var button in view.Choices) ConfigureIdentityLabel(button.GetComponentInChildren<Text>());
                PrefabUtility.SaveAsPrefabAsset(root,path);AssetDatabase.SaveAssets();
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
        }
        private static void ConfigureIdentityLabel(Text label)
        {
            label.fontSize=16;label.alignment=TextAnchor.MiddleLeft;
            label.supportRichText=false;label.horizontalOverflow=HorizontalWrapMode.Wrap;
            label.verticalOverflow=VerticalWrapMode.Truncate;label.resizeTextForBestFit=false;
        }
    }
}
