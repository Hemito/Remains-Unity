using System;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryBigBoxAuthoring
    {
        public const string Folder="Assets/Remains/RoomDrafts/FactoryProps";
        public const string Resource="Original/Props/factory-bigbox";
        [Serializable] private sealed class Source
        {public string id,name;public float massKg,width,height,pixelsPerUnit;public int imageWidth,imageHeight,sourceFootprintCells;}
        public static void ImportAndBuild()
        {
            RoomAuthoringScenes.ValidateSavedOverrides();
            if(AssetDatabase.LoadMainAssetAtPath(Folder+"/bigbox.asset")!=null) throw new InvalidOperationException("Большой ящик уже сохранён; авторские правки не заменены.");
            var source=JsonUtility.FromJson<Source>(File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../Tools/Research/factory_bigbox_resources.json"))));
            if(source.id!="bigbox" || source.massKg!=175 || source.width!=2 || source.height!=1.6f || source.sourceFootprintCells!=2)
                throw new InvalidOperationException("Неверный исследовательский источник большого ящика.");
            AssetDatabase.Refresh();
            var importer=(TextureImporter)AssetImporter.GetAtPath("Assets/Remains/Resources/"+Resource+".jpg");
            if(importer==null) throw new InvalidOperationException("Не импортирован исходный рисунок большого ящика.");
            importer.textureType=TextureImporterType.Default;importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.npotScale=TextureImporterNPOTScale.None;importer.mipmapEnabled=false;importer.maxTextureSize=4096;importer.SaveAndReimport();
            var texture=GameAssets.Load<Texture2D>(Resource);
            if(texture==null || texture.width!=source.imageWidth || texture.height!=source.imageHeight)
                throw new InvalidOperationException("Изменены исходные размеры рисунка большого ящика.");
            if(!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Remains/RoomDrafts","FactoryProps");
            var sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),source.pixelsPerUnit,0,SpriteMeshType.FullRect);
            AssetDatabase.CreateAsset(sprite,Folder+"/bigbox-sprite.asset");
            var material=WorldArtworkMaterial.Create(texture);AssetDatabase.CreateAsset(material,Folder+"/bigbox.mat");
            var definition=ScriptableObject.CreateInstance<PropAsset>();
            definition.SetDefinition(new PropDefinition {id=source.id,name=source.name,mass=source.massKg,width=source.width,height=source.height,resource=Resource});
            var root=new GameObject(source.name);
            try
            {
                var renderer=root.AddComponent<SpriteRenderer>();renderer.sprite=sprite;renderer.sharedMaterial=material;renderer.sortingOrder=ActorVisualLayers.RestingObjectOrder;
                root.AddComponent<PropView>().SetArtwork(renderer);
                definition.SetPrefab(PrefabUtility.SaveAsPrefabAsset(root,Folder+"/bigbox.prefab").GetComponent<PropView>());
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
            AssetDatabase.CreateAsset(definition,Folder+"/bigbox.asset");
            var catalog=EntityAssetCatalog.Load();catalog.AddProp(definition);EditorUtility.SetDirty(catalog);
            Install(definition,source.sourceFootprintCells);
            AssetDatabase.SaveAssets();RoomAuthoringScenes.Verify();PrototypeBuild.Build();
        }
        private static void Install(PropAsset definition,int cells)
        {
            var root=PrefabUtility.LoadPrefabContents(FactoryWorkshopDraft.Path);
            try
            {
                var room=root.GetComponent<RoomAuthoring>();var report=root.GetComponent<RoomImportAudit>().Report;
                var records=report.Records.Where(r=>r.kind=="bigbox").ToArray();
                if(records.Length!=2 || records.Any(r=>r.installed)) throw new InvalidOperationException("Не найдены две неподключённые исходные записи bigbox.");
                foreach(var record in records)
                {
                    var marker=new GameObject("Большой металлический ящик · "+record.code);marker.transform.SetParent(root.transform,false);
                    marker.transform.localPosition=FactorySourceCoordinates.Placement(record.sourceX,record.sourceY,cells);
                    marker.AddComponent<RoomPropPlacement>().Configure("original:plant:18:"+record.code,definition);
                    var preview=(GameObject)PrefabUtility.InstantiatePrefab(definition.Prefab.gameObject,marker.transform);
                    preview.transform.localPosition=new Vector3(0,definition.CreateDefinition().height*.5f,0);
                    record.installed=true;record.reason="Физический металлический ящик 175 кг без инвентаря; исходная графика и отдельное определение.";
                }
                EditorUtility.SetDirty(report);root.GetComponent<RoomPhysicalContent>().Configure("base:factory-workshop-18:physical-v2");
                root.GetComponent<RoomPhysicalContent>().ConfigureExpansion("base:factory-workshop-18:physical-v1",
                    records.Select(r=>"original:plant:18:"+r.code).ToArray());
                PrefabUtility.SaveAsPrefabAsset(root,FactoryWorkshopDraft.Path);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            var connected=PrefabUtility.LoadPrefabContents(ConnectedFactoryAuthoring.Path);
            try
            {
                connected.GetComponent<RoomPhysicalContent>().Configure("base:factory-connected-46-18:physical-v2");
                connected.GetComponent<RoomPhysicalContent>().ConfigureExpansion("base:factory-connected-46-18:physical-v1",
                    new[]{"original:plant:18:ZoIAvJZMFks7XYbh","original:plant:18:Ainu8fDKejKYdTIz"});
                PrefabUtility.SaveAsPrefabAsset(connected,ConnectedFactoryAuthoring.Path);
            }
            finally {PrefabUtility.UnloadPrefabContents(connected);}
        }
    }
}
