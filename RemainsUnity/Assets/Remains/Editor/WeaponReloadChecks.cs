using System;
using System.Collections.Generic;
using System.Reflection;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    public static class WeaponReloadChecks
    {
        public static void Run()
        {
            WeaponCatalog.Load();
            foreach(string weapon in new[]{"pistol","energy"})
            {
                var asset=Resources.Load<WeaponAsset>("Content/weapons/"+weapon);var clip=asset.ReloadAnimation;
                Require(clip!=null && clip.FrameCount==(weapon=="pistol"?31:51),"Нет полной исходной последовательности: "+weapon);
                clip.ValidateAgainst(asset.Presentation.Artwork.sprite);
                CheckMuzzle(asset);
                foreach(int fps in new[]{30,60,144}) CheckLocal(weapon,fps);
                CheckContextCancellation(weapon);
                CheckRemote(weapon);CheckNpc(weapon);
            }
            Debug.Log("Изображение перезарядки PASS: actual timing30/60/144,82frames/registration/muzzle, pause/cancel/duplicate/token/item/epoch/owner/lifecycle/world и NPC state.");
        }
        private static ActorState Actor(string weapon,bool player=true)
        {
            var definition=WeaponCatalog.Get(weapon);
            var actor=new ActorState {id="reload-"+weapon,owner="local",player=player,connected=player,level="hub",weapon=weapon,
                generation=2,lifecycleSequence=3,weaponInventoryVersion=1,weaponSelectionRevision=7,equippedWeaponItem="selected"};
            actor.inventory.Add(new ItemStack {id="selected",definition=weapon,count=1,weaponState=new WeaponItemState {rounds=definition.magazineSize-1}});
            actor.inventory.Add(new ItemStack {id="reserve",definition=definition.ammo,count=5});return actor;
        }
        private static CoopSession Session(GameObject root,ActorState actor,bool local)
        {
            var session=root.AddComponent<CoopSession>();var world=new WorldState {worldId="reload-fixture"};world.actors.Add(actor);
            if(!local) world.actors.Add(new ActorState {id="observer",owner="observer",player=true,connected=true,level="hub"});
            typeof(CoopSession).GetProperty("World").SetValue(session,world);
            typeof(CoopSession).GetProperty("LocalActorId").SetValue(session,local?actor.id:"observer");
            typeof(CoopSession).GetProperty("LocalOwner").SetValue(session,local?actor.owner:"observer");return session;
        }
        private static ReloadSoundMessage Message(ActorState actor,string token,bool start=true)=>new()
        {actor=actor.id,token=token,weapon=actor.weapon,weaponItem=actor.equippedWeaponItem,selectionRevision=actor.weaponSelectionRevision,
            level=actor.level,generation=actor.generation,lifecycle=actor.lifecycleSequence,started=start};
        private static void Pause(CoopSession session,bool value,long revision)
        {typeof(SessionPause).GetMethod("Receive",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(session.Pause,new object[]{new PauseStateMessage {revision=revision,levels=value?new[]{"hub"}:Array.Empty<string>()}});}
        private static void CheckLocal(string weapon,int fps)
        {
            var root=new GameObject("Проверка действительной перезарядки");
            try
            {
                var actor=Actor(weapon);var session=Session(root,actor,true);var reload=new WeaponReloadController(session);
                var animation=root.AddComponent<WeaponReloadAnimation>();animation.Initialize(session,reload);
                var asset=Resources.Load<WeaponAsset>("Content/weapons/"+weapon);var clip=asset.ReloadAnimation;var rest=asset.Presentation.Artwork.sprite;
                var message=Message(actor,"local-token");reload.Started+=(a,w)=>animation.Observe(message);
                reload.Finished+=()=>{var stop=message;stop.started=false;animation.Observe(stop);};
                Require(reload.Start(actor,weapon),"Обычная неполная обойма не начала перезарядку");
                Require(animation.TryGet(actor,out var initial) && initial.Frame==0,"Первый кадр не принадлежит началу actual reload");
                Pause(session,true,1);float remaining=reload.Remaining;reload.Step(weapon,.4f);animation.Step(.4f);
                Require(reload.Remaining==remaining && animation.TryGet(actor,out var frozen) && frozen.Frame==initial.Frame,"Pause продвинула actual reload/изображение");
                Pause(session,false,2);int last=-1,steps=0;var seen=new HashSet<int>();
                while(reload.Active)
                {
                    Require(steps++<fps*3,"Перезарядка не завершилась");
                    Require(animation.TryGet(actor,out var state) && state.Frame>=last,"Кадр потерян/идёт назад");last=state.Frame;seen.Add(state.Frame);
                    Require(InventoryJournal.Count(actor,WeaponCatalog.Get(weapon).ammo)==5 && actor.inventoryUses.Count==0,"Изображение/начало списало резерв");
                    float progress=1-reload.Remaining/WeaponCatalog.Get(weapon).reloadSeconds;
                    Require(Mathf.Abs(state.Progress-progress)<.00001f && animation.Select(actor,rest)==clip.Frame(state.Frame),"Изображение расходится с actual remaining");
                    animation.Observe(message); // A replay must not reset progress.
                    reload.Step(weapon,1f/fps);animation.Step(1f/fps);
                }
                Require(seen.Count>10 && animation.Select(actor,rest)==rest,"Нет смены кадров/возврата в покой");
                Require(WeaponInventory.Selected(actor).weaponState.rounds==WeaponCatalog.Get(weapon).magazineSize &&
                    InventoryJournal.Count(actor,WeaponCatalog.Get(weapon).ammo)==4 && actor.inventoryUses.Count==1,"Actual finish потерял один расход/магазин");
                WeaponInventory.Selected(actor).weaponState.rounds--;message=Message(actor,"cancel-token");
                Require(reload.Start(actor,weapon),"Вторая actual reload не началась");
                actor.inventory.Add(new ItemStack {id="second",definition=weapon,count=1,weaponState=new WeaponItemState {rounds=1}});
                Require(WeaponInventory.Select(actor,"second",out _),"Не выбран другой экземпляр того же оружия");
                Require(animation.Select(actor,rest)==rest,"Другой экземпляр сохранил чужой кадр");
                reload.Step(weapon,1f/fps);animation.Step(1f/fps);animation.Observe(message);
                Require(!reload.Active && animation.Select(actor,rest)==rest && InventoryJournal.Count(actor,WeaponCatalog.Get(weapon).ammo)==4 && actor.inventoryUses.Count==1,
                    "Отмена/поздний start списали резерв или воскресили рисунок");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void CheckRemote(string weapon)
        {
            var root=new GameObject("Проверка удалённой перезарядки");
            try
            {
                var actor=Actor(weapon);var session=Session(root,actor,false);var animation=root.AddComponent<WeaponReloadAnimation>();animation.Initialize(session,null);
                var rest=Resources.Load<WeaponAsset>("Content/weapons/"+weapon).Presentation.Artwork.sprite;var first=Message(actor,"remote-first");
                animation.Observe(first);animation.Step(WeaponCatalog.Get(weapon).reloadSeconds*.5f);
                Require(animation.TryGet(actor,out var middle) && middle.Progress>.49f,"Удалённый actual intent не показал середину");
                animation.Observe(first);Require(animation.TryGet(actor,out var duplicate) && duplicate.Frame==middle.Frame,"Дубликат перезапустил клип");
                Pause(session,true,1);animation.Step(.5f);Require(animation.TryGet(actor,out var frozen) && frozen.Frame==middle.Frame,"Remote pause продвинула клип");
                Pause(session,false,2);var second=Message(actor,"remote-second");animation.Observe(second);
                first.started=false;animation.Observe(first);Require(animation.TryGet(actor,out var state) && state.Token==second.token,"Старый stop отменил новый token");
                second.started=false;animation.Observe(second);second.started=true;animation.Observe(second);
                Require(animation.Select(actor,rest)==rest,"Повтор ended token воскресил клип");
                int index=0;
                foreach(Action mutate in new Action[]{()=>actor.generation++,()=>actor.lifecycleSequence++,()=>actor.owner="other-owner",()=>actor.weaponSelectionRevision++,
                    ()=>actor.equippedWeaponItem="other",()=>actor.down=true,()=>actor.connected=false,()=>actor.level="factory:0",()=>session.World.worldId="other-world"})
                {
                    actor=Actor(weapon);session.World.actors[0]=actor;session.World.worldId="reload-fixture";
                    var message=Message(actor,"invalidate-"+(index++));animation.Observe(message);Require(animation.TryGet(actor,out _),"Нет клипа перед invalidation");
                    mutate();animation.Step(0);Require(animation.Select(actor,rest)==rest,"Invalidation оставила старый кадр: "+(index-1));
                }
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void CheckNpc(string weapon)
        {
            var root=new GameObject("Проверка NPC reload state");
            try
            {
                var actor=Actor(weapon,false);WeaponInventory.Selected(actor).weaponState.rounds=0;
                var session=Session(root,actor,false);var animation=root.AddComponent<WeaponReloadAnimation>();animation.Initialize(session,null);
                var rest=Resources.Load<WeaponAsset>("Content/weapons/"+weapon).Presentation.Artwork.sprite;
                var action=NpcWeaponActions.Prepare(actor);Require(action?.kind==NpcWeaponActions.Reload,"Actual NPC action не выбрал reload");
                actor.npcWeaponAction=action;actor.attackBusy=true;actor.attackId="npc-reload";actor.attackRemaining=WeaponCatalog.Get(weapon).reloadSeconds*.5f;
                Require(animation.TryGet(actor,out var state) && state.Token==actor.attackId && state.Progress>.49f,"NPC не читает actual action/remaining");
                NpcAttackRules.Cancel(actor);Require(animation.Select(actor,rest)==rest,"Cancel NPC оставил reload рисунок");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void CheckContextCancellation(string weapon)
        {
            foreach(string change in new[]{"world","level","selection-return"})
            {
                var root=new GameObject("Проверка контекстной отмены reload");
                try
                {
                    var actor=Actor(weapon);var session=Session(root,actor,true);var reload=new WeaponReloadController(session);
                    Require(reload.Start(actor,weapon),"Не началась контекстная reload");
                    if(change=="world") session.World.worldId="different-world";
                    else if(change=="level") actor.level="factory:0";
                    else Require(WeaponInventory.Select(actor,actor.equippedWeaponItem,out _),"Не выполнен возврат к тому же экземпляру");
                    reload.Step(weapon,WeaponCatalog.Get(weapon).reloadSeconds+1);
                    Require(!reload.Active && actor.inventoryUses.Count==0 && InventoryJournal.Count(actor,WeaponCatalog.Get(weapon).ammo)==5 &&
                        WeaponInventory.Selected(actor).weaponState.rounds==WeaponCatalog.Get(weapon).magazineSize-1,"Контекстная отмена списала резерв: "+change);
                }
                finally {UnityEngine.Object.DestroyImmediate(root);}
            }
        }
        private static void CheckMuzzle(WeaponAsset asset)
        {
            var actor=Actor(asset.Id);actor.Position=new Vector2(94,7.2f);
            foreach(int side in new[]{-1,1}) foreach(float angle in new[]{-80f,0,65})
            {
                actor.weaponAngle=angle;actor.weaponFlipY=side<0;
                var view=UnityEngine.Object.Instantiate(asset.Presentation);
                try
                {
                    view.transform.position=WeaponPose.Mount(actor);view.transform.rotation=Quaternion.Euler(0,0,angle);view.SetMirrored(side<0);
                    for(int i=0;i<asset.ReloadAnimation.FrameCount;i++)
                    {view.SetFrame(asset.ReloadAnimation.Frame(i));Require(Vector2.Distance(view.Muzzle.position,WeaponPose.Muzzle(actor))<.0001f,"Reload кадр изменил реальное дуло: "+asset.Id+"/"+i);}
                }
                finally {UnityEngine.Object.DestroyImmediate(view.gameObject);}
            }
        }
        private static void Require(bool value,string reason)
        {if(!value) throw new InvalidOperationException("Изображение перезарядки: "+reason);}
    }
}
