using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    // Explicit Editor fixtures exercise the saved HUD with actual inventory rules.
    // They do not author resources or participate in normal runtime updates.
    public static class SelectedItemHudChecks
    {
        private const int RenderLayer=30;
        private sealed class Fixture : IDisposable
        {
            public readonly GameObject Root;
            public readonly CombatHud Hud;
            public readonly CombatHudView View;
            public readonly InventoryArtwork Artwork=new();
            public readonly List<string> Calls=new();
            public ActorState Actor;
            public Fixture(Transform parent=null)
            {
                Root=new GameObject("Проверка выбранной вещи HUD",typeof(RectTransform));
                if(parent!=null)
                {
                    Root.transform.SetParent(parent,false);
                    var rect=(RectTransform)Root.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;
                    rect.sizeDelta=Vector2.zero;rect.anchoredPosition=Vector2.zero;
                }
                Hud=new CombatHud(Root.transform,id=>Calls.Add("legacy:"+id),id=>Calls.Add("item:"+id),slot=>Calls.Add(slot));
                View=Root.GetComponentInChildren<CombatHudView>(true);View.Validate();
                Actor=new ActorState {id="selected-hud-player",character="Персонаж проверки",owner="selected-hud-owner",
                    player=true,connected=true,level="factory:0",health=100,mana=100,weapon="pistol"};
                PlayerStarterInventory.Give(Actor);
            }
            public SelectedItemHudView Selected=>View.SelectedItem;
            public ItemStack Item(string definition)=>Actor.inventory.Single(item=>item.definition==definition);
            public void Refresh(float? reload=null,bool visible=true)
            {
                string before=StateJson.ToJson(Actor);
                Hud.Refresh(Actor,visible,reload);
                Require(StateJson.ToJson(Actor)==before,"Refresh изменил здоровье, выбор, магазин, резерв, receipts или неизвестные данные актёра.");
            }
            public void Dispose()=>Object.DestroyImmediate(Root);
        }

        public static void Run()
        {
            WeaponCatalog.Load();
            using var fixture=new Fixture();var selected=fixture.Selected;
            Require(ReferenceEquals(selected.Counter,fixture.View.Ammunition) && selected.transform.IsChildOf(fixture.View.Loadout.transform),
                "Новый блок потерял прежнюю ссылку Ammunition или находится вне Loadout.");
            fixture.Actor.extensions=new Dictionary<string,JToken> { ["selected-hud-mod"]=StateJson.FromJson<JToken>("{\"exact\":0.1234567890123456789012345678}") };
            for(int i=0;i<3;i++) Require(WeaponAmmunition.SpendShot(fixture.Actor,WeaponCatalog.Get("pistol"),out _),"Не выполнен обычный расход патрона.");
            fixture.Refresh();
            CheckSelection(fixture,"pistol","p10","9 / 12    Запас: 36","R — перезарядить");
            fixture.Refresh(1.2f);
            Require(selected.ActionState.text=="Перезарядка · "+1.2f.ToString("0.0")+" с" &&
                selected.Counter.text=="9 / 12    Запас: 36","Перезарядка скрыла настоящий магазин/запас или попала в числовое поле.");
            CheckShortcuts(fixture);

            var ammo=fixture.Item("p10");
            Require(HeldItemSelection.Select(fixture.Actor,ammo.id,out _),"Не выбраны обычные патроны.");
            fixture.Actor.inventoryReservations[ammo.id]=4;fixture.Refresh();
            CheckSelection(fixture,"p10",null,"В копытах: 32","ЛКМ — бросить 1 · Shift+ЛКМ — стопку");
            Require(InventoryJournal.Debit(fixture.Actor,"selected-hud-one",ammo.id,1),"Не выполнен реальный расход одной единицы стопки.");
            fixture.Refresh();Require(selected.Counter.text=="В копытах: 31","Расход той же стопки оставил прежний счётчик.");
            fixture.Actor.inventoryReservations[ammo.id]=ammo.count;fixture.Refresh();
            CheckSelection(fixture,"p10",null,"В копытах: 0","Вещь зарезервирована для передачи…");
            Require(selected.Icon.color==new Color(.5f,.5f,.5f,1) && fixture.View.Weapons.All(button=>!button.interactable),
                "Полный резерв не показан недоступным или быстрые клавиши игнорируют pending.");

            fixture.Actor.inventoryReservations.Clear();
            var grenade=fixture.Item("grenade");Require(WeaponInventory.Select(fixture.Actor,grenade.id,out _),"Не выбрана настоящая стопка гранат.");
            fixture.Actor.inventoryReservations[grenade.id]=2;fixture.Refresh();
            CheckSelection(fixture,"grenade",null,"В стопке: 3","ЛКМ — бросить");
            fixture.Actor.inventoryReservations.Clear();
            Require(WeaponInventory.Select(fixture.Actor,fixture.Item("pistol").id,out _),"Не возвращён экземпляр пистолета.");
            var pistol=fixture.Item("pistol");fixture.Actor.inventoryReservations[pistol.id]=pistol.count;fixture.Refresh(1.2f);
            CheckSelection(fixture,"pistol","p10","9 / 12    Запас: 35","Вещь зарезервирована для передачи…");
            Require(selected.Icon.color==new Color(.5f,.5f,.5f,1) && selected.AmmoIcon.color==selected.Icon.color,
                "Резерв экземпляра оружия не затемняет обе иконки или проигрывает reload hint.");
            fixture.Actor.inventoryReservations.Clear();
            fixture.Actor.down=true;fixture.Refresh();
            CheckSelection(fixture,"pistol","p10","9 / 12    Запас: 35","Без сознания · нужна помощь");
            Require(selected.Icon.color==new Color(.5f,.5f,.5f,1) && selected.AmmoIcon.color==selected.Icon.color &&
                fixture.View.Weapons.All(button=>!button.interactable),"Нокаут не блокирует старые кнопки/не затемняет оба настоящих изображения.");

            // Snapshot replacement has the same actor and item IDs, but new objects/state.
            fixture.Actor=fixture.Actor.Copy();fixture.Actor.down=false;
            Require(WeaponAmmunition.CompleteReload(fixture.Actor,WeaponCatalog.Get("pistol")),"Не выполнена обычная перезарядка заменённого ActorState.");
            fixture.Refresh();CheckSelection(fixture,"pistol","p10","12 / 12    Запас: 32","");
            CheckCachedRefresh(fixture);
            CheckUnsupportedVersion(fixture);
            CheckMissingWeaponInstance(fixture);
            Require(WeaponInventory.Select(fixture.Actor,null,out _),"Не очищен настоящий выбор.");fixture.Refresh();
            Require(selected.ItemName.text=="Оружие не выбрано" && selected.Counter.text=="" && selected.ActionState.text=="" &&
                !selected.Icon.enabled && selected.Icon.sprite==null && !selected.AmmoIcon.enabled && selected.AmmoIcon.sprite==null,
                "Пустой выбор сохраняет предыдущие название, числа или изображения.");
            ammo=fixture.Item("p10");Require(HeldItemSelection.Select(fixture.Actor,ammo.id,out _),"Не выбран остаток обычной стопки.");
            Require(InventoryJournal.Debit(fixture.Actor,"selected-hud-last",ammo.id,ammo.count),"Не списана последняя единица выбранного остатка.");
            fixture.Refresh();Require(selected.ItemName.text=="Предмет закончился" && selected.Counter.text=="" && selected.Icon.sprite==null &&
                selected.ActionState.text=="Предмет закончился · выберите другой","Исчезнувшая живая стопка заменена старым cached item.");
            CheckUnknown(fixture);
            fixture.Hud.Refresh(null,false);
            Require(!fixture.View.Loadout.activeSelf && !fixture.View.Vitals.activeSelf && selected.ItemName.text=="" && selected.Counter.text=="" &&
                selected.ActionState.text=="" && selected.Icon.sprite==null && selected.AmmoIcon.sprite==null,"Выход/actor null сохраняет картинку выбранной вещи.");
            CheckBrokenBindings(selected);
            Debug.Log("Выбранная вещь HUD: PASS — реальные магазины/расход/reload, резервы стопок, 12 прежних callbacks, гранаты/нокаут, snapshot replacement, отсутствие gameplay mutations, кеш и явный пропуск неизвестной иконки.");
        }

        private static void CheckSelection(Fixture fixture,string definition,string ammo,string counter,string action)
        {
            var view=fixture.Selected;
            Require(view.ItemName.text==ItemTextCatalog.Load().Name(definition,"SelectedItemHudChecks") &&
                view.Icon.enabled && view.Icon.sprite==fixture.Artwork.Require(definition) && view.Counter.text==counter && view.ActionState.text==action,
                $"Выбор не отражает actual inventory: definition={definition}, name={view.ItemName.text}, counter={view.Counter.text}, action={view.ActionState.text}.");
            Require(ammo==null?!view.AmmoIcon.enabled && view.AmmoIcon.sprite==null:
                view.AmmoIcon.enabled && view.AmmoIcon.sprite==fixture.Artwork.Require(ammo),"Иконка боеприпаса не соответствует выбранному WeaponCatalog.ammo: "+definition);
        }
        private static void CheckShortcuts(Fixture fixture)
        {
            var ammo=fixture.Item("p10");
            foreach(string key in ItemShortcuts.Keys) Require(ItemShortcuts.Assign(fixture.Actor,key,ammo.id,out _),"Не назначен существующий shortcut: "+key);
            fixture.Refresh();string before=StateJson.ToJson(fixture.Actor);int start=fixture.Calls.Count;
            for(int i=0;i<ItemShortcuts.Keys.Count;i++)
            {
                Require(fixture.View.Weapons[i].interactable,"Доступная стопка заблокировала shortcut: "+ItemShortcuts.Keys[i]);
                fixture.View.Weapons[i].onClick.Invoke();
                Require(fixture.Calls.Count==start+i+1 && fixture.Calls[^1]==ItemShortcuts.Keys[i],"Старый raw shortcut callback потерян/дублирован/подменён.");
            }
            Require(StateJson.ToJson(fixture.Actor)==before,"Отображение/callback HUD сам расходует или выбирает предмет.");
        }
        private static void CheckCachedRefresh(Fixture fixture)
        {
            int dirty=0;UnityEngine.Events.UnityAction callback=()=>dirty++;
            Graphic[] graphics={fixture.Selected.Icon,fixture.Selected.AmmoIcon,fixture.Selected.ItemName,fixture.Selected.Counter,fixture.Selected.ActionState};
            foreach(var graphic in graphics) {graphic.RegisterDirtyLayoutCallback(callback);graphic.RegisterDirtyVerticesCallback(callback);}
            int children=fixture.Root.GetComponentsInChildren<Transform>(true).Length;var icon=fixture.Selected.Icon.sprite;var ammo=fixture.Selected.AmmoIcon.sprite;
            try
            {
                for(int i=0;i<20;i++) fixture.Refresh();
                Require(dirty==0 && children==fixture.Root.GetComponentsInChildren<Transform>(true).Length &&
                    fixture.Selected.Icon.sprite==icon && fixture.Selected.AmmoIcon.sprite==ammo,"Неизменный Refresh пересоздаёт UI/изображения или пачкает layout выбранного блока.");
            }
            finally {foreach(var graphic in graphics) {graphic.UnregisterDirtyLayoutCallback(callback);graphic.UnregisterDirtyVerticesCallback(callback);}}
        }
        private static void CheckUnknown(Fixture fixture)
        {
            string id="checks.selected-hud.unknown-"+Guid.NewGuid().ToString("N");
            var item=new ItemStack {id="unknown-selected-instance",definition=id,count=2};fixture.Actor.inventory.Add(item);
            Require(HeldItemSelection.Select(fixture.Actor,item.id,out _),"Не выбрана сохранённая модовая обычная вещь.");
            var warnings=new List<string>();Application.LogCallback handler=(message,trace,type)=>{if(type==LogType.Warning && message.Contains(id)) warnings.Add(message);};
            Application.logMessageReceived+=handler;
            try
            {
                fixture.Refresh();int count=warnings.Count;
                Require(count==2 && warnings.Any(message=>message.Contains("иконки")) && warnings.Any(message=>message.Contains("context=HUD выбранной вещи")) &&
                    fixture.Selected.ItemName.text=="["+id+"]" && !fixture.Selected.Icon.enabled && fixture.Selected.Icon.sprite==null &&
                    fixture.Selected.Counter.text=="В копытах: 2","Неизвестная вещь скрыта молча или заменена чужой картинкой/текстом.");
                for(int i=0;i<20;i++) fixture.Refresh();Require(warnings.Count==count,"Повторный кадр спамит одним неизвестным ID.");
            }
            finally {Application.logMessageReceived-=handler;}
        }
        private static void CheckBrokenBindings(SelectedItemHudView view)
        {
            var original=view.AmmoIcon;view.AmmoIcon=view.Icon;
            try {view.Validate();throw new Exception("Одинаковые изображения Item/Ammo приняты.");}
            catch(InvalidOperationException error) {Require(error.Message.Contains("HUD выбранной вещи"),"Нет контекста ошибочных HUD bindings.");}
            finally {view.AmmoIcon=original;}
        }
        private static void CheckUnsupportedVersion(Fixture fixture)
        {
            int previous=fixture.Actor.weaponInventoryVersion;fixture.Actor.weaponInventoryVersion=2;
            var warnings=new List<string>();Application.LogCallback handler=(message,trace,type)=>
            {if(type==LogType.Warning && message.Contains("HUD выбранной вещи: неподдерживаемая версия")) warnings.Add(message);};
            Application.logMessageReceived+=handler;
            try
            {
                fixture.Refresh();
                Require(warnings.Count==1 && warnings[0].Contains("actor="+fixture.Actor.id) && warnings[0].Contains("version=2") &&
                    fixture.Selected.ItemName.text=="Неподдерживаемый инвентарь" && fixture.Selected.Counter.text=="" &&
                    fixture.Selected.ActionState.text=="Ошибка версии инвентаря — см. журнал." &&
                    fixture.Selected.Icon.sprite==null && fixture.Selected.AmmoIcon.sprite==null,
                    "Будущая версия инвентаря подставлена из старого weapon или скрыта без contextual warning.");
                Require(fixture.View.Weapons.Length==12 && fixture.View.Weapons.All(button=>!button.interactable),
                    "Будущая версия инвентаря оставляет действующие legacy buttons вместо явного отказа.");
                int calls=fixture.Calls.Count;string before=StateJson.ToJson(fixture.Actor);
                foreach(var button in fixture.View.Weapons) button.onClick.Invoke();
                Require(fixture.Calls.Count==calls && StateJson.ToJson(fixture.Actor)==before,
                    "Callback неизвестной версии вызывает старый выбор/действие или изменяет сохранённого актёра.");
                for(int i=0;i<20;i++) fixture.Refresh();Require(warnings.Count==1,"Ошибка версии инвентаря повторяется каждый кадр.");
            }
            finally {Application.logMessageReceived-=handler;fixture.Actor.weaponInventoryVersion=previous;}
            fixture.Refresh();CheckSelection(fixture,"pistol","p10","12 / 12    Запас: 32","");
        }
        private static void CheckMissingWeaponInstance(Fixture fixture)
        {
            var original=fixture.Actor;fixture.Actor=original.Copy();var pistol=fixture.Item("pistol");
            Require(InventoryJournal.Debit(fixture.Actor,"selected-hud-missing-instance",pistol.id,pistol.count),"Не удалён настоящий выбранный экземпляр для malformed fixture.");
            // Explicit malformed Editor fixture: the stable selection remains, but its stack and legacy definition are absent.
            fixture.Actor.weapon=null;
            Require(fixture.Actor.equippedWeaponItem==pistol.id,"Fixture неожиданно очистил ссылку на исчезнувший экземпляр.");
            var warnings=new List<string>();Application.LogCallback handler=(message,trace,type)=>
            {if(type==LogType.Warning && message.Contains("HUD выбранной вещи: нет выбранного экземпляра")) warnings.Add(message);};
            Application.logMessageReceived+=handler;
            try
            {
                fixture.Refresh();
                Require(warnings.Count==1 && warnings[0].Contains("actor="+fixture.Actor.id) && warnings[0].Contains("item="+pistol.id) &&
                    fixture.Selected.ItemName.text=="Оружие не выбрано" && fixture.Selected.Counter.text=="" && fixture.Selected.ActionState.text=="" &&
                    !fixture.Selected.Icon.enabled && fixture.Selected.Icon.sprite==null && !fixture.Selected.AmmoIcon.enabled && fixture.Selected.AmmoIcon.sprite==null,
                    "Исчезнувший equippedWeaponItem при weapon=null скрыт молча или оставляет cached изображение/магазин.");
                for(int i=0;i<20;i++) fixture.Refresh();Require(warnings.Count==1,"Один отсутствующий экземпляр предупреждает каждый кадр.");
            }
            finally {Application.logMessageReceived-=handler;fixture.Actor=original;}
            fixture.Refresh();CheckSelection(fixture,"pistol","p10","12 / 12    Запас: 32","");
        }

        public static void Render()
        {
            WeaponCatalog.Load();
            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/selected-item-hud-r67"));Directory.CreateDirectory(directory);
            foreach(var size in new[]{new Vector2Int(1024,768),new Vector2Int(1280,720),new Vector2Int(1920,1080)}) RenderSize(size,directory);
            Debug.Log("Выбранная вещь HUD: макеты PASS — actual saved sprites/text, native Canvas Expand 1024/1280/1920, непересекающиеся 12 слотов/selected band, полные русские строки, PNG+JSON без сохранения тестовой сцены.");
        }
        private static void RenderSize(Vector2Int size,string directory)
        {
            var root=new GameObject("Макет выбранной вещи HUD",typeof(RectTransform));var cameraObject=new GameObject("Камера выбранной вещи HUD");
            var camera=cameraObject.AddComponent<Camera>();camera.transform.position=new Vector3(0,0,-10);camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color(.03f,.04f,.035f,1);camera.cullingMask=1<<RenderLayer;
            var target=new RenderTexture(size.x,size.y,24);target.Create();camera.targetTexture=target;
            var previous=RenderTexture.active;Texture2D pixels=null;
            try
            {
                var canvas=root.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
                var scaler=root.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);
                scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
                using var fixture=new Fixture(root.transform);
                foreach(var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer=RenderLayer;
                fixture.Refresh();Canvas.ForceUpdateCanvases();CombatHudRender.RenderReserveCamera(camera,target);Canvas.ForceUpdateCanvases();
                var center=camera.WorldToViewportPoint(((RectTransform)root.transform).TransformPoint(((RectTransform)root.transform).rect.center));
                Require(Mathf.Abs(center.x-.5f)<.001f && Mathf.Abs(center.y-.5f)<.001f && Mathf.Abs(center.z-1)<.001f,"Камера не подготовила реальную плоскость Canvas: "+center);
                foreach(string state in new[]{"pistol-reload","held-p10","grenade","down"})
                {
                    ConfigureRenderState(fixture,state);fixture.Refresh(state=="pistol-reload"?1.2f:(float?)null);
                    Canvas.ForceUpdateCanvases();CombatHudRender.RenderReserveCamera(camera,target);Canvas.ForceUpdateCanvases();CheckLayout(fixture,camera,size,state);
                    pixels=new Texture2D(size.x,size.y,TextureFormat.RGBA32,false);RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,size.x,size.y),0,0);pixels.Apply();
                    Require(pixels.GetPixels32().Select(value=>((uint)value.r<<24)|((uint)value.g<<16)|((uint)value.b<<8)|value.a).Distinct().Take(5).Count()==5,
                        "GPU кадр HUD одноцветный/пустой: "+size+"/"+state);
                    string path=Path.Combine(directory,$"selected-{state}-{size.x}x{size.y}.png");File.WriteAllBytes(path,pixels.EncodeToPNG());
                    WriteEvidence(path,fixture,camera,size,state);Object.DestroyImmediate(pixels);pixels=null;
                }
            }
            finally
            {
                RenderTexture.active=previous;if(pixels!=null) Object.DestroyImmediate(pixels);
                camera.targetTexture=null;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(root);Object.DestroyImmediate(cameraObject);
            }
        }
        private static void ConfigureRenderState(Fixture fixture,string state)
        {
            fixture.Actor.down=false;fixture.Actor.inventoryReservations.Clear();
            if(state=="held-p10")
            {var item=fixture.Item("p10");Require(HeldItemSelection.Select(fixture.Actor,item.id,out _),"Нет ordinary render selection.");fixture.Actor.inventoryReservations[item.id]=4;}
            else Require(WeaponInventory.Select(fixture.Actor,fixture.Item(state=="grenade"?"grenade":"pistol").id,out _),"Нет weapon render selection.");
            if(state=="pistol-reload") for(int i=0;i<3;i++) Require(WeaponAmmunition.SpendShot(fixture.Actor,WeaponCatalog.Get("pistol"),out _),"Нет actual render расхода.");
            if(state=="down") fixture.Actor.down=true;
        }
        private static void CheckLayout(Fixture fixture,Camera camera,Vector2Int size,string state)
        {
            string context=size+"/"+state;var loadout=(RectTransform)fixture.View.Loadout.transform;var selected=(RectTransform)fixture.Selected.transform;
            Require(Mathf.Abs(loadout.rect.width-800)<.1f && Mathf.Abs(loadout.rect.height-108)<.1f && Mathf.Abs(loadout.localScale.x-.8f)<.001f,
                "Сохранённая компактная область Loadout не 800×108/scale0.8: "+context+"/"+loadout.rect+"/"+loadout.localScale);
            Require(Mathf.Abs(selected.rect.width-772)<.1f && Mathf.Abs(selected.rect.height-50)<.1f &&
                fixture.Selected.ItemName.fontSize>=18 && fixture.Selected.Counter.fontSize>=18 && fixture.Selected.ActionState.fontSize>=15,
                "Selected band не 772×50 или читаемость снижена вместо сохранения границ: "+context);
            var graphics=new Graphic[] {fixture.Selected.Icon,fixture.Selected.AmmoIcon,fixture.Selected.ItemName,fixture.Selected.Counter,
                fixture.Selected.ActionState,fixture.View.GrenadeIcon,fixture.View.Grenades};
            foreach(var graphic in graphics)
            {
                var rect=graphic.rectTransform;var screen=ScreenRect(rect,camera);
                Require(screen.xMin>=0 && screen.yMin>=0 && screen.xMax<=size.x && screen.yMax<=size.y,"Selected graphic вне экрана: "+context+"/"+graphic.name+"/"+screen);
                Require(Contains(ScreenRect(selected,camera),screen),"Selected graphic вне authored selected band: "+context+"/"+graphic.name);
                if(graphic is Text label) Require(label.fontSize>=13 && !label.resizeTextForBestFit &&
                    label.preferredHeight<=rect.rect.height+.1f && label.preferredWidth<=rect.rect.width+.1f,
                    $"Текст выбранной вещи обрезан: {context}/{label.name}, font={label.fontSize}, rect={rect.rect}, preferred={label.preferredWidth}/{label.preferredHeight}, text={label.text}.");
            }
            for(int i=0;i<graphics.Length;i++) for(int j=i+1;j<graphics.Length;j++)
                Require(!ScreenRect(graphics[i].rectTransform,camera).Overlaps(ScreenRect(graphics[j].rectTransform,camera)),"Selected graphics перекрываются: "+context+"/"+graphics[i].name+"/"+graphics[j].name);
            var band=ScreenRect(selected,camera);
            foreach(var button in fixture.View.Weapons)
            {
                var transform=(RectTransform)button.transform;var rect=ScreenRect(transform,camera);
                Require(Mathf.Abs(transform.rect.width-62)<.1f && Mathf.Abs(transform.rect.height-38)<.1f,"Изменена доступная область одного из 12 быстрых слотов: "+context+"/"+button.name);
                Require(Contains(ScreenRect(loadout,camera),rect) && !rect.Overlaps(band),"12 shortcut band перекрывается с selected block: "+context+"/"+button.name);
            }
            foreach(var rect in fixture.Hud.LayoutReservations) Require(Contains(new Rect(0,0,size.x,size.y),ScreenRect(rect,camera)),"HUD reservation вне кадра: "+context+"/"+rect.name);
            Require(!band.Overlaps(ScreenRect((RectTransform)fixture.View.Vitals.transform,camera)),"Выбранная вещь перекрывает HP/ману: "+context);
        }
        private static Rect ScreenRect(RectTransform rect,Camera camera)
        {
            var corners=new Vector3[4];rect.GetWorldCorners(corners);var screen=corners.Select(camera.WorldToScreenPoint).ToArray();
            foreach(var point in screen) Require(float.IsFinite(point.x) && float.IsFinite(point.y) && point.z>0,"Не конечная/невидимая граница HUD: "+rect.name);
            return Rect.MinMaxRect(screen.Min(point=>point.x),screen.Min(point=>point.y),screen.Max(point=>point.x),screen.Max(point=>point.y));
        }
        private static bool Contains(Rect outer,Rect inner)=>inner.xMin>=outer.xMin-.1f && inner.yMin>=outer.yMin-.1f && inner.xMax<=outer.xMax+.1f && inner.yMax<=outer.yMax+.1f;
        [Serializable] private sealed class FrameEvidence
        {
            public int schema=1,width,height;public bool explicitEditorFixture=true,originalRuntimeVerified=false;
            public string state,actorJson,itemName,counter,action,pngSha256;
            public CombatHudRender.ReserveSpriteFrame itemSprite,ammoSprite;public float[] loadoutBounds,selectedBounds,itemBounds,ammoBounds,nameBounds,counterBounds,actionBounds,grenadeIconBounds,grenadeTextBounds;
        }
        private static float[] Bounds(Rect rect)=>new[]{rect.xMin,rect.yMin,rect.xMax,rect.yMax};
        private static void WriteEvidence(string path,Fixture fixture,Camera camera,Vector2Int size,string state)
        {
            var view=fixture.Selected;
            var evidence=new FrameEvidence {width=size.x,height=size.y,state=state,actorJson=StateJson.ToJson(fixture.Actor),itemName=view.ItemName.text,counter=view.Counter.text,action=view.ActionState.text,
                pngSha256=VectorArtworkReserveManifest.Hash(path),itemSprite=view.Icon.sprite==null?null:CombatHudRender.DescribeReserveSprite(view.Icon.sprite),
                ammoSprite=view.AmmoIcon.sprite==null?null:CombatHudRender.DescribeReserveSprite(view.AmmoIcon.sprite),loadoutBounds=Bounds(ScreenRect((RectTransform)fixture.View.Loadout.transform,camera)),
                selectedBounds=Bounds(ScreenRect((RectTransform)view.transform,camera)),itemBounds=Bounds(ScreenRect(view.Icon.rectTransform,camera)),ammoBounds=Bounds(ScreenRect(view.AmmoIcon.rectTransform,camera)),
                nameBounds=Bounds(ScreenRect(view.ItemName.rectTransform,camera)),counterBounds=Bounds(ScreenRect(view.Counter.rectTransform,camera)),actionBounds=Bounds(ScreenRect(view.ActionState.rectTransform,camera)),
                grenadeIconBounds=Bounds(ScreenRect(fixture.View.GrenadeIcon.rectTransform,camera)),grenadeTextBounds=Bounds(ScreenRect(fixture.View.Grenades.rectTransform,camera))};
            File.WriteAllText(Path.ChangeExtension(path,".json"),StateJson.ToJson(evidence,true));
        }
        private static void Require(bool value,string reason) {if(!value)throw new InvalidOperationException("Выбранная вещь HUD: "+reason);}
    }
}
