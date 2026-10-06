using System;
using System.Linq;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Editor
{
    public static class PauseMenuAuthoring
    {
        public static void RefineControls()
        {
            const string path="Assets/Remains/Resources/Presentation/PauseMenu.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var view=root.GetComponent<PauseMenuView>();
                if(view==null) throw new InvalidOperationException("Меню паузы: нет компонента PauseMenuView в "+path);
                var candidates=root.GetComponentsInChildren<Text>(true)
                    .Where(text=>text==view.Controls || text.text.StartsWith("A/D —",StringComparison.Ordinal)).ToArray();
                if(candidates.Length!=1) throw new InvalidOperationException($"Меню паузы: ожидается одна сохранённая справка, найдено {candidates.Length}, путь={path}.");
                view.Controls=candidates[0];view.Controls.name="Справка управления";
                var side=view.Controls.transform.parent.GetComponent<RectTransform>();
                if(side==null || side.name!="Параметры сессии")
                    throw new InvalidOperationException("Меню паузы: справка находится вне сохранённой панели параметров сессии.");
                float extra=350-side.sizeDelta.x;
                foreach(RectTransform child in side)
                {
                    child.sizeDelta+=Vector2.right*extra;
                    foreach(var text in child.GetComponentsInChildren<Text>(true))
                        if(text.transform!=child) text.rectTransform.sizeDelta+=Vector2.right*extra;
                }
                HudElements.Rect(side,new Vector2(-370,0),new Vector2(350,490));
                HudElements.Rect(view.Close.GetComponent<RectTransform>(),new Vector2(350,-369),new Vector2(310,44));
                HudElements.Rect(view.Controls.rectTransform,new Vector2(12,-250),new Vector2(326,230));
                view.Controls.fontSize=14;view.Controls.resizeTextForBestFit=false;
                view.Controls.alignment=TextAnchor.UpperLeft;view.Controls.raycastTarget=false;
                view.Controls.text="A/D — ходьба · Shift — бег\n"+
                    "W/S — лестница · S/W — присесть/встать\n"+
                    "S дважды — спуск сквозь настил\n"+
                    "Пробел — прыжок (держать — выше)\n"+
                    "Мышь — прицел · ЛКМ — атака/бросок\n"+
                    "Q держать — телекинез; отпустить — сброс\n"+
                    "E по курсору — открыть/подобрать\n"+
                    "E держать — поднять союзника/ремонт\n"+
                    "R — перезарядка · H — аптечка себе\n"+
                    "G — быстрый бросок гранаты\n"+
                    "1234567890-= — выбрать предмет\n"+
                    "Tab: выбрать вещь → ячейка клавиши\n"+
                    "Shift+ЛКМ — стопка обычной вещи\n"+
                    "Esc — закрыть окно / открыть меню";
                view.Validate();PrefabUtility.SaveAsPrefabAsset(root,path);AssetDatabase.SaveAssets();
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
        }
        public static void RefineAndBuild()
        {
            RefineControls();WorldLobbyAuthoring.RefineIdentityLabels();
            PauseMenuRender.Render();WorldLobbyRender.Render();PrototypeBuild.Build();
        }
    }
}
