using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // The original's firearms connected by FirearmAuthoring: each generated definition loads with its presentation
    // (rest frame, muzzle ahead of the grip, shot and reload frames on one scale), shot artwork, sounds and magazine;
    // known numbers match the original (hunt: damage 22, 25 frames, 5 rounds, 50-frame reload, 300 px/frame);
    // formal stacks become working weapons (split per weapon); factory loot gives working ones.
    //   batch -executeMethod Remains.Editor.FirearmChecks.RunBatch
    public static class FirearmChecks
    {
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }

        public static void Run()
        {
            Anatomy.Load();WeaponCatalog.Load();ShotArtwork.Prepare();CasingEffectCatalog.Load();
            var catalog=GameAssets.Load<WeaponAssetCatalog>("Content/WeaponCatalog");
            var generated=catalog.AllAssets().Where(a=>a.Id.StartsWith(ExportedWeaponItems.Prefix,StringComparison.Ordinal) && !a.Id.StartsWith("weapon:turret",StringComparison.Ordinal) && !WeaponCatalog.IsExplosive(a.Id)).ToArray();   // turret guns: TurretChecks
            Require(generated.Length>=50,"подключено меньше 50 определений: "+generated.Length);
            string audio=GameAssets.Load<TextAsset>("Presentation/combat-audio").text;
            var exported=ExportedContentCatalog.Load();
            foreach(var asset in generated)
            {
                var weapon=WeaponCatalog.Get(asset.Id);
                Require(ExportedWeaponItems.Working(asset.Id) && exported.TryItem(asset.Id,out _),"нет предмета "+asset.Id);
                Require(weapon.projectileSpeed>0 && weapon.damage>0 && weapon.cooldown>0 && weapon.magazineSize>0 &&
                    weapon.meleeMotion==null && (weapon.rechargeSeconds>0?string.IsNullOrEmpty(weapon.ammo):weapon.ammo.StartsWith("item:",StringComparison.Ordinal) && exported.TryItem(weapon.ammo,out _)),"числа или патрон "+asset.Id);
                var rig=WeaponRigs.Get(asset.Id);
                Require(rig.HasMuzzle && WeaponRigs.PresentationId(asset.Id)==asset.Id,"нет дула или графики "+asset.Id);
                asset.ShotAnimation?.ValidateAgainst(asset.Presentation.Artwork.sprite);
                asset.ReloadAnimation?.ValidateAgainst(asset.Presentation.Artwork.sprite);
                Require(asset.ShotAnimation!=null && asset.ShotAnimation.Deferred && (weapon.rechargeSeconds>0?asset.ReloadAnimation==null:asset.ReloadAnimation!=null && asset.ReloadAnimation.Deferred),"нет кадров выстрела или перезарядки по требованию "+asset.Id);
                var restSprite=asset.Presentation.Artwork.sprite;
                for(int i=0;i<asset.ShotAnimation.FrameCount;i++) Require(asset.ShotAnimation.LoadFrameNow(i) is {} s && Mathf.Approximately(s.pixelsPerUnit,restSprite.pixelsPerUnit),"кадр выстрела "+i+" "+asset.Id);
                for(int i=0;i<(asset.ReloadAnimation?.FrameCount??0);i++) Require(asset.ReloadAnimation.LoadFrameNow(i) is {} s && Mathf.Approximately(s.pixelsPerUnit,restSprite.pixelsPerUnit),"кадр перезарядки "+i+" "+asset.Id);
                Require(ShotArtwork.TryGet(asset.Id,out _),"нет графики снаряда "+asset.Id);
                // The catalog loads only the rest frame (and the casing): animation frames are named, not referenced.
                int textures=AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(asset),true).Count(p=>AssetDatabase.GetMainAssetTypeAtPath(p)==typeof(Texture2D));
                Require(textures<=2,$"определение тянет {textures} текстур: {asset.Id}");
                Require(audio.Contains("{\"weapon\":\""+asset.Id+"\",\"clip\":\"exported:") && audio.Contains("\"weapon\":\""+asset.Id+"\",\"clip\":\"exported:sound"),"нет звука "+asset.Id);
            }
            var hunt=WeaponCatalog.Get("weapon:hunt:grade:0");
            Require(Mathf.Approximately(hunt.damage,22) && Mathf.Approximately(hunt.cooldown,25f/30) && hunt.magazineSize==5 &&
                Mathf.Approximately(hunt.reloadSeconds,50f/30) && Mathf.Approximately(hunt.projectileSpeed,300*30f/50) && hunt.ammo=="item:p32" &&
                Mathf.Approximately(hunt.shotSpreadHalfAngleDegrees,1),"охотничья винтовка по AllData");
            Require(!WeaponCatalog.Contains("weapon:p10mm:grade:0") && !WeaponCatalog.Contains("weapon:lasp:grade:0"),"ручные пистолеты не заменены");
            var assr0=WeaponCatalog.Get("weapon:assr:grade:0");var assr1=WeaponCatalog.Get("weapon:assr:grade:1");
            Require(Mathf.Approximately(assr0.damage,9) && Mathf.Approximately(assr1.damage,11) && assr1.ammo==assr0.ammo,"улучшенный вариант поверх базового");

            CheckVolley();
            CheckAmmunition(exported,hunt);

            // Formal stacks saved before the weapon was connected.
            var items=new List<ItemStack> {new() {id="a",definition="weapon:hunt:grade:0",count=2},new() {id="b",definition="weapon:knife:grade:0",count=1}};
            Require(ExportedWeaponItems.Needed(items) && ExportedWeaponItems.Activate(items)==1 && items.Count==3 &&
                items.Take(2).All(i=>i.count==1 && i.weaponState!=null && i.weaponState.rounds==0) && items[1].id=="a:2" && items[2].weaponState==null &&
                !ExportedWeaponItems.Needed(items) && WeaponInventory.IsEquippable(items[0]),"формальные стволы становятся рабочими по одному");
            // A split copy skips IDs taken by inventory aliases and keeps the stack's extensions.
            var aliased=new List<ItemStack> {new() {id="c",definition="weapon:hunt:grade:0",count=3,extensions=new Dictionary<string,Newtonsoft.Json.Linq.JToken> {["mod"]="x"}}};
            var aliases=new Dictionary<string,string>(StringComparer.Ordinal) {["c:2"]="c"};
            ExportedWeaponItems.Activate(aliased,aliases);
            Require(aliased.Select(i=>i.id).SequenceEqual(new[]{"c","c:3","c:4"}) && aliased.All(i=>i.extensions!=null && (string)i.extensions["mod"]=="x"),
                "деление стопки обходит ссылки инвентаря и сохраняет данные предмета: "+string.Join(",",aliased.Select(i=>i.id)));
            InventoryStacks.Validate(aliased,aliases);

            // Factory loot.
            var world=new WorldState {worldId=Guid.NewGuid().ToString("N"),seed=104};
            FactoryPreparation.Create(world,GameAssets.Load<FactoryCatalogAsset>(FactoryCatalogAsset.ResourceKey),0);
            int working=0,formal=0;
            try
            {
                FactoryContentSources.Prepare(world,"factory:0");
                RoomContentInstaller.Apply(world,RoomContentBatch.Read(FactoryContentSources.Require(world,"factory:0"),"factory:0"));
                foreach(var item in world.containers.SelectMany(c=>c.items))
                {
                    if(ExportedWeaponItems.Working(item.definition)) {Require(item.weaponState!=null && item.count==1,"ствол из лута без магазина: "+item.definition);working++;}
                    else if(item.definition.StartsWith("weapon:",StringComparison.Ordinal)) formal++;
                }
            }
            finally {FactoryContentSources.Clear();}
            Debug.Log($"PASS огнестрел: определений {generated.Length} ({generated.Select(a=>a.Id.Split(':')[1]).Distinct().Count()} моделей), "+
                $"графика/дуло/кадры/снаряд/звук; охотничья винтовка и штурмовая по AllData; формальные стопки → рабочие; завод seed 104: рабочих стволов в луте {working}, формальных вещей оружия {formal}.");
        }
        private static void CheckAmmunition(ExportedContentCatalog exported,WeaponDefinition hunt)
        {
            var actor=new ActorState {id="ammo-check",owner="one",player=true,connected=true,level="hub",health=100,weaponInventoryVersion=1};
            var gun=SharedInventory.Create(hunt.id,1);ExportedWeaponItems.Activate(new List<ItemStack>{gun});actor.inventory.Add(gun);
            Require(WeaponInventory.Select(actor,gun.id,out _),"выбор винтовки");
            // The actual exported loot stack and a pre-import loadout stack must feed the same magazine.
            actor.inventory.Add(exported.CreateFormalItem("item:p32",0,3));
            actor.inventory.Add(SharedInventory.Create("p32",2));actor.inventory.Add(exported.CreateFormalItem("item:p10",0,20));
            Require(WeaponAmmunition.Reserve(actor,hunt)==5 && WeaponAmmunition.CompleteReload(actor,hunt) && gun.weaponState.rounds==5 &&
                WeaponAmmunition.Reserve(actor,hunt)==0 && InventoryJournal.Count(actor,"item:p10")==20 && actor.inventoryUses.Count==1,
                "импортированные и старые патроны не расходуются одной операцией или расходуется чужой тип");
            gun.weaponState.rounds=0;
            var reserved=exported.CreateFormalItem("item:p32",0,5);actor.inventory.Add(reserved);actor.inventoryReservations[reserved.id]=2;
            Require(WeaponAmmunition.Reserve(actor,hunt)==3 && WeaponAmmunition.CompleteReload(actor,hunt) && gun.weaponState.rounds==3 && reserved.count==2,
                "перезарядка расходует зарезервированные для передачи патроны");
            foreach(string id in new[]{"weapon:rech:grade:0","weapon:rechr:grade:0"})
            {
                var definition=WeaponCatalog.Get(id);
                Require(definition.rechargeSeconds==1 && string.IsNullOrEmpty(definition.ammo) && definition.UsesMagazine,"ammo@recharg=30: "+id);
                var items=new List<ItemStack>{new(){id=id,definition=id,count=2}};ExportedWeaponItems.Activate(items);
                Require(items.All(i=>i.weaponState.rounds==definition.magazineSize),"зарядное оружие появляется заряженным: "+id);
                actor.inventory.AddRange(items);Require(WeaponInventory.Select(actor,items[0].id,out _),"выбор зарядного оружия");
                Require(WeaponAmmunition.SpendShot(actor,definition,out _) && WeaponAmmunition.ReloadAmount(actor,definition)==0,"расход/запрет ручной перезарядки");
                var magazine=items[0].weaponState;long revision=magazine.revision;float remaining=1;
                Require(!WeaponAmmunition.AdvanceRecharge(magazine,definition,ref remaining,.4f) && magazine.revision==revision,"ранний заряд/покадровая ревизия");
                Require(WeaponAmmunition.AdvanceRecharge(magazine,definition,ref remaining,.6f) && magazine.rounds==definition.magazineSize && magazine.revision==revision+1,
                    "заряд через 30 кадров");
                magazine.rounds=0;remaining=1;
                Require(WeaponAmmunition.AdvanceRecharge(magazine,definition,ref remaining,2.4f) && magazine.rounds==2 && Mathf.Abs(remaining-.6f)<.0001f &&
                    items[1].weaponState.rounds==definition.magazineSize,"длинный шаг и независимые экземпляры");
                var restored=StateJson.Copy(actor);Require(WeaponInventory.Selected(restored).weaponState.rounds==2,"сохранение восстановленного заряда");
                Require(!WeaponAmmunition.AdvanceRecharge(magazine,definition,ref remaining,0),"пауза меняет заряд");
            }
            var root=new GameObject("Проверка часов самоподзарядки");
            try
            {
                var session=root.AddComponent<Remains.Networking.CoopSession>();session.ConfigureRules(WeaponCatalog.Rules);
                var world=new WorldState {worldId="recharge-clock"};world.actors.Add(actor);
                typeof(Remains.Networking.CoopSession).GetProperty("World").SetValue(session,world);
                typeof(Remains.Networking.CoopSession).GetProperty("LocalOwner").SetValue(session,"one");
                var clock=new Remains.Gameplay.WeaponRechargeController(session);var selected=WeaponInventory.Selected(actor);selected.weaponState.rounds=0;
                clock.Step(actor,1,1);Require(selected.weaponState.rounds==0,"подзарядка идёт во время атаки");
                clock.Step(actor,.6f,0);Require(selected.weaponState.rounds==0,"преждевременный заряд контроллера");
                var other=actor.inventory.First(i=>i.id!=selected.id && i.definition==selected.definition);
                Require(WeaponInventory.Select(actor,other.id,out _),"смена зарядного экземпляра");clock.Step(actor,4,0);
                Require(selected.weaponState.rounds==0,"убранный ствол подзарядился");
                Require(WeaponInventory.Select(actor,selected.id,out _),"возврат зарядного экземпляра");clock.Step(actor,.4f,0);
                Require(selected.weaponState.rounds==1,"смена оружия потеряла частичный интервал");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        // A shotgun shot through the real preparation: kol pellets fanned deviation/2 apart, each with its own spread,
        // speed and ID, the others marked as the first one's volley.
        private static void CheckVolley()
        {
            const string weapon="weapon:shotgun:grade:0";
            var definition=WeaponCatalog.Get(weapon);
            Require(definition.pellets==5,"дробовик: 5 дробин (kol)");
            var root=new GameObject("Проверка залпа дроби");
            using var geometry=new Remains.Gameplay.SimulationGeometry(_=>new RoomLayout {id="hub",platforms=Array.Empty<RoomSurface>()});
            try
            {
                var session=root.AddComponent<Remains.Networking.CoopSession>();session.ConfigureRules(WeaponCatalog.Rules);
                var world=new WorldState {worldId="volley"};var player=new ActorState {id="shooter",owner="a",player=true,connected=true,level="hub",weaponInventoryVersion=1};
                NpcWeaponTestItems.Equip(player,weapon);world.actors.Add(player);
                typeof(Remains.Networking.CoopSession).GetProperty("World").SetValue(session,world);
                typeof(Remains.Networking.CoopSession).GetProperty("LocalOwner").SetValue(session,"a");
                geometry.Reconcile(world);
                player.weapon=weapon;
                Remains.Gameplay.WeaponPose.Aim(player,player.Position+new Vector2(10,1),geometry.Get("hub"));
                var origin=Remains.Gameplay.WeaponPose.Muzzle(player);var aim=Remains.Gameplay.WeaponPose.Direction(player);
                var pose=Remains.Gameplay.WeaponPose.SampleRanged(player);var end=origin+aim*definition.range;
                var shot=new AttackEvent {id="first",attacker=player.id,weapon=weapon,level="hub",originX=origin.x,originY=origin.y,endX=end.x,endY=end.y,
                    rangedPose=pose.Capture(player,world.worldId)};
                var volley=session.PrepareVolley(shot);
                Require(volley!=null && volley.Count==5 && volley[0]==shot && shot.volley==null && volley.Skip(1).All(p=>p.volley=="first") &&
                    volley.Select(p=>p.id).Distinct().Count()==5,"залп: пять дробин, первая — сам выстрел, остальные помечены");
                float half=definition.shotSpreadHalfAngleDegrees;
                for(int i=0;i<5;i++)
                {
                    float angle=Vector2.SignedAngle(aim,volley[i].End-volley[i].Origin),fan=(i-2)*half;
                    Require(Mathf.Abs(angle-fan)<=half+.001f && Vector2.Distance(volley[i].Origin,origin)<.0001f,$"дробина {i}: угол {angle:F2}° вне веера {fan:F2}±{half:F2}°");
                    ProjectileLaunch.Create(volley[i],definition);
                }
                Require(volley.Max(p=>Vector2.SignedAngle(aim,p.End-p.Origin))-volley.Min(p=>Vector2.SignedAngle(aim,p.End-p.Origin))>half,"веер шире одного разброса");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException("Огнестрел: "+message);}
    }
}
