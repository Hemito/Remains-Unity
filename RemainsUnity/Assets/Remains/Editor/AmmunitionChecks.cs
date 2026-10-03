using System;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Persistence;
using UnityEngine;

namespace Remains.Editor
{
    public static class AmmunitionChecks
    {
        public static void Run()
        {
            var actor=new ActorState {id="ammo-check",player=true};
            WeaponAmmunition.Initialize(actor,true);
            var definition=WeaponCatalog.Get("energy");
            var authoritative=actor.Copy();
            for(int i=0;i<15;i++) Require(WeaponAmmunition.SpendShot(actor,definition,out _),"Лазерный магазин даёт 15 выстрелов");
            Require(!WeaponAmmunition.SpendShot(actor,definition,out _) && actor.mana==100,"Пустой магазин не стреляет и не тратит ману");
            Require(WeaponAmmunition.CompleteReload(actor,definition),"Перезарядка из личного инвентаря");
            Require(WeaponAmmunition.Magazine(actor,"energy").rounds==15 && InventoryJournal.Count(actor,"batt")==30,"Магазин пополнен из резерва без создания зарядов");
            authoritative.inventory.Add(SharedInventory.Create("caps",20));
            Require(InventoryJournal.Merge(authoritative,actor,out var inventory,out var uses),"Расход согласуется с параллельной выдачей");
            authoritative.inventory=inventory;authoritative.inventoryUses=uses;
            Require(InventoryJournal.Count(authoritative,"batt")==30 && InventoryJournal.Count(authoritative,"caps")==20,"Подбор не теряется при расходе");
            Require(InventoryJournal.Merge(authoritative,actor,out inventory,out uses),"Повтор расхода допускается идемпотентно");
            authoritative.inventory=inventory;authoritative.inventoryUses=uses;
            Require(InventoryJournal.Count(authoritative,"batt")==30 && uses.Count==1,"Повтор пакета не списывает батареи дважды");
            int before=actor.inventory.Count;
            Require(!InventoryJournal.Consume(actor,"batt",999) && actor.inventory.Count==before,"Невозможный расход не изменяет инвентарь");
            var partial=actor.Copy();
            WeaponAmmunition.Magazine(partial,"energy").rounds=3;
            InventoryJournal.Consume(partial,"batt",28);
            Require(WeaponAmmunition.CompleteReload(partial,definition) && WeaponAmmunition.Magazine(partial,"energy").rounds==5 &&
                InventoryJournal.Count(partial,"batt")==0,"Недостаточный резерв даёт частичный магазин, сохраняя заряженный остаток");

            var directory=Path.Combine(Path.GetTempPath(),"Remains-ammunition-"+Guid.NewGuid().ToString("N"));
            var saves=new SaveStore(directory);saves.SaveCharacter(actor);
            var restored=saves.LoadCharacter();
            Require(restored.weaponInventoryVersion==1 && restored.inventory.Single(item=>item.definition=="energy").weaponState.rounds==15 && InventoryJournal.Count(restored,"batt")==30 && restored.inventoryUses.Count==1,"Магазины, резерв и журнал переживают загрузку");
            int existingBackups=Directory.GetFiles(Path.Combine(directory,"Recovery")).Length;
            var legacy=new ActorState {id="legacy",player=true,health=16,down=true};
            saves.SaveCharacter(legacy);
            string source=File.ReadAllText(Path.Combine(directory,"character.json"));
            restored=saves.LoadCharacter();
            Require(restored.health==16 && restored.down && Directory.GetFiles(Path.Combine(directory,"Recovery")).Length==existingBackups+1,"Миграция сохраняет последствия и исходную копию");
            Require(Directory.GetFiles(Path.Combine(directory,"Recovery")).Any(path=>File.ReadAllText(path)==source),"Резервная копия миграции точна");
            saves.SaveCharacter(restored);restored=saves.LoadCharacter();
            Require(InventoryJournal.Count(restored,"batt")==45 && restored.inventory.Count(item=>item.weaponState!=null)==4 && Directory.GetFiles(Path.Combine(directory,"Recovery")).Length==existingBackups+1,"Миграция не выдаёт боезапас повторно");
            Debug.Log("Боезапас: PASS — магазин, резерв, отсутствие расхода маны, журнал, сохранение и однократная миграция.");
        }
        private static void Require(bool condition,string detail)
        {if(!condition) throw new InvalidOperationException("Проверка боезапаса: "+detail);}
    }
}
