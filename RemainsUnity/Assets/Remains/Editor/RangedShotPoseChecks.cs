using System;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class RangedShotPoseChecks
    {
        public static void Run()
        {
            foreach(string weapon in new[]{"pistol","energy"})
                foreach(bool left in new[]{false,true})
                    foreach(string stance in new[]{"stand","crouch","climb"})
                    {
                        var actor=new ActorState {id="pose",owner="a",player=true,connected=true,level="hub",weapon=weapon,
                            equippedWeaponItem="instance",weaponSelectionRevision=4,generation=2,lifecycleSequence=3,x=3,y=2,
                            crouched=stance=="crouch",climbing=stance=="climb"};
                        actor.weaponRecoil=new WeaponRecoilPose {offsetX=left?.05f:-.05f,angleDegrees=left?-3:3};
                        var target=actor.Position+new Vector2(left?-10:10,3);WeaponPose.Aim(actor,target);
                        float baseAngle=actor.weaponAngle;var pose=WeaponPose.SampleRanged(actor);
                        Require(Mathf.Abs(Mathf.DeltaAngle(baseAngle,pose.Angle)-(left?-3:3))<.001f,"Отдача не добавлена ровно один раз.");
                        WeaponPose.Aim(actor,target);
                        Require(actor.weaponAngle==baseAngle,"Прицел интегрирует предыдущую отдачу обратно в нейтральный угол.");
                        var shot=new AttackEvent {id="pose-shot",attacker=actor.id,sourceOwner=actor.owner,weapon=weapon,level=actor.level,
                            originX=pose.Muzzle.x,originY=pose.Muzzle.y,endX=pose.Muzzle.x+pose.Direction.x*20,endY=pose.Muzzle.y+pose.Direction.y*20,
                            rangedPose=pose.Capture(actor,"test-world")};
                        RangedShotPreparation.ValidateOwner(shot,actor,"test-world",WeaponCatalog.Get(weapon),new SmoothWeaponRecoilRule());
                        Require(shot.rangedPose.hasCasing==(weapon=="pistol"),"Энергетическое оружие получило гильзу или пистолет потерял её.");
                        if(shot.rangedPose.hasCasing)Require(Vector2.Distance(pose.Root,new Vector2(shot.rangedPose.casingX,shot.rangedPose.casingY))<.0001f,"Гильза не использует захваченный socket.");
                        var view=UnityEngine.Object.Instantiate(WeaponRigs.Prefab(weapon));
                        try
                        {
                            view.transform.position=pose.Root;view.transform.rotation=Quaternion.Euler(0,0,pose.Angle);view.SetMirrored(pose.Mirrored);
                            Require(Vector2.Distance(view.Muzzle.position,shot.Origin)<.0001f,"Фактическое дуло prefab не совпадает с запуском.");
                        }
                        finally {UnityEngine.Object.DestroyImmediate(view.gameObject);}
                        ProjectileLaunch.PrepareOwnerLaunch(shot,WeaponCatalog.Get(weapon),.5f,.8f);
                        string original=StateJson.ToJson(shot);actor.x=99;actor.weaponAngle=-100;actor.weaponRecoil.angleDegrees=25;
                        var restored=StateJson.Copy(shot);RangedShotPreparation.ValidateReceived(restored,"test-world");
                        Require(StateJson.ToJson(restored)==original,"Получатель заменил исторический выстрел текущей позой.");
                        Reject(()=>RangedShotPreparation.ValidateReceived(restored,"different-world"),"Принят выстрел другого мира.");
                    }
            CheckNpc();CheckInvalidPreflight();CheckCommitFailure();
            Debug.Log("Общая поза выстрела: PASS — 12 поз/дул prefab, предшествующая отдача, NPC, исторический запуск, invalid preflight без расхода.");
        }
        private static void CheckNpc()
        {
            var npc=new ActorState {id="npc-pose",owner="a",actorType="raider",level="hub",x=-3};
            var target=new ActorState {id="target",owner="b",player=true,connected=true,level="hub",x=8};
            NpcWeaponTestItems.Equip(npc,"pistol",12,36);
            npc.weaponRecoil=new WeaponRecoilPose {offsetX=-.06f,angleDegrees=4};
            Require(NpcAttackRules.TryBegin(npc,target),"NPC не начал действительный оружейный выстрел.");npc.attackRemaining=0;
            var attack=NpcAttackRules.Complete(npc,target,new RoomLayout {id="hub",platforms=Array.Empty<RoomSurface>()},GameplayRuleSet.CreateDefault().NpcWeaponTiming);
            Require(attack!=null && attack.rangedPose!=null,"NPC не захватил позу.");
            var pose=WeaponPose.SampleRanged(npc);var delta=attack.End-attack.Origin;
            Require(Vector2.Distance(delta.normalized,pose.Direction)<.0001f && Vector2.Distance(attack.Origin,pose.Muzzle)<.0001f,
                "NPC обходит отдачу прямым лучом к цели.");
        }
        private static void CheckInvalidPreflight()
        {
            var root=new GameObject("Отказ отдачи до расхода");
            using var geometry=new SimulationGeometry(_=>new RoomLayout {id="hub",platforms=Array.Empty<RoomSurface>()});
            try
            {
                var session=root.AddComponent<CoopSession>();var previous=GameplayRuleSet.CreateDefault();
                session.ConfigureRules(new GameplayRuleSet(previous.Damage,previous.Knockdown,previous.PartyDefeat,previous.Loot,previous.Pause,previous.Deposit,
                    previous.Telekinesis,previous.ProximityOwnership,previous.DoorAccess,previous.ItemActions,previous.NpcWeaponTiming,
                    previous.NaturalAttackDamage,previous.ButtonRepair,previous.DoorDamage,previous.ShotDirection,new InvalidCurve()));
                var world=new WorldState();var player=new ActorState {id="invalid-recoil",owner="a",player=true,connected=true,level="hub"};
                NpcWeaponTestItems.Equip(player,"pistol",12,36);world.actors.Add(player);
                typeof(CoopSession).GetProperty("World").SetValue(session,world);typeof(CoopSession).GetProperty("LocalOwner").SetValue(session,"a");geometry.Reconcile(world);
                var combat=new CombatController(session,geometry);string inventory=StateJson.ToJson(player.inventory);float mana=player.mana;
                int failures=0;session.GameplayPreparationFailed+=_=>failures++;
                Require(!combat.Fire(player,new Vector2(10,1)) && failures==1 && session.GameplayFailurePending &&
                    inventory==StateJson.ToJson(player.inventory) && mana==player.mana && player.inventoryUses.Count==0 && world.projectiles.Count==0 &&
                    combat.ShotCooldownRemaining==0,"Некорректная кривая расходует ресурсы или создаёт выстрел.");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        private sealed class InvalidCurve : IWeaponRecoilRule
        {public WeaponRecoilContribution Sample(WeaponRecoilProfile profile,double ageSeconds)=>new() {angle=float.NaN};}
        private static void CheckCommitFailure()
        {
            var root=new GameObject("Ошибка кодового правила после принятия выстрела");
            try
            {
                var session=root.AddComponent<CoopSession>();var previous=GameplayRuleSet.CreateDefault();var curve=new StatefulCurve();
                session.ConfigureRules(new GameplayRuleSet(previous.Damage,previous.Knockdown,previous.PartyDefeat,previous.Loot,previous.Pause,previous.Deposit,
                    previous.Telekinesis,previous.ProximityOwnership,previous.DoorAccess,previous.ItemActions,previous.NpcWeaponTiming,
                    previous.NaturalAttackDamage,previous.ButtonRepair,previous.DoorDamage,previous.ShotDirection,curve));
                var world=new WorldState();var actor=new ActorState {id="stateful-curve",owner="a",player=true,connected=true,level="hub"};
                NpcWeaponTestItems.Equip(actor,"pistol",12,36);world.actors.Add(actor);
                typeof(CoopSession).GetProperty("World").SetValue(session,world);typeof(CoopSession).GetProperty("LocalOwner").SetValue(session,"a");
                session.Recoil.Reconcile();WeaponPose.Aim(actor,new Vector2(10,1));var pose=WeaponPose.SampleRanged(actor);
                var shot=new AttackEvent {id="accepted",attacker=actor.id,sourceOwner=actor.owner,weapon="pistol",level="hub",rangedPose=pose.Capture(actor,world.worldId),
                    originX=pose.Muzzle.x,originY=pose.Muzzle.y,endX=pose.Muzzle.x+pose.Direction.x*20,endY=pose.Muzzle.y+pose.Direction.y*20};
                RangedShotPreparation.ValidateOwner(shot,actor,world.worldId,WeaponCatalog.Get("pistol"),curve);
                int failures=0;session.GameplayPreparationFailed+=_=>failures++;
                session.Recoil.Commit(actor,shot);
                Require(failures==1 && session.GameplayFailurePending,"Ошибка stateful curve вышла из Update без остановки сессии.");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        private sealed class StatefulCurve : IWeaponRecoilRule
        {private int calls;public WeaponRecoilContribution Sample(WeaponRecoilProfile profile,double ageSeconds)
            {if(++calls>1)throw new InvalidOperationException("Намеренный отказ stateful rule после preflight");return default;}}
        private static void Reject(Action action,string reason) {try {action();}catch(InvalidOperationException){return;}throw new InvalidOperationException(reason);}
        private static void Require(bool value,string reason) {if(!value)throw new InvalidOperationException("Общая поза: "+reason);}
    }
}
