using System;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryLootChecks
    {
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error){Debug.LogError("Проверка лута завода: "+error);EditorApplication.Exit(1);}
        }
        public static void Run()
        {
            Anatomy.Load();WeaponCatalog.Load();FactoryContainerLockChecks.Run();
            int count=0,amount=0;
            FactoryLootRules.Generate("case",4,1,50,()=>.5,request=>{count++;amount=request.count;return true;},message=>throw new Exception(message));
            Require(count==1&&amount==19,"исходная формула денег case");
            count=0;FactoryLootRules.Generate("ammo",4,1,50,()=>.5,request=>{count++;return true;},message=>throw new Exception(message));
            Require(count==1,"три независимые попытки ammo .7/.25/.15");
            var world=new WorldState {worldId="factory-loot-verification-v1",seed=104};
            var catalog=Resources.Load<FactoryCatalogAsset>(FactoryCatalogAsset.ResourceKey);
            FactoryPreparation.Create(world,catalog,0);
            FactoryContentSources.Prepare(world,"factory:0");
            var room=FactoryContentSources.Require(world,"factory:0");
            var layout=RoomPhysicalContent.Read(room,"factory:0");
            var root=room.GetComponent<FactoryLevelRoot>();
            CheckRegistration(root,layout);
            var placement=root.Objects.First(value=>value.Source.Type=="remains:obj:ammobox");
            var sourceParams=placement.Source.Params.ToArray();var sourceItems=placement.Source.Items.ToArray();
            try
            {
                placement.Source.Params["lock"]="0";placement.Source.Params["mine"]="3";placement.Source.Params["cont"]="empty";
                placement.Source.Items.Add(new System.Collections.Generic.SortedDictionary<string,string>{{"id","p10"},{"kol","2"},{"imp","1"}});
                var loot=Resources.Load<FactoryLootCatalog>("Content/FactoryLoot");
                var state=FactoryContainerLoot.Generate(loot,world,placement,loot.definitions.Single(value=>value.id=="ammobox"),0,
                    new System.Collections.Generic.Dictionary<string,int>(),new Remains.Presentation.InventoryArtwork());
                Require(state.lockLevel==0&&state.mineLevel==3&&state.items.Count==0,"явный lock0, последующая mine3 и contempty до вложенных предметов");
            }
            finally
            {
                placement.Source.Params.Clear();foreach(var pair in sourceParams)placement.Source.Params.Add(pair.Key,pair.Value);
                placement.Source.Items.Clear();placement.Source.Items.AddRange(sourceItems);
            }
            Require(layout.containers.Length>0&&layout.containers.Any(value=>value.items.Count>0),"настоящие контейнеры и лут seed104");
            Require(layout.containers.Any(value=>value.lockLevel>0),"исходные случайные замки");
            Require(RoomContentInstaller.Apply(world,new RoomContentBatch {physical=layout}),"первая атомарная публикация");
            var available=world.containers.First(value=>value.lockLevel==0&&value.mineLevel==0&&value.items.Count>0);
            var actor=new ActorState {id="loot-player",player=true,connected=true,weaponInventoryVersion=1,level=available.level,x=available.x,y=available.y};world.actors.Add(actor);
            string definition=available.items[0].definition;int before=available.items.Where(value=>value.definition==definition).Sum(value=>value.count);
            var inventory=new SharedInventory(world);
            Require(inventory.PrepareClaim("loot-proof",available.id,available.items[0].id,actor,out string reason,1),"резерв настоящего лута: "+reason);
            inventory.CommitGrant(world.pendingGrants.Single());
            Require(InventoryJournal.Count(actor,definition)==1&&available.items.Where(value=>value.definition==definition).Sum(value=>value.count)==before-1,"единственный подбор/уменьшение источника");
            Require(!inventory.PrepareClaim("loot-proof",available.id,available.items.FirstOrDefault()?.id,actor),"повтор операции не выдаёт дубль");
            string snapshot=StateJson.ToJson(world);
            string path="../.reference/factory-loot-world.json";File.WriteAllText(path,snapshot);world=StateJson.FromJson<WorldState>(File.ReadAllText(path));
            FactoryContentSources.Clear();FactoryContentSources.Prepare(world,"factory:0");
            var second=RoomPhysicalContent.Read(FactoryContentSources.Require(world,"factory:0"),"factory:0");
            Require(!RoomContentInstaller.Apply(world,new RoomContentBatch {physical=second}),"повтор посещения не переустанавливает содержимое");
            Require(StateJson.ToJson(world)==snapshot,"содержимое, замки, выдача и мировые лимиты после перезапуска");
            Debug.Log($"PASS лут завода: seed104, containers={world.containers.Count}, items={world.containers.Sum(value=>value.items.Count)}, locked={world.containers.Count(value=>value.lockLevel>0)}, mined={world.containers.Count(value=>value.mineLevel>0)}; резерв/выдача/дедупликация/сохранение/повторная подготовка. Это доменная интеграция; обычный игровой ввод проверяется отдельно.");
            FactoryContentSources.Clear();
        }
        public static void CheckRegistration(FactoryLevelRoot root,PhysicalLayout layout)
        {
            var placement=root.Objects.First(value=>value.Source.Type=="remains:obj:instr2" && layout.containers.Any(c=>c.id==value.Id));
            var state=layout.containers.Single(value=>value.id==placement.Id);
            var original=root.Catalog.Prefab(placement.Room.Cell.Template,placement.Room.Cell.Mirror)
                .GetComponentsInChildren<RoomSpriteAuthoring>(true).Single(value=>value.Kind==Remains.RoomFormat.RoomSpriteKind.Object && value.Index==placement.Index)
                .GetComponent<SpriteRenderer>();
            var roomInstance=root.Rooms.Cast<Transform>().Single(value=>Vector2.Distance(value.localPosition,root.Offset(placement.Room))<.0001f);
            var replaced=roomInstance.GetComponentsInChildren<RoomSpriteAuthoring>(true)
                .Single(value=>value.Kind==Remains.RoomFormat.RoomSpriteKind.Object && value.Index==placement.Index).GetComponent<SpriteRenderer>();
            Require(!replaced.enabled,"исходный instr2 должен быть скрыт после замены контейнерным видом: "+state.id);
            var asset=Resources.Load<FactoryLootCatalog>("Content/FactoryLoot").definitions.Single(value=>value.id=="instr2").physical;
            var view=UnityEngine.Object.Instantiate(asset.Prefab);
            try
            {
                view.DrawMotion(state,null,false,0);
                var expected=original.bounds;expected.center+=(Vector3)root.Offset(placement.Room);
                var actual=view.Artwork.bounds;
                Require(Vector3.Distance(expected.min,actual.min)<.0001f && Vector3.Distance(expected.max,actual.max)<.0001f,
                    $"регистрация instr2: id={state.id}, source={expected}, actual={actual}");
                var group=view.GetComponent<UnityEngine.Rendering.SortingGroup>()??view.gameObject.AddComponent<UnityEngine.Rendering.SortingGroup>();
                var outline=Remains.Presentation.WorldOutlineSource.ForRenderer(view.Artwork,true);
                InventorySource source=state;var rect=outline.Bounds(source.Bounds);
                Require(Vector2.Distance(rect.min,(Vector2)actual.min)<.0001f && Vector2.Distance(rect.max,(Vector2)actual.max)<.0001f,
                    "рамка instr2 совпадает с рисунком; область доступа остаётся отдельной");
                Require(Mathf.Abs(source.Bounds.yMin-rect.yMin-.24f)<.0001f,"исходный pivot instr2 не заменён нижним краем");
                Debug.Log($"PASS регистрация контейнера: id={state.id}, artwork={rect}, access={source.Bounds}; рамка рисунка отдельно от области доступа.");
            }
            finally {UnityEngine.Object.DestroyImmediate(view.gameObject);}
        }
        private static void Require(bool result,string message){if(!result)throw new InvalidOperationException("Не выполнено: "+message);}
    }
}
