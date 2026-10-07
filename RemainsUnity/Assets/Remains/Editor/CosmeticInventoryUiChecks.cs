using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using Remains.Persistence;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Remains.Editor
{
    // Explicit Editor fixtures only. The native probe uses the real starter inventory.
    public static class CosmeticInventoryUiChecks
    {
        private sealed class Fixture : IDisposable
        {
            public readonly GameObject Root;
            public readonly CoopSession Session;
            public readonly ActorState Actor;
            public readonly ItemShortcutActions Actions;
            public readonly PersonalInventoryView View;
            public readonly PersonalInventoryPanel Panel;
            public readonly ItemStack Pip,Antirad;
            public Fixture(Transform parent=null)
            {
                Root=new GameObject("Проверка интерфейса одежды",typeof(RectTransform));
                if(parent!=null) Root.transform.SetParent(parent,false);
                Session=Root.AddComponent<CoopSession>();Session.ConfigureRules(WeaponCatalog.Rules);
                Actor=new ActorState {id="cosmetic-ui-owner",character="Персонаж одежды",owner="cosmetic-ui",
                    player=true,connected=true,health=75,mana=43,weapon=null,loadoutVersion=1,weaponInventoryVersion=1,
                    appearance=new CharacterAppearance {version=1,hairStyle=5,eyeStyle=6,secondaryHairEnabled=true,
                        fur=0xABCDEF,hair=0x884411,secondaryHair=0x442288,eyes=0x22FF44,magic=0xFF33CC}};
                Pip=new ItemStack {id="cosmetic-ui-pip",definition="armor.pip",count=2};
                Antirad=new ItemStack {id="cosmetic-ui-antirad",definition="armor.antirad",count=2};
                Actor.inventory.Add(Pip);Actor.inventory.Add(Antirad);
                var world=new WorldState();world.actors.Add(Actor);
                typeof(CoopSession).GetProperty(nameof(CoopSession.World)).SetValue(Session,world);
                typeof(CoopSession).GetProperty(nameof(CoopSession.LocalActorId)).SetValue(Session,Actor.id);
                typeof(CoopSession).GetProperty(nameof(CoopSession.LocalOwner)).SetValue(Session,Actor.owner);
                Actions=new ItemShortcutActions(Session,_=>throw new InvalidOperationException("Одежда выбрала оружие."));
                var window=InventoryWindowView.Create(Root.transform);
                var rect=window.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);rect.anchoredPosition=Vector2.zero;
                View=window.PersonalContents;
                var text=ItemTextCatalog.Load();
                Panel=new PersonalInventoryPanel(View,Session,id=>text.Name(id,"CosmeticInventoryUiChecks"),()=>0,
                    _=>throw new InvalidOperationException("Одежда выбрала старое определение оружия."),new InventoryArtwork(),itemActions:()=>Actions);
                Panel.Refresh(null);View.Categories[(int)InventoryCategory.Armor].onClick.Invoke();
            }
            public void Dispose()=>UnityEngine.Object.DestroyImmediate(Root);
        }
        public static void Run()
        {
            WeaponCatalog.Load();
            using(var f=new Fixture())
            {
                var actor=f.Actor;string inventory=StateJson.ToJson(actor.inventory),uses=StateJson.ToJson(actor.inventoryUses);
                string original=StateJson.ToJson(actor);f.View.Rows[0].onClick.Invoke();
                Require(StateJson.ToJson(actor)==original && Caption(f)=="Надеть" && f.View.Use.interactable,"выбор строки действует сам либо кнопка недоступна");
                f.View.Use.onClick.Invoke();Require(CosmeticEquipment.IsEquipped(actor,f.Pip.id) && Caption(f)=="Снять","обычная кнопка не надела комплект");
                CheckUnchanged(f,inventory,uses);CheckPortrait(f);CheckRowLabels(f,null);
                string worn=StateJson.ToJson(actor.cosmeticEquipment);long selection=actor.weaponSelectionRevision;
                f.View.Weapons[8].onClick.Invoke();
                Require(ItemShortcuts.Get(actor,"9")==f.Pip && actor.weaponSelectionRevision==selection+1 &&
                    StateJson.ToJson(actor.cosmeticEquipment)==worn,"назначение клавиши изменило wardrobe");
                for(int i=0;i<3;i++) Require(f.Actions.Activate("9",null),"повтор быстрой клавиши не исполнен");
                Require(StateJson.ToJson(actor.cosmeticEquipment)==worn && actor.weaponSelectionRevision==selection+1,"повтор shortcut снял одежду либо повторил stamp");
                f.View.Rows[1].onClick.Invoke();f.View.Use.onClick.Invoke();
                Require(CosmeticEquipment.IsEquipped(actor,f.Antirad.id) && !CosmeticEquipment.IsEquipped(actor,f.Pip.id),"смена одного слота оставила два комплекта");
                CheckPortrait(f);CheckRowLabels(f,null);f.View.Use.onClick.Invoke();
                Require(!CosmeticEquipment.IsEquipped(actor,f.Antirad.id) && Caption(f)=="Надеть" &&
                    actor.cosmeticEquipment.entries.Single().item==null,"UI не снял комплект или потерял пустой слот");
                CheckPortrait(f);f.View.Use.onClick.Invoke();CheckPortrait(f);
                actor.inventoryReservations[f.Antirad.id]=2;f.Panel.Refresh(null);string reserved=StateJson.ToJson(actor);
                Require(!f.View.Use.interactable && f.View.ActionStatus.text.Contains("зарезервирован"),"полный резерв не объяснён");
                f.View.Use.onClick.Invoke();Require(StateJson.ToJson(actor)==reserved,"резерв обойдён callback кнопки");
                actor.inventoryReservations.Clear();actor.down=true;f.Panel.Refresh(null);
                Require(!f.View.Use.interactable && !f.Actions.ActivateItem(f.Antirad.id,toggleEquipped:true),"нокаут допускает смену одежды");
                actor.down=false;f.Panel.Refresh(null);CheckUnchanged(f,inventory,uses);
                foreach(int hair in new[]{1,5,PonyAnimationData.Library.hairStyles})
                {actor.appearance.hairStyle=hair;f.Panel.Refresh(null);CheckPortrait(f);}
                string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/cosmetic-ui-"+Guid.NewGuid().ToString("N")));
                var store=new SaveStore(path);store.SaveCharacter(actor);var loaded=store.LoadCharacter();
                Require(StateJson.ToJson(loaded.cosmeticEquipment)==StateJson.ToJson(actor.cosmeticEquipment) &&
                    StateJson.ToJson(loaded.inventory)==inventory,"сохранение UI выбора потеряло outfit/count2");
                Require(InventoryJournal.Debit(actor,"cosmetic-ui-partial",f.Antirad.id,1) && CosmeticEquipment.IsEquipped(actor,f.Antirad.id),"часть стопки сняла одежду");
                Require(InventoryJournal.Debit(actor,"cosmetic-ui-final",f.Antirad.id,1) && !CosmeticEquipment.IsEquipped(actor,f.Antirad.id),"последняя единица оставила фантом");
                f.Panel.Refresh(null);CheckPortrait(f);
            }
            Debug.Log("Интерфейс одежды: PASS — выбор строки, Надеть/Снять/смена, count2, повтор shortcut, резервы/нокаут, стили/портрет, сохранение и последняя единица.");
        }
        private static string Caption(Fixture f)=>f.View.Use.GetComponentInChildren<Text>().text;
        private static void CheckUnchanged(Fixture f,string inventory,string uses)
        {
            Require(StateJson.ToJson(f.Actor.inventory)==inventory && StateJson.ToJson(f.Actor.inventoryUses)==uses &&
                f.Actor.health==75 && f.Actor.mana==43 && f.Actor.x==0 && f.Actor.y==0 && f.Actor.vx==0 && f.Actor.vy==0 &&
                f.Actor.weapon==null && f.Actor.equippedWeaponItem==null && f.Actor.heldItemId==null && f.View.gameObject.activeInHierarchy,
                "действие изменило количество, HP, оружие, movement или закрыло окно");
        }
        private static void CheckPortrait(Fixture f)
        {
            var plan=PonyRenderSelection.PrepareEquipment(f.Actor);var frame=plan.PortraitFrame;
            var appearance=PonyAppearance.Snapshot(f.Actor);
            Require(f.View.Portrait.activeInHierarchy && frame.Length<=f.View.PortraitParts.Length,"нет actual составного портрета/ёмкости");
            for(int i=0;i<f.View.PortraitParts.Length;i++)
            {
                var part=f.View.PortraitParts[i];
                if(i>=frame.Length) {Require(!part.gameObject.activeSelf,"лишняя часть портрета не скрыта");continue;}
                Require(part.gameObject.activeSelf==frame[i].visible && part.mainTexture==frame[i].material.mainTexture &&
                    part.color==frame[i].baseColor*PonyAppearance.Tint(frame[i].slot,appearance),"портрет не читает общий план/цвет: part="+i);
                for(int p=0;p<4;p++) Require(part.PreparedPoint(p)==frame[i].points[p],"вершины портрета не совпали: part="+i);
            }
            bool worn=f.Actor.cosmeticEquipment?.entries.Any(entry=>entry.item!=null)==true;
            Require(plan.HasEquipment==worn && plan.VisualIds.Count==(worn?1:0),"портрет сохранил старую одежду");
        }
        // Native Editor command; no runtime test or original-runtime equivalence implied.
        public static void Render()
        {
            WeaponCatalog.Load();
            foreach(var size in new[]{new Vector2Int(1024,768),new Vector2Int(1280,720),new Vector2Int(1920,1080)})
            {
                var root=new GameObject("Макет одежды",typeof(RectTransform));Camera camera=null;RenderTexture target=null;Texture2D image=null;
                var previous=RenderTexture.active;
                try
                {
                    var canvas=root.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;
                    camera=new GameObject("Камера одежды").AddComponent<Camera>();camera.transform.position=new Vector3(0,0,-10);canvas.worldCamera=camera;canvas.planeDistance=1;
                    target=new RenderTexture(size.x,size.y,24);target.Create();camera.targetTexture=target;
                    var scaler=root.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution=new Vector2(1280,720);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
                    var raycaster=root.AddComponent<GraphicRaycaster>();var events=root.AddComponent<EventSystem>();
                    using(var f=new Fixture(root.transform))
                    {
                        f.View.Rows[0].onClick.Invoke();Canvas.ForceUpdateCanvases();camera.Render();Canvas.ForceUpdateCanvases();
                        var center=camera.WorldToViewportPoint(root.GetComponent<RectTransform>().TransformPoint(root.GetComponent<RectTransform>().rect.center));
                        Require(Mathf.Abs(center.x-.5f)<.001f && Mathf.Abs(center.y-.5f)<.001f && Mathf.Abs(center.z-canvas.planeDistance)<.001f,"Canvas не подготовлен native камерой");
                        foreach(var rect in new[]{f.Panel.UseRect,f.Panel.RowRect(0),f.Panel.RowRect(1),f.Panel.CategoryRect(InventoryCategory.Armor),f.Panel.WeaponRect(8)})
                            CheckCorners(rect,camera,size);
                        string inventory=StateJson.ToJson(f.Actor.inventory);
                        foreach(var stage in new[]{"pip","antirad","removed"})
                        {
                            if(stage=="antirad") f.View.Rows[1].onClick.Invoke();
                            var screen=RectTransformUtility.WorldToScreenPoint(camera,f.Panel.UseRect.TransformPoint(f.Panel.UseRect.rect.center));
                            var pointer=new PointerEventData(events) {position=screen,button=PointerEventData.InputButton.Left};
                            var hits=new List<RaycastResult>();raycaster.Raycast(pointer,hits);
                            Require(hits.Count>0 && hits[0].gameObject.transform.IsChildOf(f.View.Use.transform),"реальная Use кнопка закрыта другой графикой");
                            ExecuteEvents.Execute(f.View.Use.gameObject,pointer,ExecuteEvents.pointerClickHandler);CheckPortrait(f);
                            Require(StateJson.ToJson(f.Actor.inventory)==inventory && f.View.gameObject.activeInHierarchy &&
                                (stage=="removed"?!CosmeticEquipment.IsEquipped(f.Actor,f.Antirad.id):CosmeticEquipment.IsEquipped(f.Actor,stage=="pip"?f.Pip.id:f.Antirad.id)),"actual click не подтвердил outfit/count/window");
                            Canvas.ForceUpdateCanvases();camera.Render();Canvas.ForceUpdateCanvases();
                            foreach(var label in f.View.GetComponentsInChildren<Text>())
                            {
                                var rect=label.rectTransform.rect;float preferred=label.preferredHeight;
                                Require(preferred<=rect.height+.1f,$"текст одежды обрезан: screen={size}, stage={stage}, label={label.name}, font={label.fontSize}, rect={rect}, preferredHeight={preferred}, preferredWidth={label.preferredWidth}, text={label.text}");
                            }
                            CheckRowLabels(f,size);
                            image=new Texture2D(size.x,size.y,TextureFormat.RGBA32,false);RenderTexture.active=target;
                            image.ReadPixels(new Rect(0,0,size.x,size.y),0,0);image.Apply();
                            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/cosmetic-inventory-ui"));Directory.CreateDirectory(directory);
                            File.WriteAllBytes(Path.Combine(directory,$"{stage}-{size.x}x{size.y}.png"),image.EncodeToPNG());
                            UnityEngine.Object.DestroyImmediate(image);image=null;
                        }
                    }
                }
                finally
                {
                    RenderTexture.active=previous;if(image!=null) UnityEngine.Object.DestroyImmediate(image);
                    if(target!=null) {camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);}
                    UnityEngine.Object.DestroyImmediate(root);if(camera!=null) UnityEngine.Object.DestroyImmediate(camera.gameObject);
                }
            }
            Debug.Log("Макеты одежды: PASS — native Expand 1024/1280/1920, actual raycast, три состояния, полные однострочные названия/количество/надето при шрифте 16–20 и общий составной портрет. Сетевой/reload сценарий проверяется отдельно.");
        }
        private static void CheckRowLabels(Fixture f,Vector2Int? screen)
        {
            var items=new[]{f.Pip,f.Antirad};var text=ItemTextCatalog.Load();
            for(int i=0;i<items.Length;i++)
            {
                var item=items[i];var label=f.View.Rows[i].GetComponentInChildren<Text>();
                string name=text.Name(item.definition,"CosmeticInventoryUiChecks: row="+i);
                string expected=(f.Panel.SelectedItemId==item.id?"› ":"")+name+" × 2"+
                    (CosmeticEquipment.IsEquipped(f.Actor,item.id)?" · надето":"");
                var rect=label.rectTransform.rect;float width=label.preferredWidth,height=label.preferredHeight;
                string context=$"screen={screen}, row={i}, item={item.id}, font={label.fontSize}, rect={rect}, preferred={width}/{height}, text={label.text}";
                Require(label.text==expected,"полное название, количество или статус пропали: "+context);
                Require(label.fontSize>=16 && label.fontSize<=20 && !label.resizeTextForBestFit && !label.supportRichText &&
                    label.horizontalOverflow==HorizontalWrapMode.Overflow && !label.text.Contains('\n') && !label.text.Contains('\r'),
                    "нарушена читаемая однострочная подпись: "+context);
                Require(float.IsFinite(width) && float.IsFinite(height) && width<=rect.width+.1f && height<=rect.height+.1f,
                    "название выходит за фактическое поле: "+context);
                if(screen.HasValue) Require(label.cachedTextGenerator.lineCount==1,"native подпись перенесена на другую строку: "+context);
                if(f.Panel.SelectedItemId==item.id)
                {
                    string description=text.Description(item.definition,"CosmeticInventoryUiChecks: detail="+i);
                    Require(f.View.Detail.text.Contains(name) && f.View.Detail.text.Contains(description) &&
                        f.View.Detail.text.Contains("В наличии: 2") && f.View.Detail.text.Contains("Доступно: 2"),
                        "полное название, описание или количество отсутствуют справа: "+context);
                }
            }
        }
        private static void CheckCorners(RectTransform rect,Camera camera,Vector2Int size)
        {
            var corners=new Vector3[4];rect.GetWorldCorners(corners);
            foreach(var corner in corners) {var point=camera.WorldToViewportPoint(corner);Require(point.x>=0 && point.y>=0 && point.x<=1 && point.y<=1,$"поле одежды вне кадра {size}: {rect.name}, point={point}");}
        }
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException("CosmeticInventoryUiChecks: "+reason);}
    }
}
