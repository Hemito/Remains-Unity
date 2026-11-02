using System;
using System.Linq;
using Remains.Content;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    // Focused migration of six generated flight prefabs. Does not regenerate definitions, physics or held views.
    public static class GrenadeFlightAuthoring
    {
        private const string Folder="Assets/Remains/Prefabs/Weapons/Original/";
        public static void RepairBatch()
        {
            try{Repair();CatalogArtBuildSplit.Validate();EditorApplication.Exit(0);}
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Repair()
        {
            string[] ids=FirearmAuthoring.PlainGrenades.Concat(FirearmAuthoring.FireGrenades).ToArray();
            foreach(string id in ids)
            {
                string path=Folder+"grenade-"+id+"-flight.prefab";
                var root=PrefabUtility.LoadPrefabContents(path);
                CatalogSpriteReference reference;Material material;
                try
                {
                    var renderer=root.GetComponent<SpriteRenderer>()??throw new InvalidOperationException("Нет renderer гранаты: "+path);
                    var sprite=GrenadeArtwork.ResolveSprite(renderer);
                    material=renderer.sharedMaterial??throw new InvalidOperationException("Нет материала гранаты: "+path);
                    reference=CatalogSpriteAuthoring.Reference(sprite);
                    if(string.IsNullOrEmpty(reference.Key))throw new InvalidOperationException("Граната не ссылается на графику каталога: "+path);
                    var view=root.GetComponent<WeaponView>()??root.AddComponent<WeaponView>();
                    view.Configure("item:"+id,renderer,null);view.ConfigureRest(reference);
                    if(renderer.sprite!=null)throw new InvalidOperationException("Прямая ссылка гранаты не снята: "+path);
                    PrefabUtility.SaveAsPrefabAsset(root,path);
                }
                finally{PrefabUtility.UnloadPrefabContents(root);}
                var saved=AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<SpriteRenderer>();
                var savedView=saved.GetComponent<WeaponView>();
                var actual=GrenadeArtwork.ResolveSprite(saved);
                if(saved.sprite!=null || saved.sharedMaterial!=material || savedView.Muzzle!=null ||
                    savedView.RestReference.Key!=reference.Key || savedView.RestReference.PixelsPerUnit!=reference.PixelsPerUnit ||
                    savedView.RestReference.Rect!=reference.Rect || savedView.RestReference.Pivot!=reference.Pivot ||
                    savedView.RestReference.Bounds!=reference.Bounds || actual==null)
                    throw new InvalidOperationException("После сохранения изменилась регистрация гранаты: "+path);
                // In Editor Awake is not guaranteed for an ordinary MonoBehaviour; exercise the same explicit
                // resolution as Awake and confirm an independent instance can receive its art without asset writes.
                var instance=Object.Instantiate(saved);
                try
                {
                    var view=instance.GetComponent<WeaponView>();view.SetFrame(view.ResolveRestSprite());
                    if(instance.sprite!=actual || instance.sharedMaterial!=material || saved.sprite!=null)
                        throw new InvalidOperationException("Экземпляр гранаты не разрешил рисунок независимо: "+path);
                }
                finally{Object.DestroyImmediate(instance.gameObject);}
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"Графика гранат: PASS — префабов={ids.Length}, ключи каталога, исходные rect/pivot/PPU/bounds и материалы сохранены; прямых ссылок нет.");
        }
    }
}
