using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryToolStandAuthoring
    {
        public const string Code="A1LGxhvIcc83ZfC2";
        public const string SourceId="original:plant:18:"+Code;
        public const string Folder="Assets/Remains/RoomDrafts/ToolStand";
        public const string Resource="Original/Room/WorkshopToolStand/tool-stand";
        public const string NativeImageHash="8f93d076d103e0ede7004e71e8b41ac29b583ee1b21176c2cf6f631f0b73a83d";
        public const string WorkshopPatch="base:factory-workshop-18:physical-v3";
        public const string ConnectedPatch="base:factory-connected-46-18:physical-v3";
        public static void Import()
        {
            RoomAuthoringScenes.ValidateSavedOverrides();Preflight();AssetDatabase.Refresh();
            string imagePath="Assets/Remains/Resources/"+Resource+".png";
            using(var sha=SHA256.Create())
                if(BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(imagePath))).Replace("-","").ToLowerInvariant()!=NativeImageHash)
                    throw new InvalidOperationException("Исходный PNG стенда отличается: "+imagePath);
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(imagePath);
            if(texture==null || texture.width!=73 || texture.height!=49)
                throw new InvalidOperationException("Стенд требует исходный RGBA рисунок 73×49 без изменения размера.");
            // Preserve the image registration; the authored cursor offset includes its lower part.
            var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(imagePath);
            if(sprite==null || sprite.pixelsPerUnit!=50 || Vector2.Distance(sprite.pivot,new Vector2(36,12))>.0001f)
                throw new InvalidOperationException("Потеряна исходная регистрация стенда (36;12), 50 пикселей/м.");
            AssetDatabase.CreateFolder("Assets/Remains/RoomDrafts","ToolStand");
            var material=WorldArtworkMaterial.Create(texture);AssetDatabase.CreateAsset(material,Folder+"/tool-stand.mat");
            var definition=ScriptableObject.CreateInstance<PropAsset>();
            definition.SetDefinition(new PropDefinition {id="instr2",name="Стенд для инструментов",resource=Resource,
                mass=0,width=1.46f,height=.98f,interactionOffset=new Vector2(.01f,-.24f),movable=false,hasPhysicalBody=false});
            var root=new GameObject("Стенд для инструментов");
            try
            {
                var renderer=root.AddComponent<SpriteRenderer>();renderer.sprite=sprite;renderer.sharedMaterial=material;
                renderer.sortingOrder=ActorVisualLayers.RestingObjectOrder;
                var view=root.AddComponent<PropView>();view.SetArtwork(renderer);view.ConfigurePlacementArtwork();
                definition.SetPrefab(PrefabUtility.SaveAsPrefabAsset(root,Folder+"/tool-stand.prefab").GetComponent<PropView>());
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
            AssetDatabase.CreateAsset(definition,Folder+"/tool-stand.asset");
            var loot=ScriptableObject.CreateInstance<ContainerLootAsset>();
            // Explicit starting Unity balance. The original generator and its conditional rolls are not reused.
            loot.Configure(new ContainerLootTable {id="base:workshop-tool-stand",version="1",entries=new[]{
                new ContainerLootEntry {id="pins",definition="pin",minimum=1,maximum=3},
                new ContainerLootEntry {id="repair-kits",definition="rep",minimum=1,maximum=2},
                new ContainerLootEntry {id="metal-components",definition="metal_comp",minimum=0,maximum=2}}});
            AssetDatabase.CreateAsset(loot,Folder+"/tool-stand-loot.asset");
            var catalog=EntityAssetCatalog.Load();catalog.AddProp(definition);EditorUtility.SetDirty(catalog);
            Install(definition,loot);AssetDatabase.SaveAssets();
            FactoryToolStandChecks.Run();RoomAuthoringScenes.ValidateSavedOverrides();
            Debug.Log("Цех18: настенный стенд установлен как общий закреплённый контейнер без физической опоры; vendor остаётся отложенным. Исходная графика сохранена, баланс лута явно задан Unity.");
        }
        private static void Preflight()
        {
            if(AssetDatabase.IsValidFolder(Folder) || AssetDatabase.LoadMainAssetAtPath(Folder+"/tool-stand.asset")!=null)
                throw new InvalidOperationException("Стенд уже сохранён; авторские правки не заменены: "+Folder);
            var workshop=AssetDatabase.LoadAssetAtPath<GameObject>(FactoryWorkshopDraft.Path);
            var connected=AssetDatabase.LoadAssetAtPath<GameObject>(ConnectedFactoryAuthoring.Path);
            if(workshop==null || connected==null) throw new InvalidOperationException("Нет сохранённых модулей цеха и связного завода.");
            var record=workshop.GetComponent<RoomImportAudit>().Report.Records.Single(r=>r.code==Code);
            if(record.kind!="instr2" || record.recordId!="original:plant:18:record:46" || record.sourceX!=2 || record.sourceY!=22 || record.installed ||
                workshop.GetComponentsInChildren<RoomContainerPlacement>(true).Any(p=>p.Definition?.CreateDefinition().id=="instr2"))
                throw new InvalidOperationException("Исходная запись стенда изменена либо уже установлена: "+Code);
            foreach(var pair in new[]{(workshop,"base:factory-workshop-18:physical-v2"),(connected,"base:factory-connected-46-18:physical-v2")})
                if(RoomPhysicalContent.Read(pair.Item1.GetComponent<RoomAuthoring>(),"factory:0").patch!="factory:0:"+pair.Item2)
                    throw new InvalidOperationException("Перед добавлением стенда требуется проверенная физическая расстановка v2: "+pair.Item1.name);
        }
        private static void Install(PropAsset definition,ContainerLootAsset loot)
        {
            var workshop=PrefabUtility.LoadPrefabContents(FactoryWorkshopDraft.Path);
            try
            {
                var marker=new GameObject("Стенд для инструментов · "+Code);marker.transform.SetParent(workshop.transform,false);
                marker.transform.localPosition=new Vector3(2.4f,1.62f,0);
                var placement=marker.AddComponent<RoomContainerPlacement>();placement.Configure(SourceId,definition);placement.SetLoot(loot);
                var preview=(GameObject)PrefabUtility.InstantiatePrefab(definition.Prefab.gameObject,marker.transform);
                preview.transform.localPosition=Vector3.zero;
                Upgrade(workshop.GetComponent<RoomPhysicalContent>(),WorkshopPatch,"base:factory-workshop-18:physical-v2","base:factory-workshop-18:physical-v1");
                var report=workshop.GetComponent<RoomImportAudit>().Report;var record=report.Records.Single(r=>r.code==Code);
                record.installed=true;record.reason="Настенный общий контейнер: исходный рисунок и регистрация; закреплён, без физической опоры. Лут — самостоятельная начальная таблица Unity.";
                EditorUtility.SetDirty(report);PrefabUtility.SaveAsPrefabAsset(workshop,FactoryWorkshopDraft.Path);
            }
            finally {PrefabUtility.UnloadPrefabContents(workshop);}
            var connected=PrefabUtility.LoadPrefabContents(ConnectedFactoryAuthoring.Path);
            try
            {
                Upgrade(connected.GetComponent<RoomPhysicalContent>(),ConnectedPatch,"base:factory-connected-46-18:physical-v2","base:factory-connected-46-18:physical-v1");
                PrefabUtility.SaveAsPrefabAsset(connected,ConnectedFactoryAuthoring.Path);
            }
            finally {PrefabUtility.UnloadPrefabContents(connected);}
        }
        private static void Upgrade(RoomPhysicalContent content,string patch,string previous,string earliest)
        {
            content.Configure(patch);
            content.ConfigureExpansion(previous,new[]{SourceId},new[]{new ContentExpansionStep {previousPatch=earliest,
                additions=new[]{SourceId,"original:plant:18:ZoIAvJZMFks7XYbh","original:plant:18:Ainu8fDKejKYdTIz"}}});
        }
    }
}
