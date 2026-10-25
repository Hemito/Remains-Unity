using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Remains.Editor
{
    public static class CatalogWeaponMaterialChecks
    {
        private const string Folder="Assets/Remains/Prefabs/Weapons/Original";
        public static void RegenerateBatch()
        {
            try {FirearmAuthoring.RegenerateSpriteMaterials();Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Run()
        {
            var materials=new HashSet<Material>();
            foreach(string path in Directory.GetFiles(Folder,"*.mat",SearchOption.TopDirectoryOnly).Select(path=>path.Replace('\\','/')))
            {
                var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(material==null || material.shader==null || material.shader.name!="Remains/WorldLit" || material.mainTexture!=null)
                    throw new InvalidOperationException("Материал оружия удерживает PNG или не использует WorldLit: "+path);
                int property=material.shader.FindPropertyIndex("_MainTex");
                if(property<0 || (material.shader.GetPropertyFlags(property)&ShaderPropertyFlags.PerRendererData)==0)
                    throw new InvalidOperationException("WorldLit не получает текстуру от SpriteRenderer: "+path);
                string source=File.ReadAllText(AssetDatabase.GetAssetPath(material.shader));
                if(material.passCount!=1 || !source.Contains("SampleSpriteTexture(input.uv)") || !source.Contains("UnitySprites.cginc"))
                    throw new InvalidOperationException("Изменён контракт прохода WorldLit: "+path);
                materials.Add(material);
            }
            int prefabs=0;
            foreach(string path in Directory.GetFiles(Folder,"*.prefab",SearchOption.TopDirectoryOnly).Select(path=>path.Replace('\\','/')))
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var weapon=prefab==null?null:prefab.GetComponent<WeaponView>();
                if(weapon==null || weapon.Artwork==null || !materials.Contains(weapon.Artwork.sharedMaterial))
                    throw new InvalidOperationException("Оружие не связано с подготовленным SpriteRenderer-материалом: "+path);
                foreach(var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                    if(renderer.sharedMaterials.Any(materials.Contains) && renderer is not SpriteRenderer)
                        throw new InvalidOperationException("Материал без текстуры используется вне SpriteRenderer: "+path+", renderer="+renderer.name);
                prefabs++;
            }
            if(materials.Count!=105 || prefabs!=105)
                throw new InvalidOperationException($"Изменился набор графики оружия: материалов={materials.Count}, префабов={prefabs}; ожидается 105/105.");
            Debug.Log($"Материалы оружия: PASS — {materials.Count}, PNG-ссылок нет; {prefabs} префабов используют SpriteRenderer, единственный WorldLit pass читает SampleSpriteTexture с PerRendererData.");
        }
    }
}
