using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Remains.Domain;
using Remains.Persistence;
using Remains.Serialization;
namespace Remains.Editor
{
    public static class WeaponInventoryMigrationChecks
    {
        public static void Run()
        {
            var old=new ActorState {id="body",character="migration-character",owner="owner",loadoutVersion=1,weapon="energy",health=37,mana=42};
            old.inventory.Add(SharedInventory.Create("heal",3));
            old.magazines.Add(new WeaponMagazine {weapon="pistol",rounds=4,revision=2,extensions=new Dictionary<string,JToken>{{"mod:paint","red"}}});
            old.magazines.Add(new WeaponMagazine {weapon="energy",rounds=8});
            old.magazines.Add(new WeaponMagazine {weapon="mod:saved-weapon",rounds=5});
            string before=StateJson.ToJson(old);
            var prepared=WeaponInventoryMigration.Prepare(old);
            Require(before==StateJson.ToJson(old),"Подготовка переписала исходник");
            Require(prepared.weaponInventoryVersion==1 && prepared.health==37 && prepared.mana==42 && prepared.magazines.Count==0 &&
                prepared.inventory.Count==6 && prepared.inventory[0].id==old.inventory[0].id && prepared.weaponShortcuts.Count==4,
                "Миграция потеряла предметы/статистику или добавила лишнее оснащение");
            var pistol=prepared.inventory.Single(item=>item.definition=="pistol");
            Require(pistol.weaponState.rounds==4 && pistol.weaponState.revision==2 && (string)pistol.weaponState.extensions["mod:paint"]=="red",
                "Потерян заряд или данные мода");
            Require(WeaponInventory.Selected(prepared).definition=="energy" && WeaponInventory.Selected(prepared).weaponState.rounds==8,
                "Потерян выбор оружия");
            Require(StateJson.ToJson(WeaponInventoryMigration.Prepare(prepared))==StateJson.ToJson(prepared),"Повторная миграция выдала оружие");
            var body=old.Copy();body.id="other-world-body";
            var convertedBody=WeaponInventoryMigration.Prepare(body);
            Require(convertedBody.equippedWeaponItem==prepared.equippedWeaponItem,"Копии персонажа получили разные ID оружия");
            var older=prepared.Copy();
            Require(WeaponInventory.Select(prepared,pistol.id,out _) && WeaponInventory.Assign(prepared,"4",pistol.id,out _),"Не изменён выбор");
            WeaponInventory.PreserveSelection(prepared,older);
            Require(older.equippedWeaponItem==pistol.id && older.weapon=="pistol" && older.weaponShortcuts.Single(s=>s.slot=="4").item==pistol.id,
                "Старый снимок откатил выбор или назначение");
            older.weaponShortcuts[0].item="changed-copy";
            Require(prepared.weaponShortcuts[0].item!="changed-copy","Списки назначений разделяют память");
            var afterTransfer=prepared.Copy();afterTransfer.inventory.RemoveAll(item=>item.id==pistol.id);
            WeaponInventory.PreserveSelection(prepared,afterTransfer);
            Require(afterTransfer.weapon==null && WeaponInventory.Selected(afterTransfer)==null,"После передачи осталось оружие в копытах");
            string directory=Path.Combine(Path.GetTempPath(),"Remains-weapon-migration-"+Guid.NewGuid().ToString("N"));
            var saves=new SaveStore(directory);
            old.player=true;
            var world=new WorldState();world.actors.Add(old);saves.SaveWorld(world);saves.SaveCharacter(old);
            string worldSource=File.ReadAllText(Path.Combine(directory,"world.json"));
            string characterSource=File.ReadAllText(Path.Combine(directory,"character.json"));
            var loaded=saves.LoadWorld();var character=saves.LoadCharacter();
            Require(Directory.GetFiles(Path.Combine(directory,"Recovery")).Any(path=>File.ReadAllText(path)==worldSource) &&
                Directory.GetFiles(Path.Combine(directory,"Recovery")).Any(path=>File.ReadAllText(path)==characterSource),"Нет точных исходников перед миграцией мира и персонажа");
            Require(loaded.actors[0].equippedWeaponItem==character.equippedWeaponItem && loaded.actors[0].weaponInventoryVersion==1,
                "Загрузка не подготовила тело или рассогласовала идентичность");
            saves.SaveWorld(loaded);saves.SaveCharacter(character);
            int backups=Directory.GetFiles(Path.Combine(directory,"Recovery")).Length;
            Require(saves.LoadWorld().actors[0].inventory.Count==character.inventory.Count && saves.LoadCharacter().inventory.Count==character.inventory.Count &&
                Directory.GetFiles(Path.Combine(directory,"Recovery")).Length==backups,"Повторная загрузка повторила миграцию");
        }
        private static void Require(bool value,string message)
        {if(!value) throw new InvalidOperationException(message);}
    }
}
