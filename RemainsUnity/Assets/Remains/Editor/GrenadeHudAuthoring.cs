using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Editor
{
    public static class GrenadeHudAuthoring
    {
        public static void Run()
        {
            const string path="Assets/Remains/Resources/Presentation/CombatHud.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var view=root.GetComponent<CombatHudView>();
                if(view.Grenades==null)
                {
                    var ui=new HudElements(view.Ammunition.font);
                    view.Grenades=ui.Label(view.Loadout.transform,"G · Гранаты: 0",new Vector2(42,-125),new Vector2(390,26));
                    view.Grenades.name="Запас гранат";view.Grenades.fontSize=16;
                    view.Grenades.alignment=TextAnchor.MiddleLeft;
                }
                if(view.GrenadeIcon==null)
                {
                    var icon=new GameObject("Иконка гранаты",typeof(RectTransform));icon.transform.SetParent(view.Loadout.transform,false);
                    HudElements.Rect((RectTransform)icon.transform,new Vector2(12,-125),new Vector2(24,24));
                    view.GrenadeIcon=icon.AddComponent<Image>();view.GrenadeIcon.preserveAspect=true;view.GrenadeIcon.raycastTarget=false;
                    new InventoryArtwork().Set(view.GrenadeIcon,"grenade");
                }
                var panel=(RectTransform)view.Loadout.transform;
                panel.sizeDelta=new Vector2(panel.sizeDelta.x,156);
                panel.anchoredPosition=new Vector2(panel.anchoredPosition.x,140.8f);
                view.Validate();PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();
        }
    }
}
