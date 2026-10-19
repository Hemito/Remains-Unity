using System;
using Remains.Content;
using System.Reflection;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using Remains.Serialization;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    public static class HeldItemChecks
    {
        public static void Run()
        {
            var actor=new ActorState {id="held-contract",owner="local",character="held-character",player=true,connected=true,weaponInventoryVersion=1,level="hub"};
            var ammo=InventoryStacks.Add(actor,SharedInventory.Create("p10",12));
            var grenade=SharedInventory.Create("grenade",1);actor.inventory.Add(grenade);
            Require(WeaponInventory.Select(actor,grenade.id,out _),"Не выбран предыдущий экземпляр гранаты");
            string inventory=StateJson.ToJson(actor.inventory);int uses=actor.inventoryUses.Count;
            Require(HeldItemSelection.Select(actor,ammo.id,out _) && actor.equippedWeaponItem==grenade.id && actor.weapon=="grenade" &&
                StateJson.ToJson(actor.inventory)==inventory && actor.inventoryUses.Count==uses && !actor.heldItemPoseValid,
                "Выбор обычной вещи потратил стопку, снял предыдущий экземпляр или использовал старую позу");
            foreach(string key in ItemShortcuts.Keys)
                Require(ItemShortcuts.Assign(actor,key,ammo.id,out _) && ItemShortcuts.Get(actor,key)==ammo,"Быстрая связь потеряна: "+key);
            actor.inventoryAliases["merged-held-reference"]=ammo.id;actor.heldItemId="merged-held-reference";
            Require(HeldItemSelection.Selected(actor)==ammo,"Удержание не разрешает alias объединённой стопки");
            actor.inventoryReservations[ammo.id]=ammo.count;
            long epoch=actor.weaponSelectionRevision;
            Require(!HeldItemSelection.Select(actor,ammo.id,out _) && actor.weaponSelectionRevision==epoch && actor.heldItemId=="merged-held-reference",
                "Зарезервированный предмет выбран или отказ изменил текущий выбор");
            actor.inventoryReservations.Clear();
            Require(HeldItemSelection.Select(actor,ammo.id,out _),"Не выбран доступный остаток");
            foreach(bool crouched in new[]{false,true})
            foreach(bool climbing in new[]{false,true})
            foreach(float distance in new[]{0f,.2f,.7f,8f})
            foreach(float angle in new[]{0f,45,90,135,180,225,270,315})
            {
                actor.crouched=crouched;actor.climbing=climbing;
                var direction=new Vector2(Mathf.Cos(angle*Mathf.Deg2Rad),Mathf.Sin(angle*Mathf.Deg2Rad));
                var aim=HeldItemPose.Anchor(actor)+direction*distance;
                Require(HeldItemPose.Aim(actor,aim),"Не получена конечная удерживаемая поза");
                var expected=HeldItemPose.Anchor(actor)+direction*Mathf.Min(distance,HeldItemPose.ReachLimit(actor));
                Require(Vector2.Distance(HeldItemPose.Mount(actor),expected)<.0001f,"Поза обычной вещи ограничена горизонталью или потеряла радиус");
                var launch=HeldItemPose.Capture(actor,aim);
                Require(launch.origin==HeldItemPose.Mount(actor) && float.IsFinite(launch.velocity.sqrMagnitude),"Выпуск не читает удерживаемую позу");
                var restored=actor.Copy();
                Require(HeldItemSelection.Selected(restored)?.id==ammo.id && HeldItemPose.Mount(restored)==launch.origin,
                    "Снимок потерял выбор или точку выпуска");
            }
            actor.crouched=actor.climbing=false;
            var wall=new RoomSurface {x=.6f,y=-1,width=.2f,height=3};
            var floor=new RoomSurface {x=-5,y=-1,width=10,height=1};
            var room=new RoomLayout {platforms=new[]{wall,floor}};
            Require(HeldItemPose.Aim(actor,new Vector2(3,1),room) && HeldItemPose.Mount(actor).x<wall.x-HeldItemPose.ClearanceRadius &&
                !SurfaceContact.Entry(wall,HeldItemPose.Mount(actor),HeldItemPose.Mount(actor),HeldItemPose.ClearanceRadius).HasValue,
                "Удерживаемый предмет прошёл в стену");
            Require(HeldItemPose.Aim(actor,new Vector2(0,-3),room) && HeldItemPose.Mount(actor).y>HeldItemPose.ClearanceRadius,
                "Удерживаемый предмет прошёл под пол");
            var invalidRoom=new RoomLayout {platforms=new[]{new RoomSurface {x=-.1f,y=-1,width=.2f,height=3}}};
            Require(!HeldItemPose.Aim(actor,new Vector2(3,1),invalidRoom) && !actor.heldItemPoseValid,
                "Исходная опора внутри стены принята как готовая поза");
            bool rejected=false;try {HeldItemPose.Capture(actor,new Vector2(3,1));} catch(InvalidOperationException) {rejected=true;}
            Require(rejected,"Недействительная поза запустила предмет из прежней точки");
            Require(HeldItemPose.Aim(actor,new Vector2(-3,3)),"Не подготовлена 2D-поза");
            var before=actor.Copy();
            Require(WeaponInventory.Select(actor,grenade.id,out _) && !HeldItemSelection.HasSelection(actor),"Экипировка не сняла обычную вещь");
            ActorPose.CopyMotion(before,actor);
            Require(!HeldItemSelection.HasSelection(actor) && !actor.heldItemPoseValid,"Поза прошлого выбора вернула удержание");
            WeaponInventory.PreserveSelection(actor,before);
            Require(before.equippedWeaponItem==grenade.id && !HeldItemSelection.HasSelection(before) && before.weaponSelectionRevision==actor.weaponSelectionRevision,
                "Старый снимок воскресил обычный выбор");
            CheckNetworkEpoch(actor);
            Require(HeldItemSelection.Select(actor,ammo.id,out _) && HeldItemPose.Aim(actor,new Vector2(-3,3)),"Не восстановлена обычная поза");
            string ordinarySelection=actor.heldItemId;var ordinaryMount=HeldItemPose.Mount(actor);float ordinaryAngle=actor.weaponAngle;
            var quick=GrenadeLaunchRequest.Capture(actor,new Vector2(3,2),room);
            Require(GrenadeLaunchRequest.Matches(actor,quick) && actor.heldItemId==ordinarySelection && HeldItemPose.Mount(actor)==ordinaryMount && actor.weaponAngle==ordinaryAngle,
                "Быстрый G изменил обычный выбор или не использовал готовую гранатную позу");
            Require(SharedInventory.Consume(actor,"grenade",quick.item) && actor.heldItemId==ordinarySelection,
                "Последняя быстрая граната сняла удерживаемую обычную вещь");
            var world=new WorldState();world.actors.Add(actor);
            var launchState=HeldItemPose.Capture(actor,new Vector2(-3,3));
            Require(DroppedItemTransfer.Prepare(world,actor,"held-drop",ammo.id,1,launchState.origin,launchState.velocity,new NearbyDepositRule(),out _,false,launchState.angle,launchState.mirrored),
                "Не подготовлен бросок из выбранной 2D-позы");
            var drop=world.containers[0];
            var looseBounds=DroppedItemShapeCatalog.Require(ammo.definition).WorldBounds(launchState.angle,launchState.mirrored);
            Require(drop.Position==launchState.origin+looseBounds.center && drop.presentationAngle==launchState.angle && drop.presentationMirrored==launchState.mirrored &&
                Mathf.Abs(drop.width-looseBounds.width)<.00001f && ammo.count==11 && world.grenades.Count==0,"Бросок изменил позу/форму, потратил всю стопку или создал гранату");
            var savedWorld=world.Copy();
            Require(savedWorld.containers[0].presentationAngle==drop.presentationAngle && savedWorld.containers[0].presentationMirrored==drop.presentationMirrored,
                "Сохранение потеряло позу выпуска обычной вещи");
            string pending=StateJson.ToJson(world);
            Require(!DroppedItemTransfer.Prepare(world,actor,"held-drop",ammo.id,1,launchState.origin,launchState.velocity,new NearbyDepositRule(),out _) && StateJson.ToJson(world)==pending,
                "Повтор броска изменил расход или общую вещь");
            Require(new SharedDeposits(world).Commit("held-drop") && !new SharedDeposits(world).Commit("held-drop") && drop.items.Count==1 && drop.items[0].count==1,
                "Подтверждение броска потеряло предмет или применено дважды");
            actor.inventoryReservations[ammo.id]=2;
            Require(DroppedItemTransfer.Prepare(world,actor,"held-all",ammo.id,0,launchState.origin,launchState.velocity,new NearbyDepositRule(),out _) &&
                ammo.count==2 && world.pendingDeposits[0].item.count==9 && InventoryAvailability.Count(actor,ammo)==0,
                "Весь доступный остаток включил уже зарезервированные единицы");
            Require(new SharedDeposits(world).Commit("held-all") && HeldItemSelection.Selected(actor)==ammo,
                "Подтверждение остатка потеряло ссылку на зарезервированную стопку");
            CheckVisualRelease();
            Debug.Log("Удерживаемые вещи: PASS — выбор без расхода, 12 ссылок/alias, резерв, 2D-диапазон, препятствия, выпуск, эпоха, G и единая транзакция.");
        }
        private static void CheckVisualRelease()
        {
            var prefab=Resources.Load<SpriteRenderer>("Presentation/HeldItem");
            Require(prefab!=null,"Не найден сохранённый префаб обычной вещи");
            var icons=new InventoryArtwork();icons.PrepareWorldMaterials();var props=new PropArtwork();
            foreach(string definition in new[]{"p10","caps"})
            foreach(float angle in new[]{-135f,0,45,90})
            foreach(bool mirrored in new[]{false,true})
            {
                var actor=new ActorState {id="held-visual",owner="local"};
                var state=DroppedItemTransfer.Preview(actor,"visual",definition,new Vector2(2,3),Vector2.zero,angle,mirrored);
                var held=UnityEngine.Object.Instantiate(prefab);var dropped=props.CreateContainerView(state);
                try
                {
                    var sprite=icons.Require(definition);SpriteArtworkMaterial.Apply(held,sprite);
                    float scale=DroppedItemTransfer.Width/sprite.bounds.size.x;
                    held.transform.localScale=new Vector3(scale,scale,1);held.transform.position=new Vector2(2,3);
                    held.transform.rotation=Quaternion.Euler(0,0,angle);held.flipY=mirrored;
                    dropped.DrawMotion(state,"local",false,0);
                    for(int corner=0;corner<4;corner++)
                        Require(Vector2.Distance(Corner(held,corner),Corner(dropped.Artwork,corner))<.00001f,
                            $"Изображение обычной вещи изменилось при выпуске: definition={definition}, angle={angle}, mirrored={mirrored}, corner={corner}");
                }
                finally {UnityEngine.Object.DestroyImmediate(held.gameObject);UnityEngine.Object.DestroyImmediate(dropped.gameObject);}
            }
        }
        private static Vector3 Corner(SpriteRenderer renderer,int corner)
        {
            var bounds=renderer.sprite.bounds;
            var point=new Vector3(corner%2==0?bounds.min.x:bounds.max.x,corner<2?bounds.min.y:bounds.max.y,0);
            if(renderer.flipX) point.x=-point.x;if(renderer.flipY) point.y=-point.y;
            return renderer.transform.TransformPoint(point);
        }
        private static void CheckNetworkEpoch(ActorState actor)
        {
            float previousAngle=actor.weaponAngle;bool previousValid=actor.heldItemPoseValid;
            var apply=typeof(CoopSession).GetMethod("ApplyPose",BindingFlags.Instance|BindingFlags.NonPublic);
            Require(apply!=null,"Не найден фактический обработчик сетевой позы");
            var root=new GameObject("Проверка текущей сетевой эпохи удержания");
            try
            {
            var session=root.AddComponent<CoopSession>();
            apply.Invoke(session,new object[]{actor,new PoseMessage {weaponSelectionRevision=actor.weaponSelectionRevision-1,
                weapon="pistol",weaponAngle=123,heldItemPoseX=10,heldItemPoseY=20,heldItemPoseValid=true},false});
            Require(actor.weapon=="grenade" && actor.weaponAngle==previousAngle && actor.heldItemPoseValid==previousValid,
                "Отложенный пакет позы применён к другому выбору");
            apply.Invoke(session,new object[]{actor,new PoseMessage {weaponSelectionRevision=actor.weaponSelectionRevision,
                weapon="grenade",weaponAngle=45,heldItemPoseX=.3f,heldItemPoseY=.4f,heldItemPoseValid=true},false});
            Require(actor.weaponAngle==45 && actor.heldItemPoseX==.3f && actor.heldItemPoseY==.4f && actor.heldItemPoseValid,
                "Пакет текущей эпохи потерял удерживаемую позу");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void Require(bool value,string reason) {if(!value) throw new InvalidOperationException("Удерживаемые предметы: "+reason);}
    }
}
