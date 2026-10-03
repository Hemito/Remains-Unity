using System;
using Remains.Content;
using Remains.Domain;
namespace Remains.Editor
{
    public static class WeaponInventoryChecks
    {
        public static void Run()
        {
            var actor=new ActorState {id="instance-selection",weaponInventoryVersion=1};
            var a=new ItemStack {id="pistol-a",definition="pistol",count=1,weaponState=new WeaponItemState {rounds=7}};
            var b=new ItemStack {id="pistol-b",definition="pistol",count=1,weaponState=new WeaponItemState {rounds=2}};
            actor.inventory.Add(a);actor.inventory.Add(b);actor.inventory.Add(SharedInventory.Create("p10",20));
            var definition=WeaponCatalog.Get("pistol");
            Require(WeaponInventory.Select(actor,a.id,out _) && WeaponInventory.Assign(actor,"1",b.id,out _),"Выбор/назначение не выполнены");
            Require(WeaponAmmunition.SpendShot(actor,definition,out _) && a.weaponState.rounds==6 && b.weaponState.rounds==2 && a.weaponState.revision==1,
                "Выстрел потратил другой экземпляр");
            Require(WeaponInventory.SelectShortcut(actor,"1",out _) && actor.equippedWeaponItem==b.id,"Быстрый слот не сослался на экземпляр");
            int reserve=InventoryJournal.Count(actor,"p10");
            Require(WeaponAmmunition.CompleteReload(actor,definition) && b.weaponState.rounds==definition.magazineSize &&
                a.weaponState.rounds==6 && InventoryJournal.Count(actor,"p10")==reserve-(definition.magazineSize-2),"Перезарядка не сохранила баланс");
            actor.inventoryReservations[b.id]=1;
            int before=b.weaponState.rounds;long revision=b.weaponState.revision;
            Require(!WeaponAmmunition.SpendShot(actor,definition,out _) && !WeaponAmmunition.CompleteReload(actor,definition) &&
                !WeaponInventory.Select(actor,b.id,out _) && before==b.weaponState.rounds && revision==b.weaponState.revision,
                "Резервируемое оружие изменилось");
            actor.inventoryReservations.Clear();
            var copy=actor.Copy();
            Require(copy.equippedWeaponItem==b.id && copy.weaponShortcuts[0].item==b.id && WeaponAmmunition.Magazine(copy,"pistol").rounds==before,
                "Сохранение потеряло выбор, ссылку или магазин");
            actor.inventory.Remove(b);
            Require(!WeaponInventory.SelectShortcut(actor,"1",out _) && !WeaponAmmunition.SpendShot(actor,definition,out _),
                "Быстрый слот воссоздал отсутствующее оружие");
            Require(WeaponInventory.Select(actor,null,out _) && actor.weapon==null && WeaponInventory.Selected(actor)==null,
                "Пустые копыта заменились оружием по умолчанию");
            var grenades=SharedInventory.Create("grenade",2);actor.inventory.Add(grenades);
            Require(WeaponInventory.Select(actor,grenades.id,out _) && WeaponInventory.Assign(actor,"2",grenades.id,out _) &&
                WeaponInventory.Selected(actor)==grenades && grenades.weaponState==null,"Выбор гранаты превратил стопку в отдельное оружие");
            var saved=actor.Copy();
            Require(WeaponInventory.Selected(saved)?.count==2 && WeaponInventory.Shortcut(saved,"2")?.id==grenades.id,
                "Сохранение потеряло удерживаемую стопку");
            Require(SharedInventory.Consume(actor,"grenade",grenades.id) && WeaponInventory.Available(actor,"grenade"),"Остаток стопки перестал быть доступен");
            Require(SharedInventory.Consume(actor,"grenade",grenades.id) && WeaponInventory.Selected(actor)==null &&
                !WeaponInventory.SelectShortcut(actor,"2",out _),"Пустая стопка осталась действующим оружием");
            Require(actor.weapon==null && actor.equippedWeaponItem==null,"После последней гранаты осталось видимое оружие без предмета");
        }
        private static void Require(bool value,string message)
        {if(!value) throw new InvalidOperationException(message);}
    }
}
