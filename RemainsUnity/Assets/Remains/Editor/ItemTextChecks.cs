using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class ItemTextChecks
    {
        private static void Require(bool condition,string reason)
        {if(!condition) throw new InvalidOperationException("Тексты предметов: "+reason);}
        private static void Reject(Action action,string reason)
        {
            try {action();} catch(InvalidOperationException) {return;} catch(InvalidDataException) {return;}
            throw new InvalidOperationException("Тексты предметов: принят некорректный ввод — "+reason);
        }
        public static void Run()
        {
            var text=Resources.Load<TextAsset>(ItemTextCatalog.Resource);
            if(text==null) throw new InvalidOperationException("Нет данных проверки текстов предметов.");
            CheckText(text.text);ToolStandIconAuthoring.CheckAuthored();OfficeValuableIconAuthoring.CheckAuthored();
            var manifest=ToolStandIconAuthoring.ReadManifest();var catalog=ItemTextCatalog.Load();
            Require(manifest.items.Length==60 && manifest.items.All(entry=>catalog.Contains(entry.id)),"Не согласованы явные ID текстов и иконок стенда.");
            Require(OfficeValuableIconAuthoring.ReadManifest().items.Length==1 && catalog.Contains("lbook"),"Не согласованы текст и исходная иконка довоенной книги.");
            Debug.Log("Тексты и иконки предметов: PASS — 70 текстов, 60 связей иконок стенда и довоенная книга, явная регистрация модов, стопки/назначения/бросок/восстановление.");
        }
        // Pure checks: also executable without running Unity or importing assets.
        public static void CheckText(string json)
        {
            var warnings=new List<string>();var catalog=new ItemTextCatalog(json,warnings.Add);
            Require(catalog.Count==70,"Изменён заявленный набор нынешних предметов, обычных компонентов и довоенной книги.");
            var expected=new Dictionary<string,string> {{"spear","Копьё"},{"crowbar","Лом"},{"pistol","10-мм пистолет"},{"energy","Лазерный пистолет"},
                {"caps","Крышки"},{"heal","Аптечка"},{"grenade","Граната"},{"p10","10-мм патроны"},{"batt","Энергобатареи"},
                {"pin","Заколка"},{"rep","Ремонтный набор оружейника"},{"metal_comp","Металлическая пластина"},{"lbook","Довоенная книга"}};
            foreach(var pair in expected) Require(catalog.Name(pair.Key,"fixture")==pair.Value,"Изменено русское имя "+pair.Key);
            foreach(var entry in StateJson.FromJson<ItemTextManifest>(json).items)
            {
                Require(catalog.Name(entry.id,"fixture")==entry.name && catalog.Description(entry.id,"fixture")==entry.description,"Данные разных читателей разошлись: "+entry.id);
                if(!expected.ContainsKey(entry.id) || entry.id is "pin" or "rep" or "metal_comp" or "lbook") CheckOrdinaryItem(entry.id);
            }
            Require(warnings.Count==0,"Известный предмет вызывает предупреждение.");
            Reject(()=>new ItemTextCatalog("{\"schema\":2,\"items\":[]}"),"другая схема");
            Reject(()=>new ItemTextCatalog("{\"schema\":1,\"items\":[{\"id\":\"x\",\"name\":\"Х\",\"description\":\"Текст\"},{\"id\":\"x\",\"name\":\"Х\",\"description\":\"Текст\"}]}"),"повтор ID");
            Reject(()=>catalog.Register(new ItemTextDefinition {id="pin",name="Повтор",description="Текст"}),"неявная замена");
            Require(catalog.Name("pin","fixture")=="Заколка","Неудачная регистрация изменила действующий текст.");
            var mod=new ItemTextDefinition {id="mod:part",name="Деталь мода",description="Текст мода"};catalog.Register(mod);
            mod.name="Изменение внешнего DTO";Require(catalog.Name("mod:part","fixture")=="Деталь мода","Каталог хранит mutable внешний DTO.");
            catalog.Register(new ItemTextDefinition {id="mod:part",name="Новая деталь",description="Новый текст"},true);
            Require(catalog.Name("mod:part","fixture")=="Новая деталь" && catalog.Description("mod:part","fixture")=="Новый текст","Явная замена мода не согласована.");
            Reject(()=>catalog.Register(new ItemTextDefinition {id="bad",name="Имя",description=" "}),"пустое описание");
            Require(!catalog.Contains("bad"),"Отказ оставил неполную запись.");
            Require(catalog.Name("unknown","panel:actor-A")=="[unknown]","Неизвестный ID скрыт.");
            catalog.Name("unknown","panel:actor-A");catalog.Description("unknown","panel:actor-A");catalog.Description("unknown","panel:actor-A");
            Require(warnings.Count==2 && warnings.All(message=>message.Contains("unknown") && message.Contains("panel:actor-A")),"Нет контекста или предупреждение повторяется каждый кадр.");
            catalog.Name("unknown","other-panel");Require(warnings.Count==3,"Потерян контекст другого потребителя.");
        }
        private static void CheckOrdinaryItem(string definition)
        {
            var actor=new ActorState {id="source",character="source",level="factory:0",owner="client-A",player=true,connected=true,weaponInventoryVersion=1,loadoutVersion=1};
            var first=new ItemStack {id="first:"+definition,definition=definition,count=6};
            var absorbed=new ItemStack {id="absorbed:"+definition,definition=definition,count=3};
            var stack=InventoryStacks.Add(actor,first);InventoryStacks.Add(actor,absorbed);
            Require(actor.inventory.Count==1 && stack.count==9 && InventoryStacks.Find(actor,absorbed.id)==stack,"Не объединён компонент: "+definition);
            foreach(string slot in ItemShortcuts.Keys)
                Require(ItemShortcuts.Assign(actor,slot,absorbed.id,out _) && ItemShortcuts.Get(actor,slot)==stack,"Не назначен компонент: "+definition);
            Require(new StandardItemActions().Action(stack)==StandardItemActions.Hold && HeldItemSelection.Select(actor,absorbed.id,out _) &&
                stack.count==9 && HeldItemSelection.Selected(actor)==stack,"Выбор потратил предмет или добавил неподключённое действие: "+definition);
            var recipient=new ActorState {id="recipient",character="recipient",level=actor.level,player=true,connected=true,weaponInventoryVersion=1,loadoutVersion=1,x=.5f};
            var world=new WorldState();world.actors.Add(actor);world.actors.Add(recipient);
            Require(DroppedItemTransfer.Prepare(world,actor,"throw",absorbed.id,2,new Vector2(.2f,.7f),new Vector2(4,3),new NearbyDepositRule(),out _),"Не подготовлен обычный бросок: "+definition);
            Require(stack.count==7 && world.pendingDeposits.Single().item.definition==definition && world.containers.Single().presentationItem==definition &&
                world.containers.Single().items.Count==0,"Изменён тип/количество резерва броска: "+definition);
            string pending=StateJson.ToJson(world);
            Require(!DroppedItemTransfer.Prepare(world,actor,"throw",absorbed.id,2,new Vector2(.2f,.7f),new Vector2(4,3),new NearbyDepositRule(),out _) &&
                StateJson.ToJson(world)==pending,"Повтор броска изменил состояние: "+definition);
            var recovered=StateJson.Copy(world);var sender=recovered.Find(actor.id);var receiver=recovered.Find(recipient.id);
            Require(HeldItemSelection.Selected(sender)?.count==7 && ItemShortcuts.Get(sender,"=")?.count==7,"Восстановление потеряло ссылку удержания/назначения: "+definition);
            var deposits=new SharedDeposits(recovered);Require(deposits.Commit("throw") && !deposits.Commit("throw"),"Повторён commit броска: "+definition);
            var claims=new SharedInventory(recovered);var drop=recovered.containers.Single();
            Require(claims.PrepareClaim("pickup",drop.id,drop.items.Single().id,receiver),"Не подготовлен подбор компонента: "+definition);
            var grant=recovered.pendingGrants.Single();claims.CommitGrant(grant);claims.CommitGrant(grant);
            Require(sender.inventory.Single().count==7 && receiver.inventory.Single().count==2 && receiver.inventory.Single().definition==definition && drop.items.Count==0,
                "Подбор/дублирование нарушили единый остаток компонента: "+definition);
        }
    }
}
