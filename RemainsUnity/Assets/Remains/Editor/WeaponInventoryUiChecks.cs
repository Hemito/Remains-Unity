using System;
using System.IO;
using Remains.Content;
using Remains.Domain;
using Remains.Networking;
using Remains.Gameplay;
using Remains.Presentation;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
namespace Remains.Editor
{
    public static class WeaponInventoryUiChecks
    {
        public static void Run()=>Check(false);
        public static void Render()=>Check(true);
        public static void BuildAndRender() {PrototypeBuild.Build();Render();}
        private static void Check(bool render)
        {
            WeaponCatalog.Load();
            var root=new GameObject("Проверка предметов оружия");
            RenderTexture target=null;Texture2D image=null;Camera camera=null;var previous=RenderTexture.active;
            try
            {
                var session=root.AddComponent<CoopSession>();session.ConfigureRules(WeaponCatalog.Rules);
                var actor=new ActorState {id="ui-weapons",owner="ui",player=true,connected=true,character="Проверка инвентаря",weaponInventoryVersion=1,weapon=null};
                var a=new ItemStack {id="first-pistol",definition="pistol",count=1,weaponState=new WeaponItemState {rounds=7}};
                var b=new ItemStack {id="second-pistol",definition="pistol",count=1,weaponState=new WeaponItemState {rounds=2}};
                actor.inventory.Add(a);actor.inventory.Add(b);actor.inventory.Add(SharedInventory.Create("heal",3));
                actor.inventory.Add(new ItemStack {id="spear",definition="spear",count=1,weaponState=new WeaponItemState()});
                actor.inventory.Add(new ItemStack {id="crowbar",definition="crowbar",count=1,weaponState=new WeaponItemState()});
                actor.inventory.Add(new ItemStack {id="grenades",definition="grenade",count=3});
                var world=new WorldState();world.actors.Add(actor);
                typeof(CoopSession).GetProperty("World").SetValue(session,world);
                typeof(CoopSession).GetProperty("LocalActorId").SetValue(session,actor.id);
                typeof(CoopSession).GetProperty("LocalOwner").SetValue(session,actor.owner);
                var canvas=new GameObject("Окно",typeof(RectTransform)).AddComponent<Canvas>();canvas.transform.SetParent(root.transform);
                canvas.renderMode=RenderMode.ScreenSpaceCamera;
                var scaler=canvas.gameObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);
                var window=InventoryWindowView.Create(canvas.transform);window.Personal.SetActive(true);
                var rect=window.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);rect.anchoredPosition=Vector2.zero;
                var view=window.PersonalContents;
                Action<string> select=id=>{if(!WeaponInventory.Select(actor,id,out var reason)) throw new InvalidOperationException(reason);};
                var itemActions=new ItemShortcutActions(session,select);
                var panel=new PersonalInventoryPanel(view,session,id=>id switch {"pistol"=>"10-мм пистолет","spear"=>"Копьё","crowbar"=>"Лом","grenade"=>"Граната",_=>"Аптечка"},()=>0,
                    _=>throw new InvalidOperationException("UI экземпляра вызвал старый выбор определения."),new InventoryArtwork(),select,itemActions:()=>itemActions);
                panel.Refresh(null);view.Rows[1].onClick.Invoke();
                Require(view.Rows[1].colors.normalColor!=view.Rows[0].colors.normalColor,"Выбранная строка не отличается от соседней");
                Require(view.Rows[0].GetComponentInChildren<Text>().text.Contains("7/12") &&
                    view.Rows[1].GetComponentInChildren<Text>().text.Contains("2/12"),"Строки не различают боезапас экземпляров");
                Require(view.Use.interactable && view.WeaponDetail.text.Contains("В магазине: 2"),"Нет действия/магазина выбранного экземпляра");
                Require(view.WeaponDetail.text.Contains($"Урон без движения: {session.Rules.Damage.Calculate(b.definition,0):0.##}") &&
                    view.WeaponDetail.text.Contains($"Интервал атак: {WeaponCatalog.Get(b.definition).cooldown:0.##} с"),
                    "Описание не показывает характеристики текущего оружия");
                view.Use.onClick.Invoke();Require(actor.equippedWeaponItem==b.id,"Выбран первый пистолет вместо второго");
                view.Weapons[0].onClick.Invoke();Require(WeaponInventory.Shortcut(actor,"1")?.id==b.id,"Назначение не сохранило instance ID");
                view.Rows[2].onClick.Invoke();view.Weapons[11].onClick.Invoke();
                Require(ItemShortcuts.Get(actor,"=")?.definition=="heal" && actor.equippedWeaponItem==b.id,"Назначение аптечки не сохранило экземпляр или сменило оружие");
                view.Rows[1].onClick.Invoke();
                var hudRoot=new GameObject("Проверка быстрых ссылок",typeof(RectTransform));hudRoot.transform.SetParent(root.transform);
                string clicked=null;var hud=new CombatHud(hudRoot.transform,_=>throw new InvalidOperationException("HUD вызвал старый выбор."),id=>clicked=id);
                hud.Refresh(actor,true);var hudView=hudRoot.GetComponentInChildren<CombatHudView>();hudView.Weapons[0].onClick.Invoke();
                Require(clicked==b.id && !hudView.Weapons[1].interactable,"HUD выбирает определение или разрешает пустой слот");
                view.Use.onClick.Invoke();Require(actor.weapon==null && actor.equippedWeaponItem==null,"Кнопка не убрала оружие");
                view.Use.onClick.Invoke();panel.Refresh(null);hudRoot.SetActive(false);
                foreach(int row in new[]{3,4})
                {
                    view.Rows[row].onClick.Invoke();
                    Require(!view.WeaponDetail.text.Contains("магазине") && view.WeaponDetail.text.Contains("Ближний бой"),"Ближнему оружию показан магазин");
                }
                view.Rows[5].onClick.Invoke();view.Use.onClick.Invoke();
                Require(actor.weapon=="grenade" && actor.equippedWeaponItem=="grenades" && actor.inventory[5].weaponState==null &&
                    actor.inventory[5].count==3 && view.WeaponDetail.text.Contains("В стопке: 3"),"Инвентарь неверно выбирает или описывает стопку гранат");
                view.Rows[1].onClick.Invoke();
                string inventoryBefore=Remains.Serialization.StateJson.ToJson(actor.inventory);
                string actorBeforeArmor=Remains.Serialization.StateJson.ToJson(actor);
                view.Categories[(int)InventoryCategory.Armor].onClick.Invoke();
                Require(!view.Rows[0].gameObject.activeSelf && !view.Detail.gameObject.activeSelf && !view.ActionStatus.gameObject.activeSelf &&
                    !view.Use.gameObject.activeSelf && !view.Deposit.gameObject.activeSelf && !view.Portrait.activeSelf &&
                    !view.WeaponPreview.gameObject.activeSelf && !view.WeaponDetail.gameObject.activeSelf &&
                    !view.Weapons[0].gameObject.activeSelf && !view.Previous.gameObject.activeSelf && !view.Next.gameObject.activeSelf,
                    "Вкладка брони должна быть пустой, без заглушек и работающих механик");
                Require(Remains.Serialization.StateJson.ToJson(actor)==actorBeforeArmor,"Вкладка брони изменила персонажа");
                var armorTexts=view.GetComponentsInChildren<Text>();
                Require(armorTexts.Length==view.Categories.Length && Array.TrueForAll(armorTexts,text=>
                    Array.Exists(view.Categories,tab=>text.transform.IsChildOf(tab.transform))),
                    "На пустой вкладке брони остались подсказки или другие надписи помимо вкладок");
                view.Categories[(int)InventoryCategory.Items].onClick.Invoke();
                Require(view.Rows[0].GetComponentInChildren<Text>().text.Contains("Аптечка") && !view.Rows[1].interactable && !view.Use.interactable,
                    "Фильтр предметов оставил оружие или скрытое действие");
                view.Categories[(int)InventoryCategory.Ammunition].onClick.Invoke();
                Require(!view.Rows[0].interactable && !view.Next.interactable,"Пустая категория содержит строки");
                view.Categories[(int)InventoryCategory.Weapons].onClick.Invoke();view.Rows[1].onClick.Invoke();
                view.Use.onClick.Invoke();
                Require(actor.equippedWeaponItem==b.id && view.Rows[4].GetComponentInChildren<Text>().text.Contains("Граната"),
                    "Фильтр потерял ID второго пистолета или стопку гранат");
                Require(Remains.Serialization.StateJson.ToJson(actor.inventory)==inventoryBefore,"Фильтры изменили сам инвентарь");
                for(int i=0;i<9;i++) actor.inventory.Add(new ItemStack {id="filter-page-"+i,definition="heal",count=1});
                view.Categories[(int)InventoryCategory.Items].onClick.Invoke();view.Next.onClick.Invoke();
                Require(panel.FirstVisibleRow==2 && !view.Next.interactable,"Конец списка считается по нефильтрованным предметам");
                Require(view.Scrollbar.gameObject.activeSelf && Mathf.Abs(view.Scrollbar.size-.8f)<.001f,"Ползунок не отражает долю видимых строк");
                view.Scrollbar.value=.5f;
                Require(panel.FirstVisibleRow==1,"Перемещение ползунка не сдвинуло список");
                view.Scrollbar.value=1;
                Require(panel.FirstVisibleRow==2 && !view.Next.interactable,"Ползунок не достиг конца списка");
                string beforeScroll=Remains.Serialization.StateJson.ToJson(actor.inventory);
                void Scroll(float direction)
                {
                    var input=new PointerEventData(EventSystem.current) {scrollDelta=new Vector2(0,direction)};
                    ExecuteEvents.ExecuteHierarchy(view.Rows[0].gameObject,input,ExecuteEvents.scrollHandler);
                }
                Scroll(-1);Require(panel.FirstVisibleRow==2,"Колесо вышло за конец списка");
                Scroll(1);Require(panel.FirstVisibleRow==1 && panel.Category==InventoryCategory.Items,"Колесо не сдвинуло список ровно на строку");
                Scroll(1);Scroll(1);Require(panel.FirstVisibleRow==0,"Колесо вышло за начало списка");
                Scroll(-1);Require(panel.FirstVisibleRow==1,"Колесо над строкой не дошло до окна списка");
                view.Rows[0].onClick.Invoke();
                Require(view.Detail.text.Contains("Аптечка"),"Сдвинутая строка выбрала другой предмет");
                Require(Remains.Serialization.StateJson.ToJson(actor.inventory)==beforeScroll,"Прокрутка изменила предметы");
                actor.inventory.RemoveAll(item=>item.id.StartsWith("filter-page-",StringComparison.Ordinal));panel.Refresh(null);
                Require(panel.FirstVisibleRow==0 && !view.Previous.interactable,"Начало списка не зажато после удаления предметов");
                Require(!view.Scrollbar.gameObject.activeSelf,"Полоса прокрутки осталась у короткого списка");
                Scroll(-1);Require(panel.FirstVisibleRow==0,"Колесо открыло удалённые строки");
                var customAmmoFilter=new InventoryItemFilter(new[]{new WeaponDefinition {id="test",ammo="custom-ammo"}});
                Require(customAmmoFilter.Includes(new ItemStack {definition="custom-ammo"},InventoryCategory.Ammunition) &&
                    customAmmoFilter.Includes(new ItemStack {definition="custom-item"},InventoryCategory.Items),"Фильтр требует встроенные ID предметов");
                view.Categories[(int)InventoryCategory.All].onClick.Invoke();view.Rows[1].onClick.Invoke();
                view.Rows[2].onClick.Invoke();
                Require(!view.Use.interactable && view.ActionStatus.text==HealingRules.Rejection(actor,actor,true) &&
                    view.ActionStatus.text.Contains("полное"),"Аптечка не объясняет полное здоровье");
                actor.health=65;panel.Refresh(null);
                Require(view.Use.interactable && view.ActionStatus.text=="","Доступное лечение заблокировано отсутствием контейнера");
                actor.down=true;panel.Refresh(null);
                Require(!view.Use.interactable && view.ActionStatus.text.Contains("Без сознания"),"Нет причины запрета самолечения без сознания");
                actor.down=false;actor.health=100;panel.Refresh(null);
                if(render)
                {
                    camera=new GameObject("Камера окна").AddComponent<Camera>();camera.transform.SetParent(root.transform);camera.transform.position=new Vector3(0,0,-10);camera.backgroundColor=new Color(.06f,.07f,.08f);
                    target=new RenderTexture(1920,1080,24);target.Create();camera.targetTexture=target;canvas.worldCamera=camera;canvas.planeDistance=1;
                    image=new Texture2D(1920,1080,TextureFormat.RGBA32,false);
                    void SaveFrame(string name)
                    {
                        Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;
                        image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();
                        File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/"+name+".png")),image.EncodeToPNG());
                    }
                    foreach(int row in new[]{1,2,3,4,5})
                    {
                        view.Rows[row].onClick.Invoke();Canvas.ForceUpdateCanvases();
                        foreach(var button in view.Weapons)
                        {
                            var label=button.GetComponentInChildren<Text>();
                            Require(label.preferredWidth<=label.rectTransform.rect.width+.1f,"Обрезается подпись быстрой клавиши: "+label.text);
                        }
                        Require(view.WeaponDetail.preferredHeight<=view.WeaponDetail.rectTransform.rect.height+.1f,"Описание оружия обрезается");
                        string suffix=row==1?"":"-"+actor.inventory[row].definition;
                        SaveFrame("weapon-inventory-ui"+suffix);
                    }
                    view.Categories[(int)InventoryCategory.Armor].onClick.Invoke();SaveFrame("inventory-armor");
                    for(int i=0;i<20;i++) actor.inventory.Add(new ItemStack {id="scroll-render-"+i,definition="heal",count=i+1});
                    view.Categories[(int)InventoryCategory.Items].onClick.Invoke();view.Scrollbar.value=.5f;
                    CheckScrollbarPointer(root,canvas,camera,view,panel);
                    SaveFrame("inventory-scrollbar");
                }
            }
            finally
            {
                RenderTexture.active=previous;
                if(image!=null) UnityEngine.Object.DestroyImmediate(image);
                if(target!=null) {camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);}
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException(message);}
        private static void CheckScrollbarPointer(GameObject root,Canvas canvas,Camera camera,PersonalInventoryView view,PersonalInventoryPanel panel)
        {
            var events=new GameObject("Указатель проверки",typeof(EventSystem));events.transform.SetParent(root.transform);
            var raycaster=canvas.GetComponent<GraphicRaycaster>()??canvas.gameObject.AddComponent<GraphicRaycaster>();
            Canvas.ForceUpdateCanvases();camera.Render();
            var handle=view.Scrollbar.handleRect;
            var start=RectTransformUtility.WorldToScreenPoint(camera,handle.TransformPoint(handle.rect.center));
            var pointer=new PointerEventData(events.GetComponent<EventSystem>()) {position=start,pressPosition=start,button=PointerEventData.InputButton.Left};
            var hits=new System.Collections.Generic.List<RaycastResult>();raycaster.Raycast(pointer,hits);
            Require(hits.Count>0 && hits[0].gameObject.transform.IsChildOf(view.Scrollbar.transform),$"Ползунок закрыт для указателя: point={start}, screen={Screen.width}x{Screen.height}, depth={handle.GetComponent<Image>().depth}, hits={string.Join(",",hits.ConvertAll(hit=>hit.gameObject.name))}.");
            pointer.pointerPressRaycast=hits[0];pointer.pointerCurrentRaycast=hits[0];
            view.Rows[2].onClick.Invoke();string selectedDescription=view.Detail.text;
            ExecuteEvents.Execute(view.Scrollbar.gameObject,pointer,ExecuteEvents.initializePotentialDrag);
            ExecuteEvents.Execute(view.Scrollbar.gameObject,pointer,ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(view.Scrollbar.gameObject,pointer,ExecuteEvents.beginDragHandler);
            var track=view.Scrollbar.GetComponent<RectTransform>();
            foreach(bool bottom in new[]{true,false})
            {
                var local=new Vector2(track.rect.center.x,bottom?track.rect.yMin+1:track.rect.yMax-1);
                pointer.position=RectTransformUtility.WorldToScreenPoint(camera,track.TransformPoint(local));
                ExecuteEvents.Execute(view.Scrollbar.gameObject,pointer,ExecuteEvents.dragHandler);
                Require(bottom?!view.Next.interactable:panel.FirstVisibleRow==0,"Перетаскивание не достигло границы списка.");
                Require(view.Detail.text==selectedDescription,"Перетаскивание изменило выбранный предмет.");
            }
            ExecuteEvents.Execute(view.Scrollbar.gameObject,pointer,ExecuteEvents.endDragHandler);
            ExecuteEvents.Execute(view.Scrollbar.gameObject,pointer,ExecuteEvents.pointerUpHandler);
            view.Scrollbar.value=.5f;Canvas.ForceUpdateCanvases();
            Debug.Log("Указатель инвентаря: PASS — доступный ползунок, перетаскивание к обеим границам, сохранение выбора.");
        }
    }
}
