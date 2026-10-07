using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Remains.Editor
{
    public static class InventoryItemActionChecks
    {
        private sealed class FixedAction : IItemActionRule
        {
            public string Id;
            public string Action(ItemStack item)=>item==null?null:Id;
        }
        private sealed class BrokenAction : IItemActionRule
        {
            public int Calls;
            public string Action(ItemStack item) {Calls++;throw new InvalidOperationException("Ошибка ItemActions проверки.");}
        }
        private sealed class Fixture : IDisposable
        {
            public readonly GameObject Root;
            public readonly CoopSession Session;
            public readonly ActorState Actor;
            public readonly ItemStack Ordinary,Healing,Weapon;
            public readonly ItemShortcutActions Actions;
            public readonly PersonalInventoryView View;
            public readonly PersonalInventoryPanel Panel;
            public Fixture(Transform parent=null)
            {
                Root=new GameObject("Проверка прямого действия предмета",typeof(RectTransform));
                if(parent!=null) Root.transform.SetParent(parent,false);
                Session=Root.AddComponent<CoopSession>();Session.ConfigureRules(WeaponCatalog.Rules);
                Actor=new ActorState {id="direct-item-owner",owner="direct-owner",player=true,connected=true,
                    loadoutVersion=1,weaponInventoryVersion=1,character="Персонаж проверки",health=65};
                Ordinary=new ItemStack {id="direct-ammunition",definition="p10",count=8,
                    extensions=new System.Collections.Generic.Dictionary<string,JToken> {["mod:label"]="сохранить"}};
                Healing=new ItemStack {id="direct-heal",definition="heal",count=3};
                Weapon=new ItemStack {id="direct-pistol",definition="pistol",count=1,weaponState=new WeaponItemState {rounds=7}};
                Actor.inventory.Add(Ordinary);Actor.inventory.Add(Healing);Actor.inventory.Add(Weapon);
                Actor.inventoryAliases.Add("old-ammunition",Ordinary.id);
                var world=new WorldState();world.actors.Add(Actor);
                typeof(CoopSession).GetProperty(nameof(CoopSession.World)).SetValue(Session,world);
                typeof(CoopSession).GetProperty(nameof(CoopSession.LocalActorId)).SetValue(Session,Actor.id);
                typeof(CoopSession).GetProperty(nameof(CoopSession.LocalOwner)).SetValue(Session,Actor.owner);
                Actions=new ItemShortcutActions(Session,id=>
                {
                    if(!WeaponInventory.Select(Actor,id,out var reason)) throw new InvalidOperationException(reason);
                    Session.PublishActor(Actor);
                });
                var window=InventoryWindowView.Create(Root.transform);
                var rect=window.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);rect.anchoredPosition=Vector2.zero;
                View=window.PersonalContents;
                var text=ItemTextCatalog.Load();
                Panel=new PersonalInventoryPanel(View,Session,id=>text.Name(id,"InventoryItemActionChecks"),()=>0,
                    _=>throw new InvalidOperationException("Прямое действие выбрало старое определение оружия."),new InventoryArtwork(),itemActions:()=>Actions);
                Panel.Refresh(null);
            }
            public void Dispose()=>UnityEngine.Object.DestroyImmediate(Root);
        }
        public static void Run()
        {
            WeaponCatalog.Load();
            using(var fixture=new Fixture())
            {
                var actor=fixture.Actor;var view=fixture.View;var actions=fixture.Actions;
                string beforeActor=StateJson.ToJson(actor),beforeInventory=StateJson.ToJson(actor.inventory);
                view.Rows[0].onClick.Invoke();
                Require(StateJson.ToJson(actor)==beforeActor,"выбор строки изменил персонажа");
                Require(view.Use.interactable && view.Use.GetComponentInChildren<Text>().text=="Взять в копыта",
                    "обычная вещь не доступна напрямую без быстрого назначения");
                view.Use.onClick.Invoke();
                Require(HeldItemSelection.Selected(actor)==fixture.Ordinary && actor.weaponShortcuts.Count==0 &&
                    actor.inventoryUses.Count==0 && StateJson.ToJson(actor.inventory)==beforeInventory,
                    "взятие в копыта назначило слот, потратило вещь или потеряло mod-поля");
                view.Use.onClick.Invoke();
                Require(StateJson.ToJson(actor.inventory)==beforeInventory && actor.inventoryUses.Count==0,"повторное взятие расходует стопку");
                Require(actions.ActivateItem("old-ammunition",Vector2.one,true) && HeldItemSelection.Selected(actor)==fixture.Ordinary &&
                    StateJson.ToJson(actor.inventory)==beforeInventory,"alias/режим всей стопки изменил количество до отдельного броска");

                actor.inventoryReservations[fixture.Ordinary.id]=8;fixture.Panel.Refresh(null);
                string reserved=StateJson.ToJson(actor);view.Use.onClick.Invoke();
                Require(!view.Use.interactable && view.ActionStatus.text.Contains("зарезервирован") && StateJson.ToJson(actor)==reserved,
                    "резерв можно обойти кнопкой либо он не объяснён");
                actor.inventoryReservations[fixture.Ordinary.id]=2;fixture.Panel.Refresh(null);
                Require(view.Use.interactable && actions.GetAvailability(fixture.Ordinary.id).Enabled,"частичный резерв заблокировал остаток");
                actor.inventoryReservations.Clear();
                actor.down=true;Require(!actions.GetAvailability(fixture.Ordinary.id).Enabled && !actions.ActivateItem(fixture.Ordinary.id),"нокаут не блокирует действие");
                actor.down=false;actor.connected=false;Require(!actions.GetAvailability(fixture.Ordinary.id).Enabled,"отключённый персонаж может использовать вещь");actor.connected=true;
                var receive=typeof(SessionPause).GetMethod("Receive",BindingFlags.NonPublic|BindingFlags.Instance);
                receive.Invoke(fixture.Session.Pause,new object[] {new PauseStateMessage {revision=1,levels=new[]{actor.level}}});
                Require(!actions.GetAvailability(fixture.Ordinary.id).Enabled && !actions.ActivateItem(fixture.Ordinary.id),"пауза не блокирует прямое действие");
                receive.Invoke(fixture.Session.Pause,new object[] {new PauseStateMessage {revision=2,levels=Array.Empty<string>()}});

                view.Rows[2].onClick.Invoke();view.Use.onClick.Invoke();
                Require(actor.equippedWeaponItem==fixture.Weapon.id && !HeldItemSelection.HasSelection(actor),"оружие не экипировано после обычной вещи");
                Require(view.Use.GetComponentInChildren<Text>().text=="Убрать оружие","кнопка не предлагает снять текущий экземпляр");
                view.Use.onClick.Invoke();Require(actor.equippedWeaponItem==null && actor.weapon==null,"повторное UI действие не сняло оружие");
                ItemShortcuts.Assign(actor,"1",fixture.Weapon.id,out _);
                Require(actions.Activate("1",null) && actions.Activate("1",null) && actor.equippedWeaponItem==fixture.Weapon.id,
                    "быстрая клавиша стала снимать выбранное оружие");
                actor.health=100;view.Rows[1].onClick.Invoke();
                Require(!view.Use.interactable && view.ActionStatus.text==HealingRules.Rejection(actor,actor,true),"встроенное лечение обходится при полном здоровье");
                actor.health=65;fixture.Panel.Refresh(null);Require(view.Use.interactable,"встроенное лечение недоступно при повреждении");
                actor.health=100;
                int modHeal=0;
                actions.Register(StandardItemActions.Heal,(item,context)=>modHeal++,replace:true);
                fixture.Panel.Refresh(null);view.Use.onClick.Invoke();
                Require(view.Use.interactable && modHeal==1 && StateJson.ToJson(actor.inventory)==beforeInventory,
                    "замена heal сохранила старое HealingRules условие или вызвала старый расход");
                actions.Register(StandardItemActions.Heal,(item,context)=>modHeal++,replace:true,rejection:(_,_)=>"Ограничение мода.");
                fixture.Panel.Refresh(null);view.Use.onClick.Invoke();
                Require(!view.Use.interactable && view.ActionStatus.text=="Ограничение мода." && modHeal==1,
                    "модовое условие доступности не используется UI и activation");
                view.Categories[(int)InventoryCategory.Armor].onClick.Invoke();
                Require(!view.Use.interactable && !view.Rows[0].gameObject.activeInHierarchy,"пустая категория брони разрешила действие отсутствующего предмета");

                var rule=new FixedAction {Id="mod:direct-use"};fixture.Session.ConfigureRules(WithAction(fixture.Session.Rules,rule));
                int custom=0;bool toggled=false,whole=false;Vector2? aim=null;
                actions.Register(rule.Id,(item,context)=>{custom++;toggled=context.ToggleEquipped;whole=context.WholeStack;aim=context.Aim;},caption:(_,_)=>"Действие мода");
                view.Categories[(int)InventoryCategory.All].onClick.Invoke();view.Rows[0].onClick.Invoke();
                Require(view.Use.interactable && view.Use.GetComponentInChildren<Text>().text=="Действие мода","UI игнорирует модовый ItemActions/текст");
                view.Use.onClick.Invoke();Require(custom==1 && toggled && !whole && aim==null,"UI не передал контекст прямого действия ровно один раз");
                ItemShortcuts.Assign(actor,"2",fixture.Ordinary.id,out _);
                Require(actions.Activate("2",Vector2.one,true) && custom==2 && !toggled && whole && aim==Vector2.one,"shortcut изменил свой контекст");
                // The old public registration remains source compatible and replaces the predicate too.
                actions.Register(rule.Id,(item,all,point)=>custom++,replace:true);
                Require(actions.ActivateItem(fixture.Ordinary.id) && custom==3,"прежний Register не работает с прямым действием");
                actions.Register(rule.Id,(item,context)=>custom++,replace:true,rejection:(_,_)=>"Запрет мода");
                Require(!actions.ActivateItem(fixture.Ordinary.id) && custom==3,"явный модовый запрет не проверяется при исполнении");
                actions.Register(rule.Id,(item,all,point)=>custom++,replace:true);
                Require(actions.GetAvailability(fixture.Ordinary.id).Enabled && actions.ActivateItem(fixture.Ordinary.id) && custom==4,
                    "legacy replace оставил predicate предыдущего handler");
                Require(StateJson.ToJson(actor.inventory)==beforeInventory && actor.inventoryUses.Count==0,"модовые действия молча изменили стопки");
                rule.Id="mod:missing-use";fixture.Panel.Refresh(null);
                string unsupported=StateJson.ToJson(actor);view.Use.onClick.Invoke();
                Require(!view.Use.interactable && view.ActionStatus.text.Contains("не подключено") && StateJson.ToJson(actor)==unsupported && custom==4,
                    "неизвестный action был молча заменён удерживанием либо изменил персонажа");
                actor.inventory.Remove(fixture.Ordinary);
                Require(!actions.GetAvailability("old-ammunition").Enabled && !actions.ActivateItem("old-ammunition"),"устаревший ID принимает отсутствующий предмет");
            }
            Debug.Log("Прямые действия инвентаря: PASS — ID/alias, без расхода и назначения, резервы/пауза, оружейный toggle, лечение, модовые handlers/predicates и пустая броня.");
        }
        private static GameplayRuleSet WithAction(GameplayRuleSet rules,IItemActionRule action)=>new(
            rules.Damage,rules.Knockdown,rules.PartyDefeat,rules.Loot,rules.Pause,rules.Deposit,rules.Telekinesis,
            rules.ProximityOwnership,rules.DoorAccess,action,rules.NpcWeaponTiming,rules.NaturalAttackDamage,
            rules.ButtonRepair,rules.DoorDamage,rules.ShotDirection,rules.WeaponRecoil);

        // Opt-in negative checks intentionally emit the same six error logs as production.
        // Keep them separate from the normal native build's error-free ContractChecks.
        public static void RunFailures()
        {
            WeaponCatalog.Load();
            for(int stage=0;stage<6;stage++)
            {
                using(var fixture=new Fixture())
                {
                    int calls=0,reports=0;Exception reported=null;
                    fixture.Session.GameplayPreparationFailed+=error=>{reports++;reported=error;};
                    string before=StateJson.ToJson(fixture.Actor);
                    BrokenAction rule=null;
                    if(stage==0)
                    {
                        rule=new BrokenAction();fixture.Session.ConfigureRules(WithAction(fixture.Session.Rules,rule));
                    }
                    else
                        fixture.Actions.Register(StandardItemActions.Hold,(item,context)=>
                        {calls++;if(stage==3) throw new InvalidOperationException("Ошибка action проверки.");},replace:true,
                            caption:stage is 1 or 4?(_,_)=>{calls++;if(stage==1) throw new InvalidOperationException("Ошибка caption проверки.");return "";}:null,
                            rejection:stage is 2 or 5?(_,_)=>{calls++;if(stage==2) throw new InvalidOperationException("Ошибка rejection проверки.");return " ";}:null);
                    if(stage==3) Require(!fixture.Actions.ActivateItem(fixture.Ordinary.id),"сбой handler возвращает успешное действие");
                    else Require(!fixture.Actions.GetAvailability(fixture.Ordinary.id).Enabled,"ошибочная availability доступна");
                    Require(fixture.Session.GameplayFailurePending && reports==1 && reported is InvalidOperationException &&
                        reported.InnerException is InvalidOperationException && reported.Message.Contains(fixture.Actor.id) &&
                        reported.Message.Contains(fixture.Ordinary.id),"ошибка мода не передана в единый session failure с контекстом");
                    int reached=rule?.Calls??calls;
                    for(int i=0;i<3;i++)
                        Require(!fixture.Actions.GetAvailability(fixture.Ordinary.id).Enabled && !fixture.Actions.ActivateItem(fixture.Ordinary.id),
                            "после ошибки продолжается исполнение модового действия");
                    Require(!fixture.Actions.TryThrow(Vector2.one),"после ошибки допускается отдельный бросок");
                    Require(reached==1 && (rule?.Calls??calls)==1 && reports==1 && StateJson.ToJson(fixture.Actor)==before,
                        "ошибка повторяется при refresh или выполнен hold fallback/изменение предмета");
                }
            }
            Debug.Log("Ошибки прямого действия: PASS — шесть ожидаемых ошибок Rules.Action/Caption/Rejection/Action/пустых значений, один отказ с контекстом, без повторных delegates и подмены.");
        }

        public static void Render()
        {
            WeaponCatalog.Load();
            foreach(var size in new[]{new Vector2Int(1024,768),new Vector2Int(1280,720),new Vector2Int(1920,1080)})
            {
                var root=new GameObject("Макет прямого действия",typeof(RectTransform));Camera camera=null;RenderTexture target=null;Texture2D image=null;
                var previous=RenderTexture.active;
                try
                {
                    var canvas=root.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;
                    camera=new GameObject("Камера личных вещей").AddComponent<Camera>();camera.transform.position=new Vector3(0,0,-10);
                    canvas.worldCamera=camera;canvas.planeDistance=1;
                    target=new RenderTexture(size.x,size.y,24);target.Create();camera.targetTexture=target;
                    var scaler=root.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution=new Vector2(1280,720);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
                    root.AddComponent<GraphicRaycaster>();var events=root.AddComponent<EventSystem>();
                    using(var fixture=new Fixture(root.transform))
                    {
                        fixture.View.Rows[0].onClick.Invoke();
                        Canvas.ForceUpdateCanvases();camera.Render();Canvas.ForceUpdateCanvases();
                        var center=camera.WorldToViewportPoint(root.GetComponent<RectTransform>().TransformPoint(root.GetComponent<RectTransform>().rect.center));
                        Require(Mathf.Abs(center.x-.5f)<.001f && Mathf.Abs(center.y-.5f)<.001f && Mathf.Abs(center.z-canvas.planeDistance)<.001f,"камера Canvas не подготовлена native renderer");
                        foreach(var rect in new[]{fixture.Panel.RowRect(0),fixture.Panel.UseRect,fixture.Panel.DepositRect,
                            fixture.View.Description.Rect,fixture.View.ActionStatus.rectTransform,fixture.Panel.WeaponRect(11),fixture.Panel.CategoryRect(InventoryCategory.Armor)})
                            CheckCorners(rect,camera,size);
                        foreach(var label in fixture.View.GetComponentsInChildren<Text>())
                            Require(label.preferredHeight<=label.rectTransform.rect.height+.1f,$"текст обрезается: {size}, {label.name}, текст={label.text}");
                        var screen=RectTransformUtility.WorldToScreenPoint(camera,fixture.Panel.UseRect.TransformPoint(fixture.Panel.UseRect.rect.center));
                        var pointer=new PointerEventData(events) {position=screen,button=PointerEventData.InputButton.Left};
                        var hits=new System.Collections.Generic.List<RaycastResult>();canvas.GetComponent<GraphicRaycaster>().Raycast(pointer,hits);
                        Require(hits.Count>0 && hits[0].gameObject.transform.IsChildOf(fixture.View.Use.transform),"прямое действие закрыто другой графикой для указателя");
                        string inventory=StateJson.ToJson(fixture.Actor.inventory);ExecuteEvents.Execute(fixture.View.Use.gameObject,pointer,ExecuteEvents.pointerClickHandler);
                        Require(HeldItemSelection.Selected(fixture.Actor)==fixture.Ordinary && fixture.Actor.weaponShortcuts.Count==0 &&
                            StateJson.ToJson(fixture.Actor.inventory)==inventory && fixture.View.gameObject.activeInHierarchy,
                            "реальный UI-click расходует предмет/назначает слот/закрывает личное окно");
                        image=new Texture2D(size.x,size.y,TextureFormat.RGBA32,false);
                        camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,size.x,size.y),0,0);image.Apply();
                        string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/inventory-direct-use"));Directory.CreateDirectory(directory);
                        File.WriteAllBytes(Path.Combine(directory,$"ordinary-held-{size.x}x{size.y}.png"),image.EncodeToPNG());
                    }
                }
                finally
                {
                    RenderTexture.active=previous;if(image!=null) UnityEngine.Object.DestroyImmediate(image);
                    if(target!=null) {camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);}
                    UnityEngine.Object.DestroyImmediate(root);if(camera!=null) UnityEngine.Object.DestroyImmediate(camera.gameObject);
                }
            }
            Debug.Log("Прямое действие инвентаря: сохранённый макет/подписи/raycast/без расхода, Expand 1024/1280/1920 — PASS; обычный сетевой бросок проверяется отдельным runtime-сценарием.");
        }
        private static void CheckCorners(RectTransform rect,Camera camera,Vector2Int size)
        {
            var corners=new Vector3[4];rect.GetWorldCorners(corners);
            foreach(var corner in corners)
            {var point=camera.WorldToViewportPoint(corner);Require(point.x>=0 && point.y>=0 && point.x<=1 && point.y<=1,$"поле вне кадра {size}: {rect.name}, point={point}");}
        }
        private static void Require(bool condition,string reason) {if(!condition) throw new InvalidOperationException("Прямое действие предмета: "+reason);}
    }
}
