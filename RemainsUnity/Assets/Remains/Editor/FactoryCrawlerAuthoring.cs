using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryCrawlerAuthoring
    {
        public const string Patch="base:factory-connected-46-18:npcs-v3";
        public const string ActorPath="Assets/Remains/Resources/Content/actors/radroach.asset";
        public const string AtlasPath="Assets/Remains/Resources/Original/Radroach/artwork.asset";
        public const string SpawnPath="Assets/Remains/RoomDrafts/FactoryWorkshopRadroachSpawn.asset";
        public const string LootPath="Assets/Remains/RoomDrafts/FactoryWorkshopRadroachInventory.asset";
        [Serializable] private sealed class Source {public float health;public AtlasActorArtwork atlas;public ActorTypeDefinition actor;public AnatomyDefinition anatomy;}
        // Narrow repair of the existing legacy type, preserving GUIDs, material, tint and reflection.
        public static void RepairCorpsePresentation()
        {
            const string path="Assets/Remains/Prefabs/Actors/radroach.prefab";
            var atlas=AssetDatabase.LoadAssetAtPath<AtlasSpriteAsset>(AtlasPath);
            if(atlas==null || AssetDatabase.LoadAssetAtPath<GameObject>(path)==null)
                throw new InvalidOperationException("Не найден сохранённый атлас или префаб старого радтаракана.");
            AtlasSpriteAssetEditor.Rebuild(atlas);
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var visual=root.GetComponent<AtlasActorVisual>();
                if(visual==null) throw new InvalidOperationException("В префабе радтаракана нет AtlasActorVisual.");
                var saved=new SerializedObject(visual);
                if(!saved.FindProperty("customDeadPresentation").boolValue)
                    throw new InvalidOperationException("Оформление тела радтаракана не соответствует исходному авторингу.");
                visual.ConfigureDeadPresentation(saved.FindProperty("deadFlipY").boolValue,0,saved.FindProperty("deadTint").colorValue);
                if(PrefabUtility.SaveAsPrefabAsset(root,path)==null) throw new InvalidOperationException("Не сохранено исправление опоры тела радтаракана.");
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            Debug.Log("Радтаракан: изображение тела опирается на непрозрачные границы кадра; прежний подъём 0,42 м удалён.");
        }
        // Explicit one-time import. No build, session launch or destructive prefab regeneration.
        public static void Install()
        {
            RoomAuthoringScenes.ValidateSavedOverrides();
            foreach(string path in new[]{ActorPath,AtlasPath,SpawnPath,LootPath,"Assets/Remains/Prefabs/Actors/radroach.prefab","Assets/Remains/Prefabs/Actors/radroach.mat"})
                if(AssetDatabase.LoadMainAssetAtPath(path)!=null) throw new InvalidOperationException("Радтаракан уже сохранён; авторские правки не заменены: "+path);
            var data=Remains.Serialization.StateJson.FromJson<Source>(File.ReadAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../Tools/Research/crawler_resources.json"))));
            if(data?.actor?.id!="radroach" || data.health!=8 || data.atlas?.width!=480 || data.atlas.height!=30)
                throw new InvalidOperationException("Не подтверждён выбранный ресурс/вид радтаракана.");
            Preflight();
            string anatomyPath=System.IO.Path.Combine(Application.dataPath,"Remains/Resources/Content/anatomy.json");
            var anatomyJson=JObject.Parse(File.ReadAllText(anatomyPath));
            if(anatomyJson["anatomies"] is not JArray definitions || definitions.Any(a=>(string)a["id"]=="radroach") || data.anatomy?.id!="radroach")
                throw new InvalidOperationException("Анатомия радтаракана уже существует либо источник неполон; прежние данные не заменены.");
            foreach(var region in data.anatomy.regions)
                region.poses=data.atlas.clips.Select(clip=>new HitRegionPose {pose=clip.pose,
                    centers=Enumerable.Repeat(new Vector2(region.x,clip.pose=="down"?.42f-region.y:region.y),clip.count).ToArray()}).ToArray();
            AssetDatabase.Refresh();
            string texturePath="Assets/Remains/Resources/"+data.atlas.resource+".png";
            var importer=(TextureImporter)AssetImporter.GetAtPath(texturePath);
            if(importer==null) throw new InvalidOperationException("Не импортирован отдельный атлас радтаракана: "+texturePath);
            importer.textureType=TextureImporterType.Default;importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.npotScale=TextureImporterNPOTScale.None;importer.mipmapEnabled=false;importer.filterMode=FilterMode.Point;
            importer.maxTextureSize=4096;importer.SaveAndReimport();
            var atlas=ScriptableObject.CreateInstance<AtlasSpriteAsset>();atlas.Configure(Resources.Load<Texture2D>(data.atlas.resource),data.atlas);
            AssetDatabase.CreateAsset(atlas,AtlasPath);AtlasSpriteAssetEditor.Rebuild(atlas);
            var type=ScriptableObject.CreateInstance<ActorTypeAsset>();type.SetDefinition(data.actor);
            var root=new GameObject("Радтаракан");
            try
            {
                var renderer=root.AddComponent<SpriteRenderer>();renderer.sprite=atlas.Frames("idle")[0];renderer.sortingOrder=20;
                var material=WorldArtworkMaterial.Create(atlas.Texture);AssetDatabase.CreateAsset(material,"Assets/Remains/Prefabs/Actors/radroach.mat");renderer.sharedMaterial=material;
                var visual=root.AddComponent<AtlasActorVisual>();visual.SetRenderer(renderer);visual.ConfigureDeadPresentation(true,0,new Color(.55f,.55f,.55f,1));
                type.SetPrefab(PrefabUtility.SaveAsPrefabAsset(root,"Assets/Remains/Prefabs/Actors/radroach.prefab").GetComponent<AtlasActorVisual>());
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
            AssetDatabase.CreateAsset(type,ActorPath);var catalog=EntityAssetCatalog.Load();catalog.AddActor(type);EditorUtility.SetDirty(catalog);
            definitions.Add(JObject.Parse(Remains.Serialization.StateJson.ToJson(data.anatomy)));
            File.WriteAllText(anatomyPath,anatomyJson.ToString(Newtonsoft.Json.Formatting.None)+"\n");
            AssetDatabase.ImportAsset("Assets/Remains/Resources/Content/anatomy.json",ImportAssetOptions.ForceUpdate);
            var loot=ScriptableObject.CreateInstance<ContainerLootAsset>();loot.Configure(new ContainerLootTable {id="base:factory-radroach",version="1",
                entries=new[]{new ContainerLootEntry {id="currency",definition="caps",minimum=1,maximum=2}}});AssetDatabase.CreateAsset(loot,LootPath);
            var spawn=ScriptableObject.CreateInstance<NpcSpawnAsset>();spawn.Configure("radroach","radroach",data.health,null);spawn.SetInventory(loot);AssetDatabase.CreateAsset(spawn,SpawnPath);
            string[] additions;
            var workshop=PrefabUtility.LoadPrefabContents(FactoryWorkshopDraft.Path);
            try
            {
                var report=workshop.GetComponent<RoomImportAudit>().Report;
                var records=report.Records.Where(r=>r.kind=="enl1").ToArray();
                if(records.Length!=2 || records.Any(r=>r.installed) ||
                    !records.Select(r=>r.code).OrderBy(v=>v).SequenceEqual(new[]{"QyxFODdq6EPvuE6L","flGB6kWfuOYOeN8G"}.OrderBy(v=>v)))
                    throw new InvalidOperationException("Не найдены две сохранённые исходные категории crawler цеха.");
                additions=records.Select(r=>"original:plant:18:"+r.code).ToArray();
                foreach(var record in records)
                {
                    var marker=new GameObject("Радтаракан · "+record.code);marker.transform.SetParent(workshop.transform,false);
                    marker.transform.localPosition=FactorySourceCoordinates.Placement(record.sourceX,record.sourceY,1);
                    var placement=marker.AddComponent<RoomActorPlacement>();placement.Configure("original:plant:18:"+record.code,"radroach",-1,false,null);placement.SetSpawn(spawn);
                    record.installed=true;record.reason="Категория мелкого ползучего; явно выбран радтаракан Unity с естественной атакой и единым инвентарём тела. См. CRAWLER_SPEC.md; исходный runtime не проверен.";
                }
                var content=workshop.GetComponent<RoomNpcContent>();content.Configure("base:factory-workshop-18:npcs-v2");content.ConfigureExpansion("base:factory-workshop-18:npcs-v1",additions);
                EditorUtility.SetDirty(report);PrefabUtility.SaveAsPrefabAsset(workshop,FactoryWorkshopDraft.Path);
            }
            finally {PrefabUtility.UnloadPrefabContents(workshop);}
            var connected=PrefabUtility.LoadPrefabContents(ConnectedFactoryAuthoring.Path);
            try
            {
                var content=connected.GetComponent<RoomNpcContent>();
                var earlier=RoomNpcContent.Read(connected.GetComponent<RoomAuthoring>(),"factory:0").expansion;
                if(earlier?.previousPatch!="factory:0:"+FactoryWorkshopNpcAuthoring.PreviousPatch || earlier.additions.Length!=5)
                    throw new InvalidOperationException("Изменилась ожидаемая предыдущая версия NPC; дополнение требует явного разбора.");
                content.Configure(Patch);content.ConfigureExpansion(new ContentExpansion {previousPatch=FactoryWorkshopNpcAuthoring.Patch,additions=additions,
                    earlierVersions=new[]{new ContentExpansionStep {previousPatch=FactoryWorkshopNpcAuthoring.PreviousPatch,
                        additions=earlier.additions.Select(id=>id.Substring("factory:0:".Length)).Concat(additions).ToArray()}}});
                PrefabUtility.SaveAsPrefabAsset(connected,ConnectedFactoryAuthoring.Path);
            }
            finally {PrefabUtility.UnloadPrefabContents(connected);}
            AssetDatabase.SaveAssets();FactoryCrawlerChecks.Run();RoomAuthoringScenes.ValidateSavedOverrides();
            Debug.Log("Радтараканы цеха: сохранены тип/префаб/атлас и две исходные расстановки; остальные пять записей не установлены.");
        }
        private static void Preflight()
        {
            var workshop=AssetDatabase.LoadAssetAtPath<GameObject>(FactoryWorkshopDraft.Path);
            var connected=AssetDatabase.LoadAssetAtPath<GameObject>(ConnectedFactoryAuthoring.Path);
            if(workshop==null || connected==null || workshop.GetComponent<RoomNpcContent>()==null || connected.GetComponent<RoomNpcContent>()==null)
                throw new InvalidOperationException("Не найдены сохранённые модули и NPC-контент цеха.");
            var records=workshop.GetComponent<RoomImportAudit>().Report.Records;
            if(records.Count(r=>!r.installed)!=7 || records.Count(r=>r.kind=="enl1" && !r.installed)!=2)
                throw new InvalidOperationException("Изменился исходный audit цеха; нужна явная проверка добавления.");
            var layout=RoomNpcContent.Read(connected.GetComponent<RoomAuthoring>(),"factory:0");
            if(layout.patch!="factory:0:"+FactoryWorkshopNpcAuthoring.Patch || layout.actors.Length!=6 ||
                layout.expansion?.previousPatch!="factory:0:"+FactoryWorkshopNpcAuthoring.PreviousPatch || layout.expansion.additions.Length!=5)
                throw new InvalidOperationException("Изменилась ожидаемая версия расстановки NPC; сохранённые ассеты не заменены.");
        }
    }
}
