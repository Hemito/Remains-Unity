using System;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Editor
{
    // Explicit saved-prefab edit. Normal builds preserve designer changes.
    public static class PersonalInventoryAuthoring
    {
        public static void Run()
        {
            EditContents();EditShell();
            const string oldShared="Assets/Remains/Resources/Presentation/SharedInventory.prefab";
            if(AssetDatabase.LoadMainAssetAtPath(oldShared)!=null && !AssetDatabase.DeleteAsset(oldShared))
                throw new InvalidOperationException("Не удалось удалить устаревшую встроенную страницу контейнера.");
            AssetDatabase.SaveAssets();WeaponInventoryUiChecks.BuildAndRender();
        }
        private static void EditContents()
        {
            const string path="Assets/Remains/Resources/Presentation/PersonalInventory.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var view=root.GetComponent<PersonalInventoryView>();
                if(view==null || view.Categories==null || view.Categories.Length is not (4 or 5))
                    throw new InvalidOperationException("Не найдены исходные вкладки личного инвентаря.");
                var equipment=root.transform.Find("Место экипировки");
                if(equipment!=null) UnityEngine.Object.DestroyImmediate(equipment.gameObject);
                foreach(var text in root.GetComponentsInChildren<Text>(true))
                    if(text.text.StartsWith("Инвентарь ·",StringComparison.Ordinal) || text.text=="Выберите предмет, затем действие справа")
                        UnityEngine.Object.DestroyImmediate(text.gameObject);
                if(view.Categories.Length==4)
                {
                    var categories=new Button[5];Array.Copy(view.Categories,categories,4);
                    categories[4]=UnityEngine.Object.Instantiate(categories[3],root.transform,false);
                    view.Categories=categories;
                }
                string[] names={"Все","Оружие","Боеприпасы","Предметы","Броня"};
                for(int i=0;i<names.Length;i++)
                {
                    var button=view.Categories[i];button.name="Категория · "+names[i];
                    HudElements.Rect(button.GetComponent<RectTransform>(),new Vector2(20+i*158,-18),new Vector2(148,28));
                    var label=button.GetComponentInChildren<Text>();label.text=names[i];label.fontSize=15;
                    HudElements.Rect(label.rectTransform,new Vector2(2,-2),new Vector2(144,24));
                    label.alignment=TextAnchor.MiddleCenter;
                }
                view.Weapons=ItemShortcutAuthoring.Configure(view.Weapons,root.transform,new Vector2(20,-56));
                view.Validate();PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
        }
        private static void EditShell()
        {
            const string path="Assets/Remains/Resources/Presentation/InventoryWindow.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var view=root.GetComponent<InventoryWindowView>();
                if(view==null || view.PersonalContents==null) throw new InvalidOperationException("Нет личной страницы в корпусе инвентаря.");
                foreach(var button in root.GetComponentsInChildren<Button>(true))
                    if(!button.transform.IsChildOf(view.PersonalContents.transform)) UnityEngine.Object.DestroyImmediate(button.gameObject);
                var shared=root.transform.Find("Общий контейнер");
                if(shared!=null) UnityEngine.Object.DestroyImmediate(shared.gameObject);
                view.Personal.SetActive(true);view.Validate();PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
        }
    }
}
