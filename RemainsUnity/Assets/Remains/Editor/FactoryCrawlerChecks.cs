using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryCrawlerChecks
    {
        public static void Run()
        {
            ActorTypes.Load();Anatomy.Load();WeaponCatalog.Load();
            var room=RoomCatalogAsset.Load().Resolve(NewGameWorld.FactoryDefinitionId);
            var layout=FactoryContentCheckFixtures.BeforeOffice(RoomNpcContent.Read(room,"factory:0"));var geometry=room.CreateLayout();
            var records=room.GetComponentsInChildren<RoomImportAudit>(true).Single(a=>a.Report.SourceId=="original:plant:18").Report.Records;
            var crawlers=layout.actors.Where(a=>a.actorType=="radroach").ToArray();
            Require(crawlers.Length==2 && layout.actors.Length==8 && layout.loot.Length==8 && layout.expansion.additions.Length==2 &&
                records.Length==24 && records.All(r=>r.installed || r.kind is "platform2" or "knop3" or "vendor" or "instr2") && records.Where(r=>r.kind=="enl1").All(r=>r.installed),"Потеряны две исходные метки или журнал оставшегося содержимого.");
            foreach(var actor in crawlers)
            {
                var record=records.Single(r=>actor.id.EndsWith(":"+r.code,StringComparison.Ordinal));
                Require(Vector2.Distance(actor.Position,FactorySourceCoordinates.Placement(record.sourceX,record.sourceY,1)+Vector2.right*38.4f)<.001f &&
                    actor.anatomy=="radroach" && string.IsNullOrEmpty(actor.weapon) && actor.owner==null && actor.health==8 &&
                    ActorTerrainMotion.IsClear(geometry,Anatomy.Movement(actor),actor.Position),"Неверны источник, вид, здоровье или геометрия: "+actor.id);
            }
            var type=ActorTypes.Get("radroach");Require(!type.hasWeapon && type.combatAi && type.bodyInventory && type.naturalAttack!=null,"Вид подменён оружейным/декоративным NPC.");
            var saved=Resources.Load<ActorTypeAsset>("Content/actors/radroach");Require(saved.TryGetPrefab(type,out var prefab) && prefab is Remains.Presentation.AtlasActorVisual,"Нет самостоятельного сохранённого префаба.");
            var atlas=Resources.Load<AtlasSpriteAsset>(type.artwork);Require(atlas.Texture.width==480 && atlas.Texture.height==30,"Не выбран отдельный исходный атлас.");
            foreach(var clip in atlas.CreateDefinition().clips) Require(atlas.Frames(clip.pose).Length==clip.count,"Не сохранены кадры: "+clip.pose);
            CheckUpgrade(layout);NaturalAttackChecks.Run();
            Debug.Log($"Радтараканы цеха: PASS — два стабильных ID, самостоятельные ресурсы/анатомия/префаб, v1/v2/v3 без возвращения смерти/лута; не подключено записей: {records.Count(r=>!r.installed)}.");
        }
        public static void CheckUpgrade(NpcLayout layout)
        {
            foreach(string previous in new[]{"base:factory-connected-46-18:npcs-v1",FactoryWorkshopNpcAuthoring.Patch})
            {
                var world=new WorldState {worldId="crawler-upgrade-"+previous};world.contentPatches.Add(layout.level+":"+previous);
                var old=layout.actors.First(a=>a.actorType!="radroach").Copy();old.health=0;old.down=true;old.inventory.Clear();
                old.inventory.Add(new ItemStack {id="saved-old-loot",definition="caps",count=17});old.owner="saved-owner";old.x+=.3f;old.generation=7;
                old.extensions=new System.Collections.Generic.Dictionary<string,Newtonsoft.Json.Linq.JToken> {
                    {"modCreatureHistory",Newtonsoft.Json.Linq.JToken.Parse("{\"nested\":[17,\"сохранённое модовое поле\"]}")}};
                world.actors.Add(old);string retained=StateJson.ToJson(old);
                // Missing actors from the installed previous version must stay missing.
                Require(NpcContentInstaller.Apply(world,layout),"Не применено добавочное обновление "+previous);
                Require(StateJson.ToJson(world.Find(old.id))==retained && world.actors.Count==(previous.EndsWith("v1",StringComparison.Ordinal)?8:3),"Возвращён удалённый NPC или изменено старое тело: "+previous);
                var body=world.actors.First(a=>a.actorType=="radroach");body.health=0;body.down=true;body.inventory.Clear();world.actors.Remove(world.actors.Last());
                world=StateJson.Copy(world);string before=StateJson.ToJson(world);
                Require(!NpcContentInstaller.Apply(world,layout) && StateJson.ToJson(world)==before,"v3 пополнил лут или вернул тело.");
            }
            var fresh=new WorldState {worldId="crawler-new-world"};NpcContentInstaller.Apply(fresh,layout);
            Require(fresh.actors.Count==8 && fresh.actors.Count(a=>a.actorType=="radroach" && a.inventory.Count==1 && a.inventory[0].definition=="caps" && a.inventory[0].count is >=1 and <=2)==2,"Новый мир не получил единый инвентарь двух существ.");
            var bodyToClaim=fresh.actors.First(a=>a.actorType=="radroach");bodyToClaim.down=true;bodyToClaim.health=0;
            var player=new ActorState {id="crawler-loot-recipient",player=true,connected=true,level=layout.level,Position=bodyToClaim.Position};fresh.actors.Add(player);
            var item=bodyToClaim.inventory.Single();int expectedCount=item.count;var shared=new SharedInventory(fresh);string source=InventorySource.BodyId(bodyToClaim.id);
            Require(ReferenceEquals(InventorySource.Resolve(fresh,source).items,bodyToClaim.inventory) && shared.PrepareClaim("crawler-claim",source,item.id,player) &&
                player.inventory.Count==0 && !shared.PrepareClaim("crawler-second-claim",source,item.id,player),"Тело скопировано или неподтверждённый лут доступен дважды.");
            fresh=StateJson.Copy(fresh);shared=new SharedInventory(fresh);var grant=fresh.pendingGrants.Single();shared.CommitGrant(grant);shared.CommitGrant(grant);
            Require(fresh.Find(player.id).inventory.Single().count==expectedCount && fresh.Find(bodyToClaim.id).inventory.Count==0,"Подтверждение лута потеряло/удвоило крышки.");
        }
        private static void Require(bool valid,string reason)
        {if(!valid) throw new InvalidOperationException("Радтаракан: "+reason);}
    }
}
