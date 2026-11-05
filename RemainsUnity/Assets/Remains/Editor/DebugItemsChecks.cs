using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    // The hidden item menu: the list holds each kind once, a weapon comes loaded, a stack as asked, an unlisted item is
    // refused; the word opens only spelled in order; the prefab's references hold; a new character's kit has no outfits.
    public static class DebugItemsChecks
    {
        public static void Run()
        {
            WeaponCatalog.Load();CosmeticCatalog.Load();
            var items=DebugItems.Build();
            Require(items.Select(i=>i.id).Distinct().Count()==items.Count,"в списке нет повторов");
            foreach(string category in DebugItems.Categories.Where(c=>c!=DebugItems.Other))
                Require(items.Exists(i=>i.category==category),"пустая категория "+category);
            Require(items.Exists(i=>i.id=="item:molotov" && i.category==DebugItems.Grenades) && items.Exists(i=>i.id=="grenade" && i.category==DebugItems.Grenades) &&
                !items.Exists(i=>i.id=="weapon:molotov:grade:0"),"метательное — одним предметом, как в добыче");
            Require(!items.Exists(i=>i.id=="punch" || i.id=="kick"),"удар телом — не предмет");

            var actor=new ActorState {id="p",player=true,connected=true,weaponInventoryVersion=1};
            var weapon=items.First(i=>i.category==DebugItems.Weapons && WeaponCatalog.Get(i.id).magazineSize>0);
            var given=DebugItems.Grant(actor,weapon.id,10);
            Require(given!=null && given.count==1 && given.weaponState.rounds==WeaponCatalog.Get(weapon.id).magazineSize,"оружие — одно, с полным магазином");
            Require(WeaponInventory.IsEquippable(given),"выданное оружие можно взять");
            DebugItems.Grant(actor,"item:hgren",3);DebugItems.Grant(actor,"item:hgren",2);
            Require(InventoryJournal.Count(actor,"item:hgren")==5 && WeaponCatalog.IsGrenade("item:hgren"),"гранаты складываются в стопку");
            Require(DebugItems.Grant(actor,"нет-такого",1)==null && DebugItems.Grant(actor,"item:hgren",DebugItems.MaxCount+1)==null,"чужой предмет и лишнее количество отклоняются");

            var word=new SecretWord("DEBUG");
            Require("DEBU".Select(word.Feed).ToArray().All(done=>!done) && word.Feed('G'),"слово целиком срабатывает");
            Require("DEBXG".Select(word.Feed).ToArray().All(done=>!done),"ошибка сбрасывает слово");word.Reset();
            Require("DDEBU".Select(word.Feed).ToArray().All(done=>!done) && word.Feed('G'),"повторная первая буква считается началом");

            var prefab=GameAssets.Load<DebugItemsView>("Presentation/DebugItems");
            Require(prefab!=null,"нет префаба");prefab.Validate();
            var fresh=new ActorState {character="c"};PlayerStarterInventory.Give(fresh);
            Require(!fresh.inventory.Exists(i=>CosmeticCatalog.Current.TryGet(i.definition,out _)),"новому персонажу не выдаётся одежда");
            Debug.Log($"Меню выдачи: PASS — {items.Count} предметов, оружие заряжено, стопки, отказ, секретное слово, префаб, стартовый набор {fresh.inventory.Count} стопок.");
        }
        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException("Меню выдачи: "+message);}
    }
}
