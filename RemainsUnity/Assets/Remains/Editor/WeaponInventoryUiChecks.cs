using System;
using System.IO;
using Remains.Content;
using Remains.Domain;
using Remains.Networking;
using Remains.Presentation;
using UnityEngine;
using UnityEngine.UI;
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
            RenderTexture target=null;Texture2D image=null;var previous=RenderTexture.active;
            try
            {
                var session=root.AddComponent<CoopSession>();session.ConfigureRules(WeaponCatalog.Rules);
                var actor=new ActorState {id="ui-weapons",owner="ui",character="Проверка инвентаря",weaponInventoryVersion=1,weapon=null};
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
                var window=InventoryWindowView.Create(canvas.transform);window.Personal.SetActive(true);window.Shared.SetActive(false);
                var rect=window.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);rect.anchoredPosition=Vector2.zero;
                var view=window.PersonalContents;
                Action<string> select=id=>{if(!WeaponInventory.Select(actor,id,out var reason)) throw new InvalidOperationException(reason);};
                var panel=new PersonalInventoryPanel(view,session,id=>id switch {"pistol"=>"10-мм пистолет","spear"=>"Копьё","crowbar"=>"Лом","grenade"=>"Граната",_=>"Аптечка"},()=>0,
                    _=>throw new InvalidOperationException("UI экземпляра вызвал старый выбор определения."),new InventoryArtwork(),select);
                panel.Refresh(null);view.Rows[1].onClick.Invoke();
                Require(view.Rows[0].GetComponentInChildren<Text>().text.Contains("7/12") &&
                    view.Rows[1].GetComponentInChildren<Text>().text.Contains("2/12"),"Строки не различают боезапас экземпляров");
                Require(view.Use.interactable && view.WeaponDetail.text.Contains("В магазине: 2"),"Нет действия/магазина выбранного экземпляра");
                Require(view.WeaponDetail.text.Contains($"Урон без движения: {session.Rules.Damage.Calculate(b.definition,0):0.##}") &&
                    view.WeaponDetail.text.Contains($"Интервал атак: {WeaponCatalog.Get(b.definition).cooldown:0.##} с"),
                    "Описание не показывает характеристики текущего оружия");
                view.Use.onClick.Invoke();Require(actor.equippedWeaponItem==b.id,"Выбран первый пистолет вместо второго");
                view.Weapons[0].onClick.Invoke();Require(WeaponInventory.Shortcut(actor,"1")?.id==b.id,"Назначение не сохранило instance ID");
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
                Require(panel.Page==1 && !view.Next.interactable,"Страницы считаются по нефильтрованному списку");
                actor.inventory.RemoveAll(item=>item.id.StartsWith("filter-page-",StringComparison.Ordinal));panel.Refresh(null);
                Require(panel.Page==0 && !view.Previous.interactable,"Исчезнувшая страница не зажата после удаления предметов");
                var customAmmoFilter=new InventoryItemFilter(new[]{new WeaponDefinition {id="test",ammo="custom-ammo"}});
                Require(customAmmoFilter.Includes(new ItemStack {definition="custom-ammo"},InventoryCategory.Ammunition) &&
                    customAmmoFilter.Includes(new ItemStack {definition="custom-item"},InventoryCategory.Items),"Фильтр требует встроенные ID предметов");
                view.Categories[(int)InventoryCategory.All].onClick.Invoke();view.Rows[1].onClick.Invoke();
                if(render)
                {
                    var camera=new GameObject("Камера окна").AddComponent<Camera>();camera.transform.SetParent(root.transform);camera.transform.position=new Vector3(0,0,-10);camera.backgroundColor=new Color(.06f,.07f,.08f);
                    target=new RenderTexture(1920,1080,24);target.Create();camera.targetTexture=target;canvas.worldCamera=camera;canvas.planeDistance=1;
                    image=new Texture2D(1920,1080,TextureFormat.RGBA32,false);
                    foreach(int row in new[]{1,2,3,4,5})
                    {
                        view.Rows[row].onClick.Invoke();Canvas.ForceUpdateCanvases();
                        Require(view.WeaponDetail.preferredHeight<=view.WeaponDetail.rectTransform.rect.height+.1f,"Описание оружия обрезается");
                        camera.Render();RenderTexture.active=target;
                        image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();
                        string suffix=row==1?"":"-"+actor.inventory[row].definition;
                        File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/weapon-inventory-ui"+suffix+".png")),image.EncodeToPNG());
                    }
                }
            }
            finally
            {
                RenderTexture.active=previous;
                if(image!=null) UnityEngine.Object.DestroyImmediate(image);
                if(target!=null) {target.Release();UnityEngine.Object.DestroyImmediate(target);}
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException(message);}
    }
}
