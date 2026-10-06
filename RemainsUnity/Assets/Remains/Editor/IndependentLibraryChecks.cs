using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remains.Domain;
using Remains.Persistence;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class IndependentLibraryChecks
    {
        public static void Run()=>RunAt(Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/independent-libraries",Guid.NewGuid().ToString("N"))));
        public static void RunAt(string root)
        {
            var libraries=new SaveLibraries(root,"Альфа");
            var alpha=new SaveStore(libraries.Characters.ProfilePath("Альфа"));
            var beta=new SaveStore(libraries.Characters.ProfilePath("Бета"));
            var original=new WorldState {historyVersion=1,checkpointSequence=4,revision=8};
            var json=JObject.Parse(StateJson.ToJson(original));json["unknownWorldData"]=new JObject { ["large"]=decimal.MaxValue.ToString(),["nested"]=new JArray(1,"данные мода")};
            var first=new RecoverySnapshot(json.ToString());alpha.SaveRecovery(first);beta.SaveRecovery(first);
            var branch=first.CreateLiveWorld();WorldHistory.BeginContinuation(branch);branch.revision=9;
            var divergent=new RecoverySnapshot(StateJson.ToJson(branch));beta.SaveRecovery(divergent);
            var sibling=first.CreateLiveWorld();WorldHistory.BeginContinuation(sibling);sibling.revision=9;
            var siblingCopy=new RecoverySnapshot(StateJson.ToJson(sibling));alpha.SaveRecovery(siblingCopy);
            new WorldLibrary(alpha).Rename(first.WorldId,"Имя Альфы");new WorldLibrary(beta).Rename(first.WorldId,"Имя Беты");
            string oldAlpha=File.ReadAllText(Path.Combine(alpha.DirectoryPath,"world.json"));
            string oldBeta=File.ReadAllText(Path.Combine(beta.DirectoryPath,"world.json"));
            string corrupt=Path.Combine(alpha.DirectoryPath,"Worlds","damaged.json");File.WriteAllText(corrupt,"повреждённые данные");
            var selected=libraries.ForCharacter("Альфа");var other=libraries.ForCharacter("Бета");
            var catalog=selected.Catalog.Read();
            Require(selected.SelectedSnapshot().Id==siblingCopy.Id,"Перенос выбрал чужую ветвь вместо исходного выбора");
            Require(catalog.files.Any(entry=>entry.summary?.snapshot==first.Id) && catalog.files.Any(entry=>entry.summary?.snapshot==divergent.Id),"Перенос потерял точный снимок или независимое продолжение");
            Require(catalog.entries.Count==2 && catalog.entries[0].summary.CompareTo(catalog.entries[1].summary)==WorldRelation.Divergent &&
                Directory.EnumerateFiles(Path.Combine(libraries.WorldsDirectory,"Worlds"),"*.json",SearchOption.AllDirectories).Count()==3,"Перенос объединил разные ветви или размножил одинаковый снимок");
            Require(File.ReadAllText(Path.Combine(alpha.DirectoryPath,"world.json"))==oldAlpha && File.ReadAllText(Path.Combine(beta.DirectoryPath,"world.json"))==oldBeta,"Перенос изменил исходные миры");
            Require(Directory.EnumerateFiles(Path.Combine(libraries.WorldsDirectory,"LegacyMigration","Backups"),"damaged.json",SearchOption.AllDirectories).Any(path=>File.ReadAllText(path)=="повреждённые данные"),"Нет точной резервной копии повреждённого файла");
            Require(File.ReadAllText(Path.Combine(libraries.WorldsDirectory,"world-names.json")).Contains("Имя Беты"),"Различающиеся подписи не сохранены");
            var a=Profile("Альфа",71,"alpha-item",3);var b=Profile("Бета",86,"beta-item",5);
            selected.SaveCharacter(a);other.SaveCharacter(b);
            double clock=10;var timer=new CharacterPlayTime(()=>clock);timer.CaptureCheckpoint(selected.CharacterDirectoryPath).Persist();clock=25;timer.CaptureCheckpoint(selected.CharacterDirectoryPath).Persist();timer.Stop();
            string alphaSource=selected.ReadCharacterSource(),alphaTime=File.ReadAllText(Path.Combine(selected.CharacterDirectoryPath,"playtime.json"));
            string personalBackup=selected.PreserveCharacterSource();
            Require(File.ReadAllText(personalBackup)==alphaSource && personalBackup.StartsWith(selected.CharacterDirectoryPath,StringComparison.OrdinalIgnoreCase),"Резервная копия личного профиля записана в мир или потеряла данные");
            other.SelectContinuation(divergent);
            Require(selected.SelectedSnapshot().Id==divergent.Id && other.SelectedSnapshot().Id==divergent.Id,"Выбор мира всё ещё принадлежит персонажу");
            b.health=62;other.SaveCharacter(b);
            Require(selected.ReadCharacterSource()==alphaSource && File.ReadAllText(Path.Combine(selected.CharacterDirectoryPath,"playtime.json"))==alphaTime,"Чужой профиль изменил вещи/состояние/время персонажа");
            Require(selected.LoadCharacter().inventory.Single().id=="alpha-item" && other.LoadCharacter().inventory.Single().id=="beta-item","Личные инвентари смешаны");
            Require(other.Catalog.Read().entries.Select(entry=>entry.summary.snapshot).SequenceEqual(selected.Catalog.Read().entries.Select(entry=>entry.summary.snapshot)),"Список миров отличается между персонажами");
            Require(other.LoadWorld(false).worldId==first.WorldId,"Фактическая загрузка мира не использует общую библиотеку");
            var restarted=new SaveLibraries(root,"Бета");
            Require(restarted.ForCharacter("Бета").SelectedSnapshot().Id==divergent.Id,"Перезапуск или другой персонаж сбросил выбранный мир");
            new WorldLibrary(selected).Delete(first.WorldId);
            Require(new SaveLibraries(root,"Альфа").ForCharacter("Альфа").Continuations().Count==0,"Старые файлы воскресили удалённый импорт после перезапуска");
            Require(selected.ReadCharacterSource()==alphaSource,"Удаление мира изменило персонажа");
            var checkpoint=libraries.ForCharacter("Альфа");new WorldLibrary(checkpoint).PrepareNewGame();
            Require(checkpoint.ReadCharacterSource()==alphaSource,"Новый мир сбросил личный профиль");
            CheckEntry();
            Debug.Log("Независимые библиотеки: PASS — общий выбор/загрузка/список после смены профиля и перезапуска, отдельные вещи/время, точный перенос всех профилей с данными модов и подписями, резервирование ошибок, удаление без повторного импорта, вход из хаба и сохранённое тело.");
        }
        private static ActorState Profile(string name,float health,string item,int count)=>new()
        {id="player:"+name,character=name,player=true,level="hub",health=health,loadoutVersion=1,weaponInventoryVersion=1,
            inventory={new ItemStack {id=item,definition="heal",count=count}}};
        private static void CheckEntry()
        {
            var world=new WorldState();var old=Profile("Альфа",40,"same-item",2);old.x=7;old.y=3;old.generation=9;old.lifecycleSequence=11;
            var current=Profile("Альфа",83,"same-item",8);current.hair=4;
            current.appearance=new CharacterAppearance {version=1,hairStyle=2,eyeStyle=3};
            world.actors.Add(old);
            var entered=PlayerWorldEntry.Reconcile(world,old,current,out _);
            Require(entered.health==83 && entered.inventory.Single().count==8 && entered.appearance.hairStyle==2 && entered.x==7 && entered.generation==9 && entered.lifecycleSequence==11,"Хаб не использует личный профиль или потерял мировую позицию/эпохи");
            Require(old.health==40 && current.x!=7,"Согласование изменило исходный мир или профиль");
            var debit=new InventoryUse {id="deposit",debits={new ItemDebit {item="same-item",count=2}}};old.inventoryUses.Add(debit);
            world.pendingDeposits.Add(new PendingDeposit {transaction="deposit",source=old.id,item=new ItemStack {id="reserved",definition="heal",count=2}});
            entered=PlayerWorldEntry.Reconcile(world,old,current,out _);
            Require(entered.inventory.Single().count==6 && entered.inventoryUses.Count==1,"Не восстановлен ровно один подготовленный расход");
            var again=PlayerWorldEntry.Reconcile(world,old,entered,out _);
            Require(again.inventory.Single().count==6,"Повторный вход повторил расход");
            old.level="factory:0";old.health=12;old.inventory.Single().count=1;
            Require(ReferenceEquals(PlayerWorldEntry.Reconcile(world,old,current,out string notice),old) && notice!=null && old.health==12 && old.inventory.Single().count==1,"Вход переписал последствия оставленного тела");
        }
        private static void Require(bool condition,string message) {if(!condition) throw new InvalidOperationException(message);}
    }
}
