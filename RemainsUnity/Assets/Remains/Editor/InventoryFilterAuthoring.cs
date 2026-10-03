using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Editor
{
    public static class InventoryFilterAuthoring
    {
        public static void Run()
        {
            const string path="Assets/Remains/Resources/Presentation/PersonalInventory.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var view=root.GetComponent<PersonalInventoryView>();
                string[] names={"Все","Оружие","Боеприпасы","Предметы"};
                if(view.Categories==null || view.Categories.Length==0)
                {
                    view.Categories=new Button[names.Length];
                    var ui=new HudElements(view.Title.font);
                    var sprite=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Remains/Resources/Presentation/MainMenuArtwork/button-glow.asset");
                    if(sprite==null) throw new System.InvalidOperationException("Не найдена оригинальная графика кнопки фильтра.");
                    for(int i=0;i<names.Length;i++)
                    {
                        var button=ui.Button(root.transform,names[i],new Vector2(220+i*142,-18),new Vector2(136,28),()=>{});
                        button.name="Категория · "+names[i];
                        button.GetComponent<Image>().sprite=sprite;
                        var label=button.GetComponentInChildren<Text>();label.fontSize=15;
                        HudElements.Rect(label.rectTransform,new Vector2(2,-2),new Vector2(132,24));
                        label.alignment=TextAnchor.MiddleCenter;
                        view.Categories[i]=button;
                    }
                }
                view.Title.rectTransform.sizeDelta=new Vector2(190,28);
                view.Title.text="Инвентарь · 1/1";
                view.Validate();PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();PrototypeBuild.Build();
        }
    }
}
