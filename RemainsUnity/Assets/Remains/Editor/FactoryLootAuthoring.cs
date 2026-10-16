using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Gameplay;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryLootAuthoring
    {
        public const string CatalogPath="Assets/Remains/Resources/Content/FactoryLoot.asset";
        private const string Folder="Assets/Remains/FactoryLoot";
        public static void InstallBatch()
        {
            try {Install();EditorApplication.Exit(0);}
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Install()
        {
            // A batch process owns its Editor lock; never write a live user's prefab stage.
            if(!Application.isBatchMode && File.Exists("Temp/UnityLockfile"))
                throw new InvalidOperationException("Подготовьте контейнеры в отдельном batch Editor после сохранения и закрытия проекта.");
            var stage=UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            if(stage!=null && stage.assetPath==FactoryLevelAuthoring.PrefabPath)
                throw new InvalidOperationException("Закройте редактор префаба завода перед подготовкой лута.");
            var source=AssetDatabase.LoadAssetAtPath<ExportedContentCatalog>(ExportedContentAuthoring.CatalogPath)
                ??throw new InvalidOperationException("Не подготовлен каталог исходных предметов.");
            var factory=AssetDatabase.LoadAssetAtPath<FactoryCatalogAsset>(FactoryCatalogAuthoring.AssetPath)
                ??throw new InvalidOperationException("Не подготовлен каталог завода.");
            var entities=EntityAssetCatalog.Load();
            var active=new Dictionary<string,PropAsset>(StringComparer.Ordinal);
            var serializedEntities=new SerializedObject(entities);
            var props=serializedEntities.FindProperty("props");
            for(int index=0;index<props.arraySize;index++)
            {
                var prop=props.GetArrayElementAtIndex(index).objectReferenceValue as PropAsset;
                if(prop?.Prefab?.ContainerArtwork!=null)active.Add(prop.CreateDefinition().id,prop);
            }
            var existing=new Dictionary<string,PropAsset>(active,StringComparer.Ordinal);
            foreach(var group in AssetDatabase.FindAssets("t:PropAsset")
                .Select(g=>AssetDatabase.LoadAssetAtPath<PropAsset>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(a=>a?.Prefab?.ContainerArtwork!=null).GroupBy(a=>a.CreateDefinition().id,StringComparer.Ordinal))
            {
                if(existing.ContainsKey(group.Key))continue;
                var candidates=group.ToArray();
                if(candidates.Length!=1)throw new InvalidOperationException("Несколько незарегистрированных ассетов контейнера type="+group.Key);
                existing.Add(group.Key,candidates[0]);
            }
            string file=ContentPackage.Catalog;
            byte[] bytes=File.ReadAllBytes(file);
            var package=JObject.Parse(System.Text.Encoding.UTF8.GetString(bytes));
            var entries=new List<FactoryContainerDefinition>();
            foreach(var item in ((JArray)package["definitions"]).OfType<JObject>().Where(i=>(string)i["category"]=="obj"))
            {
                string xmlText=(string)item["rawXml"];var xml=XElement.Parse(xmlText);
                if(xml.Attribute("cont")==null || (string)xml.Attribute("inter")!="1")continue;
                string id=(string)xml.Attribute("id");
                if(!existing.TryGetValue(id,out var physical))
                {
                    if(Number(xml,"wall")<=0)
                        Debug.LogWarning($"Контейнер type={id}: нет проверенного физического ассета/массы; определение лута сохранено, размещение пока пропускается.");
                    else physical=CreateStationary(factory,id,(string)item["name"],xml);
                }
                if(physical!=null)
                {
                    if(physical.Prefab==null || physical.Prefab.ContainerArtwork==null)
                        throw new InvalidOperationException("Нет проверенного контейнерного профиля type="+id);
                    entities.AddProp(physical);
                }
                entries.Add(new FactoryContainerDefinition {id=id,rawXml=xmlText,physical=physical});
            }
            var catalog=AssetDatabase.LoadAssetAtPath<FactoryLootCatalog>(CatalogPath);
            if(catalog==null){catalog=ScriptableObject.CreateInstance<FactoryLootCatalog>();AssetDatabase.CreateAsset(catalog,CatalogPath);}
            using var sha=SHA256.Create();
            catalog.Configure(source,BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant(),entries.ToArray());
            EditorUtility.SetDirty(catalog);EditorUtility.SetDirty(entities);
            var prefab=PrefabUtility.LoadPrefabContents(FactoryLevelAuthoring.PrefabPath);
            try {InstallOnRoot(prefab.GetComponent<FactoryLevelRoot>());PrefabUtility.SaveAsPrefabAsset(prefab,FactoryLevelAuthoring.PrefabPath,out bool saved);
                if(!saved)throw new IOException("Не сохранён префаб завода с контейнерами.");}
            finally {PrefabUtility.UnloadPrefabContents(prefab);}
            AssetDatabase.SaveAssets();Debug.Log($"FactoryLootAuthoring: PASS — definitions={entries.Count}, physical={entries.Count(e=>e.physical!=null)}.");
        }
        public static void InstallOnRoot(FactoryLevelRoot root)
        {
            var catalog=AssetDatabase.LoadAssetAtPath<FactoryLootCatalog>(CatalogPath)
                ??throw new InvalidOperationException("Сначала подготовьте FactoryLoot.asset.");
            var adapter=root.GetComponent<FactoryContainerAdapter>()??root.gameObject.AddComponent<FactoryContainerAdapter>();
            adapter.Configure(catalog);
        }
        private static PropAsset CreateStationary(FactoryCatalogAsset factory,string id,string title,XElement xml)
        {
            string path=Folder+"/"+id;
            if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets/Remains","FactoryLoot");
            var existing=AssetDatabase.LoadAssetAtPath<PropAsset>(path+".asset");if(existing!=null)return existing;
            string paletteId="remains:obj:"+id;
            if(!factory.Definitions.Items.TryGetValue(paletteId,out var definition))
            {
                Debug.LogWarning($"Контейнер type={id}: нет определения {paletteId} в палитре завода; исходное определение лута сохранено, размещение пропускается.");
                return null;
            }
            var sprite=factory.Art.Tile("object:"+definition.Art+":1").sprite;
            if(sprite==null)throw new InvalidOperationException("Нет исходного спрайта контейнера: "+id);
            var profile=ScriptableObject.CreateInstance<ContainerArtworkProfileAsset>();
            // Multiple source frames alone do not establish an opening animation.
            profile.Configure(id,ContainerArtworkMode.Static,sprite);AssetDatabase.CreateAsset(profile,path+"-art.asset");
            var material=WorldArtworkMaterial.Create(sprite.texture);AssetDatabase.CreateAsset(material,path+".mat");
            var asset=ScriptableObject.CreateInstance<PropAsset>();
            asset.SetDefinition(new PropDefinition {id=id,name=title,resource="FactoryLoot/"+id,width=sprite.bounds.size.x,height=sprite.bounds.size.y,
                mass=0,movable=false,hasPhysicalBody=false});
            var root=new GameObject(title??id);
            try
            {
                var renderer=root.AddComponent<SpriteRenderer>();renderer.sprite=sprite;renderer.sharedMaterial=material;
                renderer.sortingOrder=ActorVisualLayers.RestingObjectOrder;
                var view=root.AddComponent<PropView>();view.SetArtwork(renderer);view.ConfigurePlacementArtwork();view.ConfigureContainerArtwork(profile);
                asset.SetPrefab(PrefabUtility.SaveAsPrefabAsset(root,path+".prefab").GetComponent<PropView>());
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
            AssetDatabase.CreateAsset(asset,path+".asset");return asset;
        }
        private static double Number(XElement xml,string name)=>double.TryParse((string)xml.Attribute(name),NumberStyles.Float,CultureInfo.InvariantCulture,out var value)?value:0;
    }
}
