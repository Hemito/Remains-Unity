using System;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Editor
{
    // Explicit authoring command. Runtime only instantiates the saved prefab.
    public static class CompactHudAuthoring
    {
        public static void RefineVitals()
        {
            const string path="Assets/Remains/Resources/Presentation/CombatHud.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var view=root.GetComponent<CombatHudView>();view.Validate();
                view.Vitals.GetComponent<Image>().color=Color.clear;
                var healthArea=view.Health.transform.parent as RectTransform;
                if(healthArea==null || !float.IsFinite(healthArea.rect.width) || healthArea.rect.width<=0)
                    throw new InvalidOperationException("HUD: у сохранённой области здоровья нет положительной полной ширины; проверьте CombatHud.prefab.");
                float width=healthArea.rect.width;
                void Bar(Image fill,float top,float height)
                {
                    var bar=fill.transform.parent as RectTransform;
                    if(bar==null || bar.parent!=view.Vitals.transform)
                        throw new InvalidOperationException("HUD: изменилась структура шкалы "+fill.name);
                    HudElements.Rect(bar,new Vector2(12,-top),new Vector2(width,height));
                    // Fit the complete gradient into its occupied portion instead of clipping its UVs.
                    fill.type=Image.Type.Simple;
                    fill.rectTransform.anchorMin=Vector2.zero;fill.rectTransform.anchorMax=Vector2.one;
                    fill.rectTransform.offsetMin=fill.rectTransform.offsetMax=Vector2.zero;
                }
                // Original artwork layout, independently specified in HUD_REFERENCE_SPEC.md.
                // The mana export is 200x12 before its source vertical compression.
                const float healthTop=32;
                float healthHeight=width*view.Health.sprite.rect.height/view.Health.sprite.rect.width;
                float manaHeight=width*view.Mana.sprite.rect.height/view.Mana.sprite.rect.width*.5833435f;
                float manaTop=healthTop+healthHeight+healthHeight*.04098f;
                Bar(view.Health,healthTop,healthHeight);Bar(view.Mana,manaTop,manaHeight);
                var frame=view.Health.transform.Find("Рамка здоровья")??view.Health.transform.parent.Find("Рамка здоровья");
                if(frame==null) throw new InvalidOperationException("HUD: нет сохранённой рамки здоровья.");
                frame.SetParent(view.Health.transform.parent,false);frame.SetAsLastSibling();
                HudElements.Rect(view.HealthText.rectTransform,new Vector2(12,-healthTop-healthHeight*.1001f),
                    new Vector2(width,healthHeight*.7971f));
                view.HealthText.fontSize=20;view.HealthText.alignment=TextAnchor.MiddleCenter;
                view.HealthText.color=new Color(0,1,.6f);view.HealthText.text="100/100";
                HudElements.Rect(view.ManaText.rectTransform,new Vector2(width+20,-manaTop-(manaHeight-24)*.5f),new Vector2(150,24));
                view.ManaText.fontSize=16;
                var manaOutline=view.ManaText.GetComponent<Outline>()??view.ManaText.gameObject.AddComponent<Outline>();
                manaOutline.effectColor=new Color(0,0,0,.9f);manaOutline.effectDistance=new Vector2(1,-1);
                HudElements.Rect(view.CharacterName.rectTransform,new Vector2(12,-5),new Vector2(width,24));
                var outline=view.CharacterName.GetComponent<Outline>()??view.CharacterName.gameObject.AddComponent<Outline>();
                outline.effectColor=new Color(0,0,0,.9f);outline.effectDistance=new Vector2(1,-1);
                ((RectTransform)view.Vitals.transform).sizeDelta=new Vector2(310,90);
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();CombatHudRender.Render();CombatHudRender.RenderHealth();
        }
        public static void Run()
        {
            const string path="Assets/Remains/Resources/Presentation/SessionHud.prefab";
            if(AssetDatabase.LoadAssetAtPath<GameObject>(path)!=null)
                throw new InvalidOperationException("Панель сессии уже существует; авторские изменения не заменены.");
            var elements=new HudElements(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            var root=elements.Panel(null,"Панель сессии",Vector2.zero,new Vector2(230,88));
            try
            {
                var rect=(RectTransform)root.transform;rect.anchorMin=rect.anchorMax=rect.pivot=Vector2.one;rect.anchoredPosition=new Vector2(-16,-16);
                root.GetComponent<Image>().color=new Color(.04f,.08f,.07f,.72f);
                var view=root.AddComponent<SessionHudView>();
                view.Status=elements.Label(root.transform,"Хаб · игроков: 1",new Vector2(8,-4),new Vector2(214,20));view.Status.fontSize=14;
                view.Inventory=elements.Button(root.transform,"Инвентарь · Tab",new Vector2(8,-55),new Vector2(104,26),()=>{});
                view.Menu=elements.Button(root.transform,"Меню · Esc",new Vector2(118,-55),new Vector2(104,26),()=>{});
                foreach(var button in new[]{view.Inventory,view.Menu})
                {
                    AssignButtonArtwork(button);
                    var label=button.GetComponentInChildren<Text>();label.fontSize=13;
                    HudElements.Rect(label.rectTransform,new Vector2(2,-2),((RectTransform)button.transform).sizeDelta-new Vector2(4,4));
                }
                view.Validate();PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
            const string combatPath="Assets/Remains/Resources/Presentation/CombatHud.prefab";
            var combat=PrefabUtility.LoadPrefabContents(combatPath);
            try
            {
                var view=combat.GetComponent<CombatHudView>();view.Validate();
                foreach(var panel in new[]{view.Vitals,view.Loadout})
                {
                    panel.transform.localScale=Vector3.one*.8f;
                    panel.GetComponent<Image>().color=new Color(.04f,.08f,.07f,.72f);
                }
                PlaceLoadout(view);
                PrefabUtility.SaveAsPrefabAsset(combat,combatPath);
            }
            finally {PrefabUtility.UnloadPrefabContents(combat);}
            AssetDatabase.SaveAssets();PrototypeBuild.Build();
        }
        private static void AssignButtonArtwork(Button button)
        {
            var sprite=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Remains/Resources/Presentation/MainMenuArtwork/button-glow.asset");
            if(sprite==null) throw new InvalidOperationException("Нет сохранённого оригинального спрайта кнопки.");
            button.GetComponent<Image>().sprite=sprite;
        }
        private static void PlaceLoadout(CombatHudView view)
        {
            var rect=(RectTransform)view.Loadout.transform;
            // Keep the authored panel above the lower margin when its height changes.
            rect.anchoredPosition=new Vector2(18,16+rect.rect.height*rect.localScale.y);
        }
        public static void FinalizeLayout()
        {
            const string sessionPath="Assets/Remains/Resources/Presentation/SessionHud.prefab";
            var root=PrefabUtility.LoadPrefabContents(sessionPath);
            try
            {
                var view=root.GetComponent<SessionHudView>();
                foreach(var button in new[]{view.Inventory,view.Menu}) AssignButtonArtwork(button);
                view.Validate();
                PrefabUtility.SaveAsPrefabAsset(root,sessionPath);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            const string combatPath="Assets/Remains/Resources/Presentation/CombatHud.prefab";
            root=PrefabUtility.LoadPrefabContents(combatPath);
            try
            {
                PlaceLoadout(root.GetComponent<CombatHudView>());
                PrefabUtility.SaveAsPrefabAsset(root,combatPath);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();PrototypeBuild.Build();
        }
        public static void RefineAndBuild()
        {
            RefineVitals();CharacterInterfaceAuthoring.RefineMenuButtonArtwork();
            CombatHudPrefabChecks.RenderAndCheck();CombatHudRender.RenderReferenceSizes();
            MainMenuPrefabChecks.Run();MainMenuRender.Render();MainMenuRender.RenderLongName();
            PrototypeBuild.Build();
        }
    }
}
