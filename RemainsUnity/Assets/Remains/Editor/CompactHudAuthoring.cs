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
                float width=view.Health.rectTransform.sizeDelta.x;
                void Bar(Image fill,Text label,float top,int fontSize)
                {
                    var bar=fill.transform.parent as RectTransform;
                    if(bar==null || bar.parent!=view.Vitals.transform || label.transform.parent!=view.Vitals.transform)
                        throw new InvalidOperationException("HUD: изменилась структура шкалы "+fill.name);
                    float height=width*fill.sprite.rect.height/fill.sprite.rect.width;
                    HudElements.Rect(bar,new Vector2(12,-top),new Vector2(width,height));
                    HudElements.Rect(fill.rectTransform,Vector2.zero,new Vector2(width,height));
                    HudElements.Rect(label.rectTransform,new Vector2(16,-top),new Vector2(width-8,height));
                    label.fontSize=fontSize;label.alignment=TextAnchor.MiddleLeft;
                }
                Bar(view.Health,view.HealthText,32,16);
                Bar(view.Mana,view.ManaText,70,12);
                HudElements.Rect(view.ManaText.rectTransform,new Vector2(width+20,-67),new Vector2(150,24));
                view.ManaText.fontSize=16;
                var manaOutline=view.ManaText.GetComponent<Outline>()??view.ManaText.gameObject.AddComponent<Outline>();
                manaOutline.effectColor=new Color(0,0,0,.9f);manaOutline.effectDistance=new Vector2(1,-1);
                HudElements.Rect(view.CharacterName.rectTransform,new Vector2(12,-5),new Vector2(width,24));
                var outline=view.CharacterName.GetComponent<Outline>()??view.CharacterName.gameObject.AddComponent<Outline>();
                outline.effectColor=new Color(0,0,0,.9f);outline.effectDistance=new Vector2(1,-1);
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
                view.Travel=elements.Button(root.transform,"На завод 0",new Vector2(8,-26),new Vector2(214,26),()=>{});
                view.Inventory=elements.Button(root.transform,"Инвентарь · Tab",new Vector2(8,-55),new Vector2(104,26),()=>{});
                view.Menu=elements.Button(root.transform,"Меню · Esc",new Vector2(118,-55),new Vector2(104,26),()=>{});
                foreach(var button in new[]{view.Travel,view.Inventory,view.Menu})
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
                ((RectTransform)view.Loadout.transform).anchoredPosition=new Vector2(18,116.8f);
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
        public static void FinalizeLayout()
        {
            const string sessionPath="Assets/Remains/Resources/Presentation/SessionHud.prefab";
            var root=PrefabUtility.LoadPrefabContents(sessionPath);
            try
            {
                var view=root.GetComponent<SessionHudView>();
                foreach(var button in new[]{view.Travel,view.Inventory,view.Menu}) AssignButtonArtwork(button);
                view.Validate();
                PrefabUtility.SaveAsPrefabAsset(root,sessionPath);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            const string combatPath="Assets/Remains/Resources/Presentation/CombatHud.prefab";
            root=PrefabUtility.LoadPrefabContents(combatPath);
            try
            {
                ((RectTransform)root.GetComponent<CombatHudView>().Loadout.transform).anchoredPosition=new Vector2(18,116.8f);
                PrefabUtility.SaveAsPrefabAsset(root,combatPath);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();PrototypeBuild.Build();
        }
    }
}
