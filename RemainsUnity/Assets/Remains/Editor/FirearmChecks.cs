using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Content;
using Remains.Domain;
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
            var catalog=Resources.Load<WeaponAssetCatalog>("Content/WeaponCatalog");
            var generated=catalog.AllAssets().Where(a=>a.Id.StartsWith(ExportedWeaponItems.Prefix,StringComparison.Ordinal) && !a.Id.StartsWith("weapon:turret",StringComparison.Ordinal) && !WeaponCatalog.IsExplosive(a.Id)).ToArray();   // turret guns: TurretChecks
            Require(generated.Length>=50,"подключено меньше 50 определений: "+generated.Length);
            string audio=Resources.Load<TextAsset>("Presentation/combat-audio").text;
            var exported=ExportedContentCatalog.Load();
            foreach(var asset in generated)
            {
                var weapon=WeaponCatalog.Get(asset.Id);
                Require(ExportedWeaponItems.Working(asset.Id) && exported.TryItem(asset.Id,out _),"нет предмета "+asset.Id);
                Require(weapon.projectileSpeed>0 && weapon.damage>0 && weapon.cooldown>0 && weapon.magazineSize>0 && !string.IsNullOrEmpty(weapon.ammo) &&
                    weapon.meleeMotion==null && exported.TryItem(weapon.ammo,out _),"числа или патрон "+asset.Id);
                var rig=WeaponRigs.Get(asset.Id);
                Require(rig.HasMuzzle && WeaponRigs.PresentationId(asset.Id)==asset.Id,"нет дула или графики "+asset.Id);
                asset.ShotAnimation?.ValidateAgainst(asset.Presentation.Artwork.sprite);
                asset.ReloadAnimation?.ValidateAgainst(asset.Presentation.Artwork.sprite);
                Require(asset.ShotAnimation!=null && asset.ReloadAnimation!=null && asset.ShotAnimation.Deferred && asset.ReloadAnimation.Deferred,"нет кадров выстрела или перезарядки по требованию "+asset.Id);
                var restSprite=asset.Presentation.Artwork.sprite;
                for(int i=0;i<asset.ShotAnimation.FrameCount;i++) Require(asset.ShotAnimation.LoadFrameNow(i) is {} s && Mathf.Approximately(s.pixelsPerUnit,restSprite.pixelsPerUnit),"кадр выстрела "+i+" "+asset.Id);
                for(int i=0;i<asset.ReloadAnimation.FrameCount;i++) Require(asset.ReloadAnimation.LoadFrameNow(i) is {} s && Mathf.Approximately(s.pixelsPerUnit,restSprite.pixelsPerUnit),"кадр перезарядки "+i+" "+asset.Id);
                Require(ShotArtwork.TryGet(asset.Id,out _),"нет графики снаряда "+asset.Id);
                // The catalog loads only the rest frame (and the casing): animation frames are named, not referenced.
                int textures=AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(asset),true).Count(p=>AssetDatabase.GetMainAssetTypeAtPath(p)==typeof(Texture2D));
                Require(textures<=2,$"определение тянет {textures} текстур: {asset.Id}");
                Require(audio.Contains("{\"weapon\":\""+asset.Id+"\",\"clip\":\"exported:") && audio.Contains("\"weapon\":\""+asset.Id+"\",\"clip\":\"exported:sound"),"нет звука "+asset.Id);
            }
            var hunt=WeaponCatalog.Get("weapon:hunt:grade:0");
            Require(Mathf.Approximately(hunt.damage,22) && Mathf.Approximately(hunt.cooldown,25f/30) && hunt.magazineSize==5 &&
                Mathf.Approximately(hunt.reloadSeconds,50f/30) && Mathf.Approximately(hunt.projectileSpeed,300*30f/50) && hunt.ammo=="p32" &&
                Mathf.Approximately(hunt.shotSpreadHalfAngleDegrees,1),"охотничья винтовка по AllData");
            Require(!WeaponCatalog.Contains("weapon:p10mm:grade:0") && !WeaponCatalog.Contains("weapon:lasp:grade:0"),"ручные пистолеты не заменены");
            var assr0=WeaponCatalog.Get("weapon:assr:grade:0");var assr1=WeaponCatalog.Get("weapon:assr:grade:1");
            Require(Mathf.Approximately(assr0.damage,9) && Mathf.Approximately(assr1.damage,11) && assr1.ammo==assr0.ammo,"улучшенный вариант поверх базового");

            CheckVolley();

            // Formal stacks saved before the weapon was connected.
            var items=new List<ItemStack> {new() {id="a",definition="weapon:hunt:grade:0",count=2},new() {id="b",definition="weapon:knife:grade:0",count=1}};
            Require(ExportedWeaponItems.Needed(items) && ExportedWeaponItems.Activate(items)==1 && items.Count==3 &&
                items.Take(2).All(i=>i.count==1 && i.weaponState!=null && i.weaponState.rounds==0) && items[1].id=="a:2" && items[2].weaponState==null &&
                !ExportedWeaponItems.Needed(items) && WeaponInventory.IsEquippable(items[0]),"формальные стволы становятся рабочими по одному");

            // Factory loot.
            var world=new WorldState {worldId=Guid.NewGuid().ToString("N"),seed=104};
            FactoryPreparation.Create(world,Resources.Load<FactoryCatalogAsset>(FactoryCatalogAsset.ResourceKey),0);
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
