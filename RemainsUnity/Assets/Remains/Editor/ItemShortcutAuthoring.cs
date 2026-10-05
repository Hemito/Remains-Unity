using System;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;

namespace Remains.Editor
{
    public static class ItemShortcutAuthoring
    {
        public static void Run()
        {
            const string droppedPath="Assets/Remains/Resources/Presentation/DroppedItem.prefab";
            if(AssetDatabase.LoadAssetAtPath<PropView>(droppedPath)==null)
            {
                var root=new GameObject("DroppedItem",typeof(SpriteRenderer),typeof(PropView),typeof(SortingGroup));
                try
                {
                    root.GetComponent<PropView>().SetArtwork(root.GetComponent<SpriteRenderer>());
                    root.GetComponent<SortingGroup>().sortingOrder=ActorVisualLayers.RestingObjectOrder;
                    PrefabUtility.SaveAsPrefabAsset(root,droppedPath);
                }
                finally {UnityEngine.Object.DestroyImmediate(root);}
            }
            Edit("Assets/Remains/Resources/Presentation/PersonalInventory.prefab",root=>
            {
                var view=root.GetComponent<PersonalInventoryView>();
                view.Weapons=Configure(view.Weapons,root.transform,new Vector2(20,-56));view.Validate();
            });
            Edit("Assets/Remains/Resources/Presentation/CombatHud.prefab",root=>
            {
                var view=root.GetComponent<CombatHudView>();
                view.Weapons=Configure(view.Weapons,view.Loadout.transform,new Vector2(14,-12));
                view.Loadout.GetComponent<RectTransform>().sizeDelta=new Vector2(800,156);
                view.Ammunition.rectTransform.sizeDelta=new Vector2(772,view.Ammunition.rectTransform.sizeDelta.y);
                view.Validate();
            });
            AssetDatabase.SaveAssets();WeaponInventoryUiChecks.BuildAndRender();
        }
        private static void Edit(string path,Action<GameObject> change)
        {
            var root=PrefabUtility.LoadPrefabContents(path);
            try {change(root);PrefabUtility.SaveAsPrefabAsset(root,path);}
            finally {PrefabUtility.UnloadPrefabContents(root);}
        }
        public static Button[] Configure(Button[] previous,Transform parent,Vector2 position)
        {
            if(previous==null || previous.Length is not (4 or 12) || Array.Exists(previous,value=>value==null))
                throw new InvalidOperationException("Назначения предметов: нужны исходные четыре или двенадцать сохранённых кнопок.");
            var result=new Button[ItemShortcuts.Keys.Count];Array.Copy(previous,result,previous.Length);
            for(int i=previous.Length;i<result.Length;i++) result[i]=UnityEngine.Object.Instantiate(previous[0],parent,false);
            for(int i=0;i<result.Length;i++)
            {
                string key=ItemShortcuts.Keys[i];var button=result[i];button.name="Shortcut["+key+"]";
                HudElements.Rect(button.GetComponent<RectTransform>(),position+Vector2.right*(64*i),new Vector2(62,38));
                var icon=button.transform.Find("Иконка предмета")?.GetComponent<Image>()??throw new InvalidOperationException("Нет иконки назначения ["+key+"].");
                HudElements.Rect(icon.rectTransform,new Vector2(4,-3),new Vector2(50,20));icon.preserveAspect=true;
                var label=button.GetComponentInChildren<Text>();label.name="ShortcutKey";label.text="["+key+"]";
                label.fontSize=12;label.color=new Color(1,.86f,.2f);label.alignment=TextAnchor.MiddleCenter;
                var rect=label.rectTransform;rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(1,0);
                rect.anchoredPosition=new Vector2(-3,1);rect.sizeDelta=new Vector2(26,14);
            }
            return result;
        }
    }
}
