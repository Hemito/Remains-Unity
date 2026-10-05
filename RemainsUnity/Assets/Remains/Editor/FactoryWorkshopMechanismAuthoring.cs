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
    public static class FactoryWorkshopMechanismAuthoring
    {
        public const string RoomId="base:factory-workshop-18",GroupId="d2",PlatformTypeId="base:platform2",ButtonTypeId="base:knop3";
        public const string SourcePrefix="original:plant:18:",PlatformCode="TFAERLMCSehXRcAt";
        public static readonly string[] ButtonCodes={"p7r4AoLcL0QxKFy7","MlUHuFSMyaGaDCyi"};
        public const string WorkshopPatch="base:factory-workshop-18:mechanisms-v1",ConnectedPatch="base:factory-connected-46-18:mechanisms-v1";
        public const string CatalogPath="Assets/Remains/Resources/Content/mechanisms.asset";
        public const string DefinitionFolder="Assets/Remains/Resources/Content/Mechanisms",ArtworkFolder="Assets/Remains/Prefabs/Mechanisms";
        public const double TemporaryRepairDuration=2.0;
        public const float TemporaryInteractionRadius=1.6f;
        // Root executes this explicit native authoring step after integrating domain contracts.
        public static void Install()
        {
            RoomAuthoringScenes.ValidateSavedOverrides();
            Preflight();
            Folder("Assets/Remains/Resources/Content","Mechanisms");Folder("Assets/Remains/Prefabs","Mechanisms");
            var platformTexture=Texture("platform",120,35,FilterMode.Point);
            var closedTexture=Texture("button-closed",24,40,FilterMode.Point);
            var openedTexture=Texture("button-open",192,320,FilterMode.Bilinear);
            var platformDefinition=new MovingPlatformDefinition {id=PlatformTypeId,width=2.4f,height=.78f,
                graphicOffsetX=-1.2f,graphicOffsetY=.1f,graphicWidth=2.4f,graphicHeight=.7f,durationSeconds=6,oneWay=false,shotsPassThrough=false,surfaceTexture="floor",movementSound="metal"};
            var buttonDefinition=new MechanismButtonDefinition {id=ButtonTypeId,repairDurationSeconds=TemporaryRepairDuration,
                interactionRadius=TemporaryInteractionRadius,graphicOffsetX=-.24f,graphicOffsetY=0,graphicWidth=.48f,graphicHeight=.8f};
            platformDefinition.Validate();buttonDefinition.Validate();
            var platformPrefab=Artwork("platform2",platformTexture,new Rect(-1.2f,.1f,2.4f,.7f),"Движущаяся платформа");
            var closedPrefab=Artwork("knop3-closed",closedTexture,new Rect(-.24f,0,.48f,.8f),"Кнопка механизма · закрыта");
            var openedPrefab=Artwork("knop3-open",openedTexture,new Rect(-.24f,0,.48f,.8f),"Кнопка механизма · открыта");
            var platform=ScriptableObject.CreateInstance<MovingPlatformAsset>();platform.Configure(platformDefinition,platformPrefab);
            AssetDatabase.CreateAsset(platform,DefinitionFolder+"/platform2.asset");
            var button=ScriptableObject.CreateInstance<MechanismButtonAsset>();button.Configure(buttonDefinition,closedPrefab,openedPrefab);
            AssetDatabase.CreateAsset(button,DefinitionFolder+"/knop3.asset");
            var catalog=ScriptableObject.CreateInstance<MechanismCatalogAsset>();catalog.Configure(new[]{platform},new[]{button});
            AssetDatabase.CreateAsset(catalog,CatalogPath);
            var workshop=PrefabUtility.LoadPrefabContents(FactoryWorkshopDraft.Path);
            try
            {
                var room=workshop.GetComponent<RoomAuthoring>();
                workshop.AddComponent<RoomMechanismContent>().Configure(WorkshopPatch);
                var platformNode=Node(workshop,"Платформа · "+PlatformCode,new Vector2(10.8f,9.62f));
                platformNode.AddComponent<RoomMovingPlatformPlacement>().Configure(SourcePrefix+PlatformCode,RoomId,GroupId,platform,new Vector2(16.8f,0));
                PrefabUtility.InstantiatePrefab(platform.Prefab,platformNode.transform);
                foreach(int index in new[]{0,1})
                {
                    var buttonNode=Node(workshop,"Кнопка платформы · "+ButtonCodes[index],new Vector2(index==0?7.6f:29.2f,11.22f));
                    buttonNode.AddComponent<RoomMechanismButtonPlacement>().Configure(SourcePrefix+ButtonCodes[index],RoomId,GroupId,
                        SourcePrefix+PlatformCode,button,MechanismRepairStates.NeedsRepair);
                    PrefabUtility.InstantiatePrefab(button.ClosedPrefab,buttonNode.transform);
                }
                FactoryWorkshopMechanismChecks.CheckRoom(room,0);
                FactoryWorkshopMechanismChecks.CheckPreservation(RoomMechanismContent.Read(room,"factory:0"));
                // Only verified installed records leave the pending journal.
                var report=workshop.GetComponent<RoomImportAudit>().Report;
                foreach(var record in report.Records.Where(r=>r.code==PlatformCode || ButtonCodes.Contains(r.code)))
                {
                    record.installed=true;
                    record.reason=record.kind=="platform2"?
                        "Общая платформа: сохранённый маршрут и исходная графика; 6 секунд — номинальное code-derived время. Runtime проверяется отдельно.":
                        "Ремонтируемая кнопка общей группы: исходная закрытая/открытая графика, ремонт удержанием E без стоимости/навыков/случайности; завершение не запускает платформу. Время и радиус — временные настройки Unity.";
                }
                EditorUtility.SetDirty(report);PrefabUtility.SaveAsPrefabAsset(workshop,FactoryWorkshopDraft.Path);
            }
            finally {PrefabUtility.UnloadPrefabContents(workshop);}
            var connected=PrefabUtility.LoadPrefabContents(ConnectedFactoryAuthoring.Path);
            try
            {
                connected.AddComponent<RoomMechanismContent>().Configure(ConnectedPatch);
                FactoryWorkshopMechanismChecks.CheckRoom(connected.GetComponent<RoomAuthoring>(),38.4f);
                PrefabUtility.SaveAsPrefabAsset(connected,ConnectedFactoryAuthoring.Path);
            }
            finally {PrefabUtility.UnloadPrefabContents(connected);}
            AssetDatabase.SaveAssets();
            FactoryWorkshopMechanismChecks.Run();RoomAuthoringScenes.ValidateSavedOverrides();
            Debug.Log("Цех18: сохранены определения/префабы платформы и двух кнопок, три исходных записи установлены и проверены; vendor и instr2 остаются в журнале.");
        }
        private static void Preflight()
        {
            foreach(string path in new[]{CatalogPath,DefinitionFolder,ArtworkFolder})
                if(AssetDatabase.LoadMainAssetAtPath(path)!=null || AssetDatabase.IsValidFolder(path))
                    throw new InvalidOperationException("Механизмы уже сохранены; авторские правки не заменены: "+path);
            var workshop=AssetDatabase.LoadAssetAtPath<GameObject>(FactoryWorkshopDraft.Path);
            var connected=AssetDatabase.LoadAssetAtPath<GameObject>(ConnectedFactoryAuthoring.Path);
            if(workshop==null || connected==null || workshop.GetComponent<RoomMechanismContent>()!=null || connected.GetComponent<RoomMechanismContent>()!=null ||
                workshop.GetComponentsInChildren<RoomMovingPlatformPlacement>(true).Length!=0 || workshop.GetComponentsInChildren<RoomMechanismButtonPlacement>(true).Length!=0)
                throw new InvalidOperationException("Не найдены исходные сохранённые модули либо механизмы уже авторились.");
            var records=workshop.GetComponent<RoomImportAudit>().Report.Records;
            var platform=records.Single(r=>r.code==PlatformCode);var buttons=ButtonCodes.Select(code=>records.Single(r=>r.code==code)).ToArray();
            if(records.Length!=24 || records.Count(r=>!r.installed)!=5 || platform.recordId!="original:plant:18:record:40" ||
                platform.kind!="platform2" || platform.installed || platform.sourceX!=12 || platform.sourceY!=12 ||
                buttons[0].recordId!="original:plant:18:record:41" || buttons[1].recordId!="original:plant:18:record:43" ||
                buttons.Any(b=>b.kind!="knop3" || b.installed || b.sourceY!=10) || buttons[0].sourceX!=9 || buttons[1].sourceX!=36 ||
                new[]{platform}.Concat(buttons).Any(r=>!r.sourceData.Contains("allid=\"d2\"")))
                throw new InvalidOperationException("Три исходные записи платформы/кнопок отличаются; нужна явная проверка источника.");
        }
        private static Texture2D Texture(string key,int width,int height,FilterMode filtering)
        {
            string path="Assets/Remains/Resources/Original/WorkshopPlatform/"+key+".png";
            var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            if(importer==null) throw new InvalidOperationException("Нет исходной текстуры механизма: "+path);
            importer.textureType=TextureImporterType.Default;importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=filtering;
            importer.npotScale=TextureImporterNPOTScale.None;importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled=false;importer.alphaIsTransparency=true;importer.sRGBTexture=true;
            importer.SaveAndReimport();
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if(texture==null || texture.width!=width || texture.height!=height)
                throw new InvalidOperationException("Исходная текстура изменила размеры: "+path);
            return texture;
        }
        private static void Folder(string parent,string name)
        {if(!AssetDatabase.IsValidFolder(parent)) throw new InvalidOperationException("Нет каталога авторинга: "+parent);AssetDatabase.CreateFolder(parent,name);}
        private static GameObject Node(GameObject root,string name,Vector2 position)
        {var node=new GameObject(name);node.transform.SetParent(root.transform,false);node.transform.localPosition=position;return node;}
        private static GameObject Artwork(string key,Texture2D texture,Rect bounds,string name)
        {
            var root=new GameObject(name);
            try
            {
                var child=Node(root,"Исходная графика",new Vector2(bounds.x,bounds.y));
                var mesh=new Mesh {name=key+"-artwork"};
                mesh.vertices=new[]{Vector3.zero,new Vector3(bounds.width,0,0),new Vector3(0,bounds.height,0),new Vector3(bounds.width,bounds.height,0)};
                mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one};mesh.triangles=new[]{0,2,1,2,3,1};mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh,ArtworkFolder+"/"+key+"-mesh.asset");
                child.AddComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=child.AddComponent<MeshRenderer>();renderer.sortingOrder=10;
                var material=WorldArtworkMaterial.Create(texture);AssetDatabase.CreateAsset(material,ArtworkFolder+"/"+key+".mat");renderer.sharedMaterial=material;
                return PrefabUtility.SaveAsPrefabAsset(root,ArtworkFolder+"/"+key+".prefab");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
