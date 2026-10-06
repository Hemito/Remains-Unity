using System;
using System.IO;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class HoldingAreaChecks
    {
        public static void Run()
        {
            int count=RunPure();
            var catalog=Resources.Load<WeaponAssetCatalog>("Content/WeaponCatalog");
            if(catalog==null) throw new InvalidOperationException("Нет каталога для проверки областей удержания.");
            catalog.OrdinaryHoldingArea.Validate("native Content/WeaponCatalog");
            var profile=WeaponRigs.OrdinaryHoldingArea;
            if(profile.version!=catalog.OrdinaryHoldingArea.version || profile.radius!=catalog.OrdinaryHoldingArea.radius ||
                profile.climbingRadius!=catalog.OrdinaryHoldingArea.climbingRadius ||
                !WeaponRigs.ManifestJson().Contains("ordinaryHoldingArea"))
                throw new InvalidOperationException("Реальный ассет, кэш и манифест обычного удержания расходятся.");
            foreach(string id in new[]{"grenade","spear","crowbar"})
            {
                var asset=Resources.Load<WeaponAsset>("Content/weapons/"+id);
                if(asset==null) throw new InvalidOperationException("Нет реального ассета удержания: "+id);
                var data=asset.CreateDefinition();var copy=asset.CreateDefinition();
                data.holdingArea.radius+=7;
                if(copy.holdingArea.radius!=asset.CreateDefinition().holdingArea.radius)
                    throw new InvalidOperationException("Изменение working holdingArea затронуло ассет: "+id);
            }
            Debug.Log($"Области удержания: PASS — {count} pure проверок, ассеты, манифест и изоляция данных; аккуратный выпуск без скачка силы.");
        }

        // Expects the same explicitly loaded content as gameplay; no Resources lookup or scene objects.
        public static int RunPure()
        {
            int count=0;
            void Require(bool value,string context)
            {count++;if(!value) throw new InvalidOperationException("HoldingAreaChecks: "+context);}
            void Reject(WeaponDefinition value,string context)
            {
                bool rejected=false;
                try {WeaponCatalogLoader.Load(StateJson.ToJson(new WeaponList {weapons=new[]{value}}),
                    Path.Combine(Path.GetTempPath(),"remains-holding-check-"+Guid.NewGuid().ToString("N")));}
                catch(InvalidDataException error) {rejected=error.Message.Contains(value.id) && error.Message.Contains("источник=base");}
                Require(rejected,"Нет полезной ошибки невалидного профиля: "+context);
            }
            var grenade=WeaponCatalog.Get("grenade");var ordinary=WeaponRigs.OrdinaryHoldingArea;
            ordinary.Validate("pure cached ordinary");
            Require(grenade.holdingArea.radius>0 && grenade.holdingArea.climbingRadius>0 && ordinary.climbingRadius>0,
                "Граната/обычная вещь неподвижны на лестнице.");
            foreach(string id in new[]{"grenade","spear","crowbar"})
            {
                var original=WeaponCatalog.Get(id);
                foreach(var invalid in new[]{default(HoldingArea),new HoldingArea(0,0),new HoldingArea(1,-1),
                    new HoldingArea(1,2),new HoldingArea(float.NaN,0),new HoldingArea(1,float.PositiveInfinity)})
                {var copy=StateJson.Copy(original);copy.holdingArea=invalid;Reject(copy,id);}
                var version=StateJson.Copy(original);version.holdingArea.version=2;Reject(version,id+"-version");
                var restored=StateJson.Copy(original);
                Require(restored.holdingArea.radius==original.holdingArea.radius && restored.holdingArea.climbingRadius==original.holdingArea.climbingRadius,
                    "Roundtrip потерял профиль: "+id);
            }
            var firearm=StateJson.Copy(WeaponCatalog.Get("pistol"));firearm.holdingArea=new HoldingArea(1,0);
            Reject(firearm,"unused-firearm-profile");
            var originalGrenadeArea=grenade.holdingArea;
            try
            {
                grenade.holdingArea=new HoldingArea(.35f,.2f);
                foreach(bool climbing in new[]{false,true})
                {
                    var held=new ActorState {weapon="grenade",climbing=climbing};
                    var item=new ActorState {heldItemId="ordinary-selection",climbing=climbing};
                    var target=HeldItemPose.Anchor(held)+new Vector2(5,3);
                    HeldItemPose.Aim(held,target);HeldItemPose.Aim(item,target);
                    Require(Vector2.Distance(HeldItemPose.Mount(held),HeldItemPose.Anchor(held))<.36f,
                        "Grenade override не ограничил собственную область.");
                    Require(Mathf.Abs(Vector2.Distance(HeldItemPose.Mount(item),HeldItemPose.Anchor(item))-ordinary.RadiusFor(climbing))<.00001f,
                        "Grenade override изменил обычную вещь.");
                    Require(HeldItemPose.TryQuickLaunch(item,target,null,out var quick) &&
                        Mathf.Abs(Vector2.Distance(quick.origin,HeldItemPose.Anchor(item))-grenade.holdingArea.RadiusFor(climbing))<.00001f,
                        "G использует обычную область вместо гранатной.");
                    Require(item.heldItemId=="ordinary-selection","G изменил активный ordinary selection.");
                }
            }
            finally {grenade.holdingArea=originalGrenadeArea;}
            foreach(string id in new[]{"spear","crowbar"})
            {
                var definition=WeaponCatalog.Get(id);float savedRange=definition.range;var savedArea=definition.holdingArea;
                try
                {
                    definition.range=.4f;definition.holdingArea=new HoldingArea(1.1f,.6f);
                    foreach(bool climbing in new[]{false,true})
                    {
                        var actor=new ActorState {weapon=id,climbing=climbing};
                        var origin=WeaponPose.Origin(actor);
                        WeaponPose.Aim(actor,origin+Vector2.up*10);
                        float expected=definition.holdingArea.RadiusFor(climbing);
                        Require(Mathf.Abs(actor.weaponReach-expected)<.00001f,"Melee holding по-прежнему зависит от damage range: "+id);
                        var tip=origin+WeaponPose.Direction(actor)*actor.weaponReach;
                        Require(Vector2.Distance(WeaponPose.Tip(id,origin,WeaponPose.Direction(actor),0,actor.weaponReach),tip)<.00001f &&
                            Vector2.Distance(WeaponPose.Tip(id,origin,WeaponPose.Direction(actor),1,actor.weaponReach),tip)<.00001f,
                            "Удар обрывает исходную/возвращаемую holding pose при radius>range: "+id);
                    }
                }
                finally {definition.range=savedRange;definition.holdingArea=savedArea;}
            }
            foreach(string id in new[]{"pistol","energy"})
            {
                var actor=new ActorState {weapon=id,weaponLift=0};
                foreach(float angle in new[]{-120f,-45,0,75,160})
                {
                    float radians=angle*Mathf.Deg2Rad;
                    WeaponPose.Aim(actor,WeaponPose.RangedAnchor(actor)+new Vector2(Mathf.Cos(radians),Mathf.Sin(radians))*5);
                    Require(WeaponPose.Mount(actor).y==WeaponPose.RangedAnchor(actor).y,
                        "Firearm elevation превратилась в двумерный диск: "+id);
                    Require(Vector2.Distance(WeaponPose.Mount(actor),WeaponPose.RangedAnchor(actor))<=WeaponRigs.AimRadius+.00001f,
                        "Firearm вышел из собственной области: "+id);
                }
            }
            foreach(bool climbing in new[]{false,true})
            foreach(bool crouched in new[]{false,true})
            foreach(bool ordinaryItem in new[]{false,true})
            foreach(float angle in new[]{-150f,-90,-30,0,45,90,135,180})
            {
                var actor=new ActorState {weapon="grenade",heldItemId=ordinaryItem?"ordinary":null,climbing=climbing,crouched=crouched};
                var anchor=HeldItemPose.Anchor(actor);float radius=HeldItemPose.ReachLimit(actor);
                var direction=new Vector2(Mathf.Cos(angle*Mathf.Deg2Rad),Mathf.Sin(angle*Mathf.Deg2Rad));
                Vector2 Release(float extra)
                {
                    var aim=anchor+direction*(radius+extra);
                    HeldItemPose.Aim(actor,aim);
                    var launch=HeldItemPose.Capture(actor,aim);
                    Require(launch.origin==HeldItemPose.Mount(actor),"Release origin расходится с фактической holding pose.");
                    return launch.velocity;
                }
                var boundary=Release(0);var tiny=Release(.0001f);var small=Release(.01f);var full=Release(100);
                Require(boundary.magnitude<.01f && tiny.magnitude<.1f && tiny.magnitude>boundary.magnitude &&
                    small.magnitude>tiny.magnitude && full.magnitude>small.magnitude,
                    $"Скачок/неплавное усилие на границе: angle={angle}, climb={climbing}, crouch={crouched}, ordinary={ordinaryItem}, boundary={boundary}, tiny={tiny}.");
                Require(Mathf.Abs(full.magnitude-grenade.grenadeMaxLaunchSpeed)<.00001f,"Нарушен объявленный предел скорости.");
            }
            // A cursor below the actual release point may deliberately throw downward.
            // Matching the cursor to the held point is the explicit zero-impulse release.
            var direct=GrenadeLaunch.Velocity(new Vector2(0,.8f),new Vector2(0,-1),0,grenade);
            Require(direct.x==0 && direct.y<0,"Вертикальное наведение вниз подавлено или искусственно поднято.");
            Require(GrenadeLaunch.Velocity(new Vector2(1,.8f),new Vector2(1,.8f),0,grenade)==Vector2.zero,
                "Нулевой оставшийся вектор задаёт импульс.");
            var minute=GrenadeLaunch.Velocity(Vector2.zero,Vector2.right*.00000001f,0,grenade);
            Require(minute.x>0 && minute.magnitude<.001f,"Малый ненулевой вектор потерян в normalization cutoff.");
            var room=new RoomLayout {left=-100,right=100,bottom=-100,top=100,platforms=Array.Empty<RoomSurface>()};
            foreach(float angle in new[]{-135f,-25,0,70,160})
            {
                Vector2? release=null,velocity=null,landing=null;
                foreach(int rate in new[]{30,60,144})
                {
                    var actor=new ActorState {weapon="grenade",climbing=true,y=20};
                    float radians=angle*Mathf.Deg2Rad;
                    var aim=HeldItemPose.Anchor(actor)+new Vector2(Mathf.Cos(radians),Mathf.Sin(radians))*(HeldItemPose.ReachLimit(actor)+.0001f);
                    for(int i=0;i<rate;i++) HeldItemPose.Aim(actor,aim,room);
                    var launch=HeldItemPose.Capture(actor,aim);
                    var flight=new GrenadeState {x=launch.origin.x,y=launch.origin.y,vx=launch.velocity.x,vy=launch.velocity.y,remaining=2,rotationVersion=1,angle=launch.angle,mirrored=launch.mirrored};
                    for(int i=0;i<rate;i++) GrenadeRules.Step(flight,room,1f/rate);
                    if(release.HasValue)
                        Require(Vector2.Distance(release.Value,launch.origin)<.00001f && Vector2.Distance(velocity.Value,launch.velocity)<.00001f &&
                            Vector2.Distance(landing.Value,flight.Position)<.0001f,"Composed аккуратный бросок зависит от FPS: "+rate);
                    release=launch.origin;velocity=launch.velocity;landing=flight.Position;
                }
            }
            CheckOverride(Require);
            return count;
        }

        private static void CheckOverride(Action<bool,string> require)
        {
            string temp=Path.GetFullPath(Path.GetTempPath());
            string root=Path.GetFullPath(Path.Combine(temp,"remains-holding-mod-"+Guid.NewGuid().ToString("N")));
            if(!root.StartsWith(temp.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Путь проверки мода вышел из временной папки.");
            Directory.CreateDirectory(Path.Combine(root,"holding"));
            try
            {
                var original=StateJson.Copy(WeaponCatalog.Get("grenade"));var changed=StateJson.Copy(original);
                changed.holdingArea=new HoldingArea(.4f,.3f);changed.grenadeMaxLaunchSpeed=2;
                string source=StateJson.ToJson(new WeaponList {weapons=new[]{original}});
                File.WriteAllText(Path.Combine(root,"load-order.json"),"{\"mods\":[\"holding\"]}");
                File.WriteAllText(Path.Combine(root,"holding","manifest.json"),
                    "{\"id\":\"holding-profile-check\",\"version\":\"1\",\"weaponOverrides\":[{\"id\":\"grenade\",\"source\":\"base\"}]}");
                File.WriteAllText(Path.Combine(root,"holding","weapons.json"),StateJson.ToJson(new WeaponList {weapons=new[]{changed}}));
                var loaded=WeaponCatalogLoader.Load(source,root);var data=loaded.definitions["grenade"];
                require(data.holdingArea.radius==.4f && data.holdingArea.climbingRadius==.3f &&
                    GrenadeLaunch.Velocity(Vector2.zero,Vector2.right*100,0,data).magnitude<=2.00001f,
                    "Явный mod override не применил профиль/launch параметры одним источником.");
                var baseline=WeaponCatalogLoader.Load(source,Path.Combine(root,"unused-mods"));
                require(loaded.fingerprint!=baseline.fingerprint,"Holding override не изменил content fingerprint.");
                require(WeaponCatalog.Get("grenade").holdingArea.radius==original.holdingArea.radius,
                    "Изолированная проверка загрузчика изменила действующий runtime каталог.");
            }
            finally {Directory.Delete(root,true);}
        }
    }
}
