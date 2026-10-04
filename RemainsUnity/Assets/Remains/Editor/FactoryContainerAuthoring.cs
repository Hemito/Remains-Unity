using System;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryContainerAuthoring
    {
        private const string Folder="Assets/Remains/RoomDrafts/Containers";
        private const string Draft="Assets/Remains/RoomDrafts/FactoryEntry46.prefab";
        public static void VerifyDraftLoot()
        {
            var room=AssetDatabase.LoadAssetAtPath<GameObject>(Draft).GetComponent<RoomAuthoring>();
            var layout=RoomPhysicalContent.Read(room,"factory:0");
            if(layout.loot.Length!=6 || layout.loot.Any(b=>b.table.entries.Any(e=>e.weaponState!=null)))
                throw new InvalidOperationException("Сохранённые таблицы потеряли привязки или получили ложное состояние оружия.");
            var world=new Remains.Domain.WorldState {worldId="factory-loot-authoring-check"};
            PhysicalContentInstaller.Apply(world,layout);
            int count=world.containers.Sum(c=>c.items.Count);
            if(count==0 || world.containers.Any(c=>c.items.Any(i=>i.weaponState!=null)))
                throw new InvalidOperationException("Сохранённые таблицы не создают обычные предметы.");
            Debug.Log($"Сохранённый лут завода: PASS — таблиц {layout.loot.Length}, контейнеров {world.containers.Count}, стопок {count}.");
        }
        public static void InstallDraftLoot()
        {
            if(AssetDatabase.LoadAssetAtPath<ContainerLootAsset>(Folder+"/case-loot.asset")!=null)
                throw new InvalidOperationException("Таблицы уже созданы; авторские правки не заменены.");
            CompleteDraftLoot();
        }
        public static void CompleteDraftLoot()
        {
            ContainerLootAsset Table(string id,params ContainerLootEntry[] entries)
            {
                string path=Folder+"/"+id+"-loot.asset";
                var asset=AssetDatabase.LoadAssetAtPath<ContainerLootAsset>(path);
                bool create=asset==null;
                if(create) asset=ScriptableObject.CreateInstance<ContainerLootAsset>();
                asset.Configure(new ContainerLootTable {id="base:draft:"+id,version="1",entries=entries});
                if(create) AssetDatabase.CreateAsset(asset,path);else EditorUtility.SetDirty(asset);
                return asset;
            }
            var money=Table("case",new ContainerLootEntry {id="caps",minimum=0,maximum=30,definition="caps"});
            var ammo=Table("ammobox",
                new ContainerLootEntry {id="p10",minimum=0,maximum=24,definition="p10"},
                new ContainerLootEntry {id="batt",minimum=0,maximum=12,definition="batt"});
            var root=PrefabUtility.LoadPrefabContents(Draft);
            try
            {
                foreach(var marker in root.GetComponentsInChildren<RoomContainerPlacement>(true))
                {
                    string type=marker.Definition.CreateDefinition().id;
                    if(type=="case") marker.SetLoot(money);
                    else if(type=="ammobox") marker.SetLoot(ammo);
                }
                var layout=RoomPhysicalContent.Read(root.GetComponent<RoomAuthoring>(),"factory:0");
                if(layout.loot.Length!=6) throw new InvalidOperationException("Не назначены шесть таблиц контейнеров.");
                var world=new Remains.Domain.WorldState {worldId="factory-loot-authoring-check"};PhysicalContentInstaller.Apply(world,layout);
                if(world.containers.Count!=8 || !world.containers.Any(c=>c.items.Count>0)) throw new InvalidOperationException("Не создано тестовое содержимое контейнеров.");
                PrefabUtility.SaveAsPrefabAsset(root,Draft);AssetDatabase.SaveAssets();
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            Debug.LogWarning("Черновые таблицы завода: количества заданы для текущего набора предметов; вероятности оригинала и полный лут сундуков ещё не воспроизведены.");
            ContractChecks.Run();
        }
        public static void Import()
        {
            if(AssetDatabase.IsValidFolder(Folder)) throw new InvalidOperationException("Контейнеры уже импортированы; авторские правки не заменены.");
            Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
            foreach(string file in Directory.GetFiles("Assets/Remains/Resources/Original/Containers","*.png"))
            {
                var importer=(TextureImporter)AssetImporter.GetAtPath(file);
                importer.textureType=TextureImporterType.Default;importer.maxTextureSize=8192;importer.npotScale=TextureImporterNPOTScale.None;
                importer.textureCompression=TextureImporterCompression.Uncompressed;importer.mipmapEnabled=false;
                importer.alphaIsTransparency=true;importer.wrapMode=TextureWrapMode.Clamp;importer.SaveAndReimport();
            }
            var small=Create("case","Чемодан",3.24f,new Rect(8,0,144,120));
            var chest=Create("chest","Сундук",19.2f,new Rect(0,0,240,160));
            var ammo=Create("ammobox","Ящик боеприпасов",5.2488f,new Rect(8,24,144,108));
            var root=PrefabUtility.LoadPrefabContents(Draft);
            try
            {
                if(root.GetComponentsInChildren<RoomContainerPlacement>(true).Length!=0) throw new InvalidOperationException("Контейнеры в комнате уже расставлены.");
                Place(root,small,"pdWAJCvXZU4feepc",45,7,1);Place(root,small,"s2zmiswvyOj8RFfv",5,19,1);
                Place(root,small,"tuTLAMpCxBrwU0l7",9,19,1);Place(root,small,"rxmS30NgGm1Skg49",7,18,1);
                Place(root,chest,"ff7k99hFJ7kOb3mq",7,19,2);Place(root,chest,"lHwuCdtbZTKvhIVm",17,2,2);
                Place(root,ammo,"YjmWpd6HlAIq0fMN",2,23,1);Place(root,ammo,"U3lP8pIbVID6okzv",45,3,1);
                var layout=RoomPhysicalContent.Read(root.GetComponent<RoomAuthoring>(),"factory:0");
                if(layout.containers.Length!=8 || layout.props.Length!=4 || layout.containers.Select(c=>c.id).Distinct().Count()!=8)
                    throw new InvalidOperationException("Потеряны контейнеры или физические ящики входа.");
                var world=new Remains.Domain.WorldState();PhysicalContentInstaller.Apply(world,layout);
                if(world.containers.Count!=8 || world.containers.Any(c=>c.items.Count!=0)) throw new InvalidOperationException("Черновые контейнеры должны иметь явно пустое содержимое.");
                PrefabUtility.SaveAsPrefabAsset(root,Draft);AssetDatabase.SaveAssets();
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            FactoryEntryActorsAuthoring.Verify();
        }
        private static PropAsset Create(string id,string title,float mass,Rect rect)
        {
            string resource="Original/Containers/"+id+"-closed";
            var texture=Resources.Load<Texture2D>(resource);
            if(texture==null || rect.xMax>texture.width || rect.yMax>texture.height) throw new InvalidOperationException("Некорректные границы изображения "+id);
            var sprite=Sprite.Create(texture,rect,new Vector2(.5f,.5f),200,0,SpriteMeshType.FullRect);
            AssetDatabase.CreateAsset(sprite,Folder+"/"+id+"-closed.asset");
            var material=WorldArtworkMaterial.Create(texture);AssetDatabase.CreateAsset(material,Folder+"/"+id+".mat");
            var asset=ScriptableObject.CreateInstance<PropAsset>();
            asset.SetDefinition(new PropDefinition {id=id,name=title,mass=mass,width=rect.width/200,height=rect.height/200,resource=resource});
            var root=new GameObject(title);
            try
            {
                var renderer=root.AddComponent<SpriteRenderer>();renderer.sprite=sprite;renderer.sharedMaterial=material;
                renderer.sortingOrder=ActorVisualLayers.WorldObjectOrder;
                root.AddComponent<PropView>().SetArtwork(renderer);
                asset.SetPrefab(PrefabUtility.SaveAsPrefabAsset(root,Folder+"/"+id+".prefab").GetComponent<PropView>());
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
            AssetDatabase.CreateAsset(asset,Folder+"/"+id+".asset");return asset;
        }
        private static void Place(GameObject root,PropAsset asset,string code,int x,int y,int cells)
        {
            var definition=asset.CreateDefinition();var marker=new GameObject(definition.name+" · "+code);
            marker.transform.SetParent(root.transform,false);marker.transform.localPosition=new Vector3((x+cells*.5f)*.8f,(24-y)*.8f,0);
            marker.AddComponent<RoomContainerPlacement>().Configure("original:plant:46:"+code,asset);
            var preview=(GameObject)PrefabUtility.InstantiatePrefab(asset.Prefab.gameObject,marker.transform);
            preview.transform.localPosition=new Vector3(0,definition.height*.5f,0);
        }
    }
}
