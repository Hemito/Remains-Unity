using System;
using Remains.Content;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class HeldGrenadeAuthoring
    {
        public static void Run()
        {
            const string path="Assets/Remains/Prefabs/Weapons/grenade.prefab";
            var definition=Resources.Load<WeaponAsset>("Content/weapons/grenade");
            if(definition==null || definition.GrenadeFlight==null)
                throw new InvalidOperationException("Для удерживаемой гранаты нужны определение и назначенный префаб полёта.");
            if(definition.Presentation!=null || AssetDatabase.LoadMainAssetAtPath(path)!=null)
                throw new InvalidOperationException("Удерживаемая граната уже создана; перезапись отменена.");
            var root=new GameObject("Граната в копытах");
            try
            {
                var renderer=root.AddComponent<SpriteRenderer>();
                renderer.sprite=definition.GrenadeFlight.sprite;
                renderer.sharedMaterial=definition.GrenadeFlight.sharedMaterial;
                renderer.sortingOrder=100;
                var view=root.AddComponent<WeaponView>();view.Configure("grenade",renderer,null);
                var prefab=PrefabUtility.SaveAsPrefabAsset(root,path);
                definition.SetPresentation(prefab.GetComponent<WeaponView>());
                EditorUtility.SetDirty(definition);AssetDatabase.SaveAssets();
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
            ContractChecks.Run();
        }
    }
}
