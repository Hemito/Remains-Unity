using System;
using System.IO;
using Remains.Content;
using Remains.Domain;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Definitions the megafactory adds to the workshop's: a platform with the short route of the car park (tmove 120) and
    // the plain button knop1, then the adapter on the factory root.
    public static class FactoryMechanismAuthoring
    {
        private const string Root="Assets/Remains/Resources/Content/FactoryLevelRoot.prefab";
        private const string Objects="Assets/Remains/Rooms/Original/Tiles/objects/";
        public const string ShortPlatformId="base:platform2-short",PlainButtonId="base:knop1";
        public static void BuildBatch()
        {
            int code=0;
            try
            {
                string folder=FactoryWorkshopMechanismAuthoring.DefinitionFolder;
                var catalog=AssetDatabase.LoadAssetAtPath<MechanismCatalogAsset>(FactoryWorkshopMechanismAuthoring.CatalogPath)
                    ??throw new InvalidOperationException("Нет каталога механизмов.");
                var platform=catalog.Platform(FactoryWorkshopMechanismAuthoring.PlatformTypeId);
                var repair=catalog.Button(FactoryWorkshopMechanismAuthoring.ButtonTypeId);
                var quick=AssetDatabase.LoadAssetAtPath<MovingPlatformAsset>(folder+"/platform2-short.asset");
                if(quick==null)
                {
                    var type=platform.CreateDefinition();type.id=ShortPlatformId;type.durationSeconds=4;
                    quick=ScriptableObject.CreateInstance<MovingPlatformAsset>();quick.Configure(type,platform.Prefab);
                    AssetDatabase.CreateAsset(quick,folder+"/platform2-short.asset");
                }
                var plain=AssetDatabase.LoadAssetAtPath<MechanismButtonAsset>(folder+"/knop1.asset");
                if(plain==null)
                {
                    var type=repair.CreateDefinition();type.id=PlainButtonId;
                    var bounds=new Rect(type.graphicOffsetX,type.graphicOffsetY,type.graphicWidth,type.graphicHeight);
                    var closed=FactoryWorkshopMechanismAuthoring.Artwork("knop1-closed",Texture("knop1-f1"),bounds,"Кнопка · закрыта");
                    var opened=FactoryWorkshopMechanismAuthoring.Artwork("knop1-open",Texture("knop1-f15"),bounds,"Кнопка · открыта");
                    plain=ScriptableObject.CreateInstance<MechanismButtonAsset>();plain.Configure(type,closed,opened);
                    AssetDatabase.CreateAsset(plain,folder+"/knop1.asset");
                }
                catalog.Configure(new[]{platform,quick},new[]{repair,plain});
                EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
                var root=PrefabUtility.LoadPrefabContents(Root);
                try
                {
                    Install(root,platform,quick,plain,repair);
                    PrefabUtility.SaveAsPrefabAsset(root,Root,out bool saved);
                    if(!saved) throw new IOException("Не записан корень завода: "+Root);
                }
                finally {PrefabUtility.UnloadPrefabContents(root);}
                AssetDatabase.SaveAssets();
                Debug.Log("Механизмы завода: PASS — записаны определения и адаптер.");
            }
            catch(Exception error) {Debug.LogError("Механизмы завода: "+error);code=1;}
            EditorApplication.Exit(code);
        }
        public static void Install(GameObject root)
        {
            var catalog=MechanismCatalogAsset.Load();
            Install(root,catalog.Platform(FactoryWorkshopMechanismAuthoring.PlatformTypeId),catalog.Platform(ShortPlatformId),
                catalog.Button(PlainButtonId),catalog.Button(FactoryWorkshopMechanismAuthoring.ButtonTypeId));
        }
        private static void Install(GameObject root,MovingPlatformAsset platform,MovingPlatformAsset quick,MechanismButtonAsset plain,MechanismButtonAsset repair)
        {
            if(!root.TryGetComponent<FactoryMechanismAdapter>(out var adapter)) adapter=root.AddComponent<FactoryMechanismAdapter>();
            adapter.Configure(platform,quick,plain,repair);
        }
        private static Texture2D Texture(string key)=>AssetDatabase.LoadAssetAtPath<Texture2D>(Objects+key+".png")
            ??throw new InvalidOperationException("Нет рисунка кнопки: "+key);
    }
}
