using System;
using System.Reflection;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using UnityEngine;

namespace Remains.Editor
{
    public static class ActiveReloadControllerChecks
    {
        public static void Run()
        {
            WeaponCatalog.Load();
            Check(false);Check(true);CheckPause();
            foreach(Action<ActorState,CoopSession> change in new Action<ActorState,CoopSession>[]
            {
                (a,s)=>a.weaponSelectionRevision++, (a,s)=>a.generation++, (a,s)=>a.lifecycleSequence++,
                (a,s)=>a.owner="other", (a,s)=>a.level="factory:0", (a,s)=>a.down=true,
                (a,s)=>s.World.worldId="other-world", (a,s)=>a.equippedWeaponItem="other-item"
            }) CheckCancellation(change);
            Debug.Log("Контроллер активной перезарядки: PASS — успех/промах, боезапас ровно один раз, обычный Start, события, пауза и смена контекста.");
        }
        private static (ActorState,CoopSession,WeaponReloadController) Create(GameObject root)
        {
            var definition=WeaponCatalog.Get("pistol");
            var actor=new ActorState {id="active-reload-player",owner="local",player=true,connected=true,level="hub",weapon="pistol",
                generation=2,lifecycleSequence=3,weaponInventoryVersion=1,weaponSelectionRevision=7,equippedWeaponItem="selected"};
            actor.inventory.Add(new ItemStack {id="selected",definition="pistol",count=1,weaponState=new WeaponItemState {rounds=definition.magazineSize-1}});
            actor.inventory.Add(new ItemStack {id="reserve",definition=definition.ammo,count=5});
            var session=root.AddComponent<CoopSession>();var world=new WorldState {worldId="active-reload-world"};world.actors.Add(actor);
            typeof(CoopSession).GetProperty("World").SetValue(session,world);
            typeof(CoopSession).GetProperty("LocalActorId").SetValue(session,actor.id);
            typeof(CoopSession).GetProperty("LocalOwner").SetValue(session,actor.owner);
            return (actor,session,new WeaponReloadController(session));
        }
        private static void Check(bool miss)
        {
            var root=new GameObject("Проверка активной перезарядки");
            try
            {
                var (actor,session,reload)=Create(root);int starts=0,ends=0;
                reload.Started+=(_,_)=>starts++;reload.Finished+=()=>ends++;
                float seconds=WeaponCatalog.Get("pistol").reloadSeconds;
                Require(reload.Request(actor,"pistol"),"Первое R не начало интервал.");
                Require(!reload.Start(actor,"pistol") && reload.Clock.Opportunity,"Обычный Start потратил активную попытку.");
                reload.Step("pistol",seconds*(miss?.1f:.6f));
                Require(reload.Request(actor,"pistol"),"Активное нажатие не принято.");
                if(miss)
                {
                    Require(reload.Active && !reload.Clock.Opportunity && reload.Remaining==seconds && starts==2 && ends==1,"Промах не перезапустил интервал и анимацию.");
                    Require(InventoryJournal.Count(actor,WeaponCatalog.Get("pistol").ammo)==5,"Промах потратил патроны.");
                    reload.Step("pistol",seconds*.6f);float remaining=reload.Remaining;
                    Require(!reload.Request(actor,"pistol") && reload.Remaining==remaining,"Промах оставил повторную попытку.");
                    reload.Step("pistol",seconds);
                }
                Require(!reload.Active && WeaponInventory.Selected(actor).weaponState.rounds==WeaponCatalog.Get("pistol").magazineSize &&
                    InventoryJournal.Count(actor,WeaponCatalog.Get("pistol").ammo)==4 && ends==(miss?2:1),"Перезарядка неверно завершила события или расход.");
                reload.Step("pistol",seconds);
                Require(InventoryJournal.Count(actor,WeaponCatalog.Get("pistol").ammo)==4,"Повтор Step повторно списал запас.");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void CheckPause()
        {
            var root=new GameObject("Проверка паузы активной перезарядки");
            try
            {
                var (actor,session,reload)=Create(root);reload.Start(actor,"pistol");float seconds=reload.Remaining;
                typeof(SessionPause).GetMethod("Receive",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(session.Pause,
                    new object[]{new PauseStateMessage {revision=1,levels=new[]{"hub"}}});
                reload.Step("pistol",seconds);
                Require(!reload.Request(actor,"pistol") && reload.Clock.Opportunity && reload.Remaining==seconds,"Пауза продвинула таймер или потратила попытку.");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void CheckCancellation(Action<ActorState,CoopSession> change)
        {
            var root=new GameObject("Проверка отмены активной перезарядки");
            try
            {
                var (actor,session,reload)=Create(root);reload.Start(actor,"pistol");reload.Step("pistol",reload.Remaining*.6f);
                change(actor,session);
                Require(!reload.Request(actor,"pistol"),"Активная попытка применена к другому контексту.");
                reload.Step("pistol",0);
                Require(!reload.Active && !reload.Clock.Opportunity && InventoryJournal.Count(actor,WeaponCatalog.Get("pistol").ammo)==5,
                    "Смена контекста сохранила окно или потратила патроны.");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void Require(bool valid,string message)
        {if(!valid) throw new InvalidOperationException("Контроллер активной перезарядки: "+message);}
    }
}
