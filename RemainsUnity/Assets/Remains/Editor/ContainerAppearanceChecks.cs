using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Persistence;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class ContainerAppearanceChecks
    {
        public static void Run()
        {
            int conditions=RunDomain();ContainerArtworkAuthoring.Check();RunNative();
            Debug.Log($"Вид контейнеров: PASS — {conditions} доменных условий, первый общий просмотр, миграция/backup/unknowns, дубликаты/отказы, сохранённый opened, 26 профилей и кэш свечения.");
        }
        public static int RunDomain()
        {
            int count=0;
            void Require(bool value,string reason) {count++;if(!value) throw new InvalidOperationException("Вид контейнера: "+reason);}
            const string worldId="ddd08088b3934e698bcfc4e96ae15751";
            var world=new WorldState {worldId=worldId};
            var actor=new ActorState {id="viewer",owner="connection:0",player=true,connected=true,level="factory:0",health=100};
            world.actors.Add(actor);
            var old=StateJson.FromJson<ContainerState>("{\"id\":\"source\",\"definition\":\"case\",\"level\":\"factory:0\",\"x\":0,\"y\":0,\"held\":true,\"owner\":\"connection:1\",\"generation\":4,\"sequence\":8,\"items\":[{\"id\":\"loot\",\"definition\":\"caps\",\"count\":3}],\"modOuter\":{\"number\":17}}");
            world.containers.Add(old);
            Require(old.appearance.version==0 && !old.appearance.opened,"Отсутствующее legacy поле не стало explicit0/closed.");
            string contents=StateJson.ToJson(old.items);string aliases=StateJson.ToJson(old.itemAliases);int backup=0;
            old.appearance.extensions=new System.Collections.Generic.Dictionary<string,Newtonsoft.Json.Linq.JToken>
                {{"modAppearance",Newtonsoft.Json.Linq.JToken.Parse("{\"state\":\"custom\",\"number\":23}")}};
            string nested=StateJson.ToJson(old.appearance.extensions);
            var invalid=new ContainerState {id="invalid",appearance=new ContainerAppearanceState {version=2}};world.containers.Add(invalid);
            string before=StateJson.ToJson(world);bool rejected=false;
            try {ContainerAppearancePersistence.Prepare(world,()=>backup++);} catch(InvalidOperationException) {rejected=true;}
            Require(rejected && backup==0 && StateJson.ToJson(world)==before,"Late invalid container разрешил частичную миграцию/backup.");
            world.containers.Remove(invalid);before=StateJson.ToJson(world);
            rejected=false;try {ContainerAppearancePersistence.Prepare(world,()=>throw new InvalidOperationException("backup unavailable"));}
            catch(InvalidOperationException) {rejected=true;}
            Require(rejected && StateJson.ToJson(world)==before,"Ошибка backup изменила мир.");
            Require(ContainerAppearancePersistence.Prepare(world,()=>{Require(StateJson.ToJson(world)==before,"Backup вызван после мутации.");backup++;}),"Legacy не мигрировал.");
            Require(backup==1 && old.appearance.version==1 && !old.appearance.opened && old.appearance.revision==0,"Миграция открыла/повторно отметила старый источник.");
            Require(old.held && old.owner=="connection:1" && old.generation==4 && old.sequence==8 && contents==StateJson.ToJson(old.items) &&
                aliases==StateJson.ToJson(old.itemAliases) && nested==StateJson.ToJson(old.appearance.extensions) && old.extensions["modOuter"]["number"].Value<int>()==17,
                "Миграция изменила тело/loot/aliases/unknown fields.");
            Require(!ContainerAppearancePersistence.Prepare(world,()=>backup++) && backup==1,"Повтор миграции снова вызвал backup.");
            var request=new ContainerInspectionContext(world,actor,old);
            ContainerInspectChange Apply(ContainerInspectionContext context=null,bool paused=false)=>
                ContainerInspectionRules.Apply(world,actor,context??request,paused,new NearbyLootRule(),new NearbyDepositRule(),out _);
            void Denied(Action alter,Action undo,string reason)
            {alter();string unchanged=StateJson.ToJson(world);Require(Apply()==ContainerInspectChange.Rejected && unchanged==StateJson.ToJson(world),reason);undo();}
            Denied(()=>actor.generation++,()=>actor.generation--,"Старое поколение открыло источник.");
            Denied(()=>actor.lifecycleSequence++,()=>actor.lifecycleSequence--,"Старый lifecycle открыл источник.");
            Denied(()=>actor.owner="other",()=>actor.owner="connection:0","Другой owner открыл источник.");
            Denied(()=>actor.level="hub",()=>actor.level="factory:0","Переход уровня открыл источник.");
            Denied(()=>actor.connected=false,()=>actor.connected=true,"Отключённый зритель открыл источник.");
            Denied(()=>actor.down=true,()=>actor.down=false,"Недееспособный зритель открыл источник.");
            Denied(()=>actor.x=10,()=>actor.x=0,"Просмотр вне радиуса открыл источник.");
            Denied(()=>world.worldId="other",()=>world.worldId=worldId,"Другой мир открыл источник.");
            Denied(()=>old.definition="chest",()=>old.definition="case","Повторно занятый source/type открыл другой тип.");
            Denied(()=>old.presentationItem="caps",()=>old.presentationItem=null,"Свободная вещь получила флаг настоящего контейнера.");
            Require(Apply(paused:true)==ContainerInspectChange.Rejected && !old.appearance.opened,"Пауза открыла источник.");
            var bodyRequest=StateJson.Copy(request);bodyRequest.source=InventorySource.BodyId(actor.id);
            Require(Apply(bodyRequest)==ContainerInspectChange.Rejected,"Инвентарь тела получил container artwork.");
            var inventory=new SharedInventory(world);long revision=world.revision;
            Require(inventory.PrepareClaim("reserve","source","loot",actor,out _,1) && !old.appearance.opened,"Первый claim открыл рисунок без просмотра.");
            inventory.CommitGrant(world.pendingGrants.Single());Require(!old.appearance.opened,"Commit claim открыл рисунок без просмотра.");
            contents=StateJson.ToJson(old.items);aliases=StateJson.ToJson(old.itemAliases);revision=world.revision;
            Require(Apply()==ContainerInspectChange.Changed && old.appearance.opened && old.appearance.revision==1 && world.revision==revision+1,"Первый просмотр не открыл canonical flag один раз.");
            Require(contents==StateJson.ToJson(old.items) && aliases==StateJson.ToJson(old.itemAliases) && world.pendingGrants.Count==0,"Просмотр удалил/резервировал содержимое.");
            before=StateJson.ToJson(world);Require(Apply()==ContainerInspectChange.Unchanged && StateJson.ToJson(world)==before,"Дубликат изменил мир/флаг.");
            Denied(()=>actor.generation++,()=>actor.generation--,"Открытый флаг разрешил ответ старому поколению.");
            Require(Apply(paused:true)==ContainerInspectChange.Rejected,"Открытый флаг разрешил новый просмотр на паузе.");
            var incoming=StateJson.Copy(old);incoming.appearance.opened=false;incoming.appearance.revision=0;
            PropReplication.PreserveNewerPose(old,incoming,old.owner);
            Require(!incoming.appearance.opened && incoming.appearance.revision==0,"Локальная поза перезаписала canonical artwork.");
            var second=StateJson.Copy(actor);second.id="viewer-2";second.owner="connection:1";world.actors.Add(second);
            before=StateJson.ToJson(world);
            Require(ContainerInspectionRules.Apply(world,second,new ContainerInspectionContext(world,second,old),false,new NearbyLootRule(),new NearbyDepositRule(),out _)==ContainerInspectChange.Unchanged && before==StateJson.ToJson(world),"Одновременный второй зритель повторно меняет opened.");
            var deposits=new SharedDeposits(world);
            var item=actor.inventory.Single();
            Require(deposits.Prepare("deposit",actor,old.id,item.id,1,out _) && deposits.Commit("deposit") && old.appearance.opened && old.appearance.revision==1,"Внесение закрыло рисунок.");
            var copy=StateJson.Copy(world);var restored=copy.containers.Single();
            Require(StateJson.ToJson(restored)==StateJson.ToJson(old) && restored.appearance.opened && restored.appearance.revision==1,"Reload JSON потерял opened/unknown/body/items.");
            // A recovery snapshot is independent evidence, captured BEFORE a reload/continuation.
            var snapshot=new RecoverySnapshot(StateJson.ToJson(world));var live=snapshot.CreateLiveWorld();
            Require(StateJson.ToJson(live.containers.Single())==StateJson.ToJson(old),"Recovery copy потеряла opened.");
            before=StateJson.ToJson(old);world.containers.Remove(old);world.containers.Add(new ContainerState {id="empty",definition="case",level=actor.level});
            ContainerAppearancePersistence.Prepare(world,()=>{});var empty=world.containers.Single();
            Require(!empty.appearance.opened && empty.items.Count==0,"Empty count сам открыл новый источник.");
            Require(ContainerInspectionRules.Apply(world,actor,new ContainerInspectionContext(world,actor,empty),false,new NearbyLootRule(),new NearbyDepositRule(),out _)==ContainerInspectChange.Changed && empty.items.Count==0,"Просмотр empty source не отметил открытие/создал вещи.");
            return count;
        }
        private static void RunNative()
        {
            PropCatalog.Load();var artwork=new PropArtwork();int pairs=0,statics=0;
            foreach(var definition in PropCatalog.All)
            {
                var state=new ContainerState {id="art-check:"+definition.id,definition=definition.id,width=definition.width,height=definition.height,
                    mass=definition.mass,movable=definition.movable,hasPhysicalBody=definition.hasPhysicalBody,anchorHeight=0,x=3,y=4,
                    appearance=new ContainerAppearanceState {version=1}};
                var view=artwork.CreateContainerView(state);
                try
                {
                    view.DrawMotion(state,null,false,0);var profile=view.ContainerArtwork;var scale=view.transform.localScale;var position=view.transform.position;
                    var closed=view.Artwork.bounds;var glow=view.GetComponentInChildren<HeldPropGlow>(true);int cached=glow.PreparedFrameCount;
                    if(view.Artwork.sprite!=profile.Closed) throw new InvalidOperationException("Исходный кадр контейнера изменён: "+definition.id);
                    state.appearance.opened=true;state.appearance.revision=1;state.held=true;
                    view.ApplyContainerAppearance(state);view.DrawMotion(state,null,false,0);
                    if(view.Artwork.sprite!=profile.Select(true) || glow.CurrentFrame!=view.Artwork.sprite || !view.HeldGlowVisible ||
                        glow.PreparedFrameCount!=cached || view.transform.localScale!=scale || view.transform.position!=position ||
                        view.Order!=ActorVisualLayers.WorldObjectOrder)
                        throw new InvalidOperationException("Open кадр/halo изменил anchor, scale, cache или held layer: "+definition.id);
                    view.ApplyContainerAppearance(state);state.held=false;view.DrawMotion(state,null,false,0);
                    if(view.HeldGlowVisible || view.Order!=ActorVisualLayers.RestingObjectOrder || glow.PreparedFrameCount!=cached)
                        throw new InvalidOperationException("Повтор/отпускание пересоздали halo или закрыли слой: "+definition.id);
                    if(profile.Mode==ContainerArtworkMode.Static)
                    {statics++;if(view.Artwork.bounds!=closed || cached!=1) throw new InvalidOperationException("Статический тип получил выдуманный кадр: "+definition.id);}
                    else {pairs++;if(cached!=2) throw new InvalidOperationException("Не подготовлены оба halo кадра: "+definition.id);}
                }
                finally {UnityEngine.Object.DestroyImmediate(view.gameObject);}
            }
            var factoryLoot=GameAssets.Load<FactoryLootCatalog>("Content/FactoryLoot");
            int newStatics=factoryLoot==null?0:factoryLoot.definitions.Where(entry=>entry.physical!=null &&
                UnityEditor.AssetDatabase.GetAssetPath(entry.physical).StartsWith("Assets/Remains/FactoryLoot/",StringComparison.Ordinal)).Count();
            if(pairs!=12 || statics!=14+newStatics) throw new InvalidOperationException($"Неверное покрытие каталога контейнеров: pair={pairs}, static={statics}, новых исходных статических типов={newStatics}.");
        }
    }
}
