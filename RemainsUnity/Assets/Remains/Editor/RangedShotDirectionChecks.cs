using System;
using System.IO;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class RangedShotDirectionChecks
    {
        public static void Run()
        {
            foreach(string weapon in new[]{"pistol","energy"})
            {
                var definition=WeaponCatalog.Get(weapon);
                Require(definition.shotSpreadHalfAngleDegrees==2.5f,"Базовое оружие потеряло явный профиль: "+weapon);
                foreach(var aim in new[]{Vector2.right,Vector2.left,Vector2.up,new Vector2(-3,4).normalized})
                {
                    foreach(float sample in new[]{0f,.17f,.5f,.83f,1f})
                    {
                        var attack=new AttackEvent {id="cone-"+weapon+"-"+sample,attacker="source",sourceOwner="a",level="hub",weapon=weapon,
                            originX=3,originY=2,endX=3+aim.x*20,endY=2+aim.y*20};
                        ProjectileLaunch.PrepareOwnerLaunch(attack,definition,.5f,sample);
                        float signed=Vector2.SignedAngle(aim,attack.End-attack.Origin);
                        Require(Mathf.Abs(signed)<=2.5001f && (sample==.5f || Mathf.Sign(signed)==Mathf.Sign(sample-.5f)),"Выстрел вышел за конус/изменил сторону отклонения.");
                        Require(Vector2.Distance(attack.Origin,new Vector2(3,2))==0,"Разброс переместил дуло.");
                        var expected=StateJson.ToJson(attack);
                        var flight=ProjectileLaunch.Create(attack,definition);
                        var world=new WorldState();world.projectiles.Add(flight);
                        world.actors.Add(new ActorState {id="near",owner="b",player=true,connected=true,level="hub",x=3,y=2});
                        flight.traveled=5;
                        ProjectileLifecycle.Reassign(world,new[]{"b"});
                        var restored=StateJson.Copy(world).projectiles[0];
                        ProjectileLifecycle.BeginSession(world);
                        Require(flight.owner==null && restored.owner=="b" && StateJson.ToJson(flight.launch)==expected && StateJson.ToJson(restored.launch)==expected,
                            "Передача/сохранение/новая сессия заново выбрали направление.");
                        Vector2? firstEnd=null;
                        foreach(int fps in new[]{30,60,144})
                        {
                            var path=restored.Path();
                            while(!path.Finished) path.Advance(1f/fps,(_,_)=>null);
                            Require(firstEnd==null || path.Position.Equals(firstEnd.Value),$"Частота кадров меняет конечную точку: weapon={weapon}, fps={fps}, endpoint={path.Position}, first={firstEnd}.");
                            firstEnd ??=path.Position;
                            float gap=Vector2.Distance(path.Position,attack.End);
                            Require(gap<Mathf.Max(.0001f,path.Range*.0000002f),$"Восстановление длинного луча меняет endpoint сверх float precision: weapon={weapon}, fps={fps}, gap={gap:R}, range={path.Range:R}.");
                        }
                    }
                }
            }
            Require(WeaponCatalog.Get("npc-shot").shotSpreadHalfAngleDegrees==0,"Точное регрессионное оружие стало неточным.");
            foreach(float sample in new[]{0f,.5f,1f})
                Require(RangedShotDirection.Select(Vector2.left,0,sample)==Vector2.left,"Нулевой профиль изменил направление.");
            foreach(float sample in new[]{float.NaN,float.NegativeInfinity,-.01f,1.01f})
                Reject(()=>RangedShotDirection.Select(Vector2.right,2.5f,sample),"Принята некорректная случайная доля.");
            foreach(float width in new[]{float.NaN,float.PositiveInfinity,-1f,90.01f})
                Reject(()=>RangedShotDirection.Select(Vector2.right,width,.5f),"Принят некорректный угол.");
            foreach(var aim in new[]{Vector2.zero,new Vector2(float.NaN,1),new Vector2(float.MaxValue,1)})
                Reject(()=>RangedShotDirection.Select(aim,2.5f,.5f),"Принято некорректное направление.");
            string emptyMods=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/no-spread-mods"));
            WeaponDefinition Profile(float width,float speed=90)=>new() {id="custom-cone",range=20,cooldown=1,projectileSpeed=speed,shotSpreadHalfAngleDegrees=width};
            LoadedWeaponCatalog Load(WeaponDefinition definition)=>WeaponCatalogLoader.Load(StateJson.ToJson(new WeaponList {weapons=new[]{definition}}),emptyMods);
            Require(Load(Profile(17)).definitions["custom-cone"].shotSpreadHalfAngleDegrees==17,"Мод не сохранил собственный профиль.");
            Require(Load(Profile(0,0)).definitions["custom-cone"].shotSpreadHalfAngleDegrees==0,"Нулевой профиль неприменимого оружия отклонён.");
            foreach(var bad in new[]{Profile(-1),Profile(91),Profile(1,0)})
                Reject(()=>Load(bad),"Некорректный профиль мода опубликован.");
            var previous=GameplayRuleSet.CreateDefault();var custom=new FixedDirectionRule(Vector2.up*3);
            var rules=new GameplayRuleSet(previous.Damage,previous.Knockdown,previous.PartyDefeat,previous.Loot,previous.Pause,previous.Deposit,
                previous.Telekinesis,previous.ProximityOwnership,previous.DoorAccess,previous.ItemActions,previous.NpcWeaponTiming,
                previous.NaturalAttackDamage,previous.ButtonRepair,previous.DoorDamage,custom);
            var customLaunch=new AttackEvent {id="custom-policy",attacker="source",sourceOwner="a",weapon="pistol",level="hub",endX=20};
            ProjectileLaunch.PrepareOwnerLaunch(customLaunch,WeaponCatalog.Get("pistol"),.5f,.2f,rules.ShotDirection);
            Require(custom.calls==1 && Mathf.Abs(customLaunch.endX)<.0001f && customLaunch.endY>0,"Owner launch не применил кодовый мод ровно один раз.");
            ProjectileLaunch.Create(StateJson.Copy(customLaunch),WeaponCatalog.Get("pistol"));
            Require(custom.calls==1,"Получатель повторно вызвал кодовый мод направления.");
            bool rejected=false;
            try {RangedShotDirection.Select(Vector2.right,2.5f,.5f,new FixedDirectionRule(Vector2.zero));} catch(InvalidOperationException) {rejected=true;}
            Require(rejected,"Кодовый мод скрыл некорректное направление.");
            CheckRejectedSpending(previous);
            Debug.Log("Разброс выстрела: PASS — данные оружия/мода, направления и границы, неизменное дуло, сохранение/передача без повторного выбора, полёт 30/60/144 Гц.");
        }
        private static void Reject(Action action,string reason)
        {
            try {action();} catch(ArgumentException) {return;} catch(InvalidDataException) {return;}
            throw new InvalidOperationException(reason);
        }
        private static void Require(bool value,string reason) {if(!value) throw new InvalidOperationException(reason);}
        private static void CheckRejectedSpending(GameplayRuleSet previous)
        {
            var root=new GameObject("Проверка отказа направления до расхода");
            using var geometry=new SimulationGeometry(_=>new RoomLayout {id="hub",platforms=Array.Empty<RoomSurface>()});
            try
            {
                var session=root.AddComponent<CoopSession>();
                var invalid=new GameplayRuleSet(previous.Damage,previous.Knockdown,previous.PartyDefeat,previous.Loot,previous.Pause,previous.Deposit,
                    previous.Telekinesis,previous.ProximityOwnership,previous.DoorAccess,previous.ItemActions,previous.NpcWeaponTiming,
                    previous.NaturalAttackDamage,previous.ButtonRepair,previous.DoorDamage,new FixedDirectionRule(Vector2.zero));
                session.ConfigureRules(invalid);
                var world=new WorldState();var player=new ActorState {id="player-invalid-direction",owner="a",player=true,connected=true,level="hub"};
                NpcWeaponTestItems.Equip(player,"pistol",12,36);world.actors.Add(player);
                typeof(CoopSession).GetProperty("World").SetValue(session,world);
                typeof(CoopSession).GetProperty("LocalOwner").SetValue(session,"a");
                geometry.Reconcile(world);
                int failures=0;session.GameplayPreparationFailed+=error=>{Require(error.Message.Contains("weapon=pistol"),"Отказ не содержит контекста оружия.");failures++;};
                var combat=new CombatController(session,geometry);
                string inventory=StateJson.ToJson(player.inventory);float mana=player.mana;
                Require(!combat.Fire(player,new Vector2(10,1)) && failures==1 && session.GameplayFailurePending &&
                    StateJson.ToJson(player.inventory)==inventory && player.mana==mana && player.inventoryUses.Count==0 &&
                    combat.ShotCooldownRemaining==0 && world.projectiles.Count==0,"Отказ направления игрока расходует/публикует выстрел или включает cooldown.");
                session.ConfigureRules(invalid);
                var npc=new ActorState {id="npc-invalid-direction",owner="a",actorType="raider",level="hub",x=-3};
                NpcWeaponTestItems.Equip(npc,"pistol",1,2);world.actors.Add(npc);
                inventory=StateJson.ToJson(npc.inventory);mana=npc.mana;
                Require(NpcAttackRules.TryBegin(npc,player),"NPC не подготовил настоящий оружейный выстрел для preflight.");
                npc.attackRemaining=0;
                var attack=NpcAttackRules.Complete(npc,player,geometry.Get("hub"),previous.NpcWeaponTiming,session.PrepareProjectile);
                Require(attack==null && failures==2 && session.GameplayFailurePending && StateJson.ToJson(npc.inventory)==inventory &&
                    npc.mana==mana && npc.inventoryUses.Count==0 && world.projectiles.Count==0,"Отказ направления NPC расходует или создаёт выстрел.");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        private sealed class FixedDirectionRule : IRangedShotDirectionRule
        {
            private readonly Vector2 direction;public int calls;
            public FixedDirectionRule(Vector2 direction)=>this.direction=direction;
            public Vector2 Select(Vector2 unitAim,float halfAngleDegrees,float sample) {calls++;return direction;}
        }
    }
}
