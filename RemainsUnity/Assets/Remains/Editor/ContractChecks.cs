using System;
using System.Collections.Generic;
using System.IO;
using Remains.Domain;
using Remains.Persistence;
using UnityEditor;
using UnityEngine;
using Remains.Content;
using Remains.Gameplay;

namespace Remains.Editor
{
    public static class ContractChecks
    {
        public static void Run()
        {
            Anatomy.Load();
            CameraFramingChecks.Run();
            WeaponCatalog.Load();
            ActorTypeChecks.Run();
            DamageFeedbackChecks.Run();
            WeaponPoseChecks.Run();
            MovableContainerChecks.Run();
            TelekinesisRuleChecks.Run();
            AttackContactChecks.Run();
            WeaponModChecks.Run();
            GameplayContentChecks.Run();
            LongSavePathChecks.Run();
            CharacterContentChecks.Run();
            GameplayRuleChecks.Run();
            LevelPauseChecks.Run();
            AtomicSaveChecks.Run();
            DepositChecks.Run();
            BodyInventoryChecks.Run();
            PonyAnimationData.Load();
            var idle=Array.Find(PonyAnimationData.Library.clips,clip=>clip.id=="idle");
            Require(idle!=null && idle.frames.Length==1,"Покой содержит только позу стояния, без сидения и переходов прыжка");
            Require(PonyAnimationData.FrameIndex("idle",0,false)==PonyAnimationData.FrameIndex("idle",120,false),"Поза покоя не меняется со временем");
            foreach(var clip in PonyAnimationData.Library.clips)
                foreach(var frame in clip.frames)
                {
                    int devices=0;
                    foreach(var part in frame.parts)
                    {
                        if(part.path.StartsWith("pip") && PonyAppearance.IsVisible(part)) devices++;
                        if(part.path.Contains("/eye/eye/") && (part.shape=="201" || part.shape=="202"))
                            Require(part.texture.Contains("-masked-"),$"Глаз должен учитывать маску: {clip.id}, {part.path}");
                        Require(!(part.shape=="200" && part.path.Contains("/eye/")),"Маска глаза не должна отображаться отдельной фигурой");
                    }
                    Require(devices==1,$"Один пипбак в кадре {clip.id}");
                }
            var actor=new ActorState { id="victim",owner="client-b",player=true,connected=true };
            var damage=new DamageReceiver(new PrototypeDamageRule());
            var attack=new AttackEvent { id="shot-1",weapon="pistol",attacker="client-a",level="factory:0",originX=-2,originY=0.7f,endX=2,endY=0.7f };
            Require(HitGeometry.Contacts(actor,attack),"Сегмент должен пересекать корпус");
            Require(!damage.Apply(actor,attack,false) && actor.health==100,"Пострадавший может отклонить попадание");
            Require(damage.Apply(actor,attack,true) && actor.health==89,"Одно принятое попадание");
            Require(!damage.Apply(actor,attack,true) && actor.health==89,"Повтор события не наносит урон");
            var transferred=actor.Copy();transferred.owner="new-owner";transferred.generation++;
            Require(!new DamageReceiver(new PrototypeDamageRule()).Apply(transferred,attack,true) && transferred.health==89,
                "Передача копии другому владельцу сохраняет журнал урона");
            actor.down=true;
            attack.id="shot-2";
            damage.Apply(actor,attack,true);
            Require(actor.down,"Урон не поднимает отключённое тело");

            var world=new WorldState();world.actors.Add(actor);
            var second=new ActorState {id="second",owner="client-c",player=true,connected=true,x=5};world.actors.Add(second);
            var npc=new ActorState {id="npc",owner="client-b",target=actor.id,x=4,attackBusy=true};world.actors.Add(npc);
            var ownership=new OwnershipService();
            ownership.Reassign(world,new[]{"client-b","client-c"});
            Require(npc.owner=="client-b","Начатая атака сохраняет владельца");
            npc.attackBusy=false;ownership.Reassign(world,new[]{"client-b","client-c"});
            Require(npc.owner=="client-c" && npc.generation==1,"После атаки ближайший активный клиент получает NPC");
            npc.attackCooldown=.01f;npc.attackId="last-known-finished";
            ownership.Reassign(world,new[]{"client-b"});
            Require(!npc.attackBusy && npc.attackId==null && npc.attackCooldown==NpcAttackRules.CooldownSeconds,
                "Потеря владельца сбрасывает атаку и паузу даже при устаревшем idle-снимке");

            var item=SharedInventory.Create("caps",20);world.containers.Add(new ContainerState {x=0,items=new List<ItemStack>{item}});actor.down=false;
            var inventory=new SharedInventory(world);
            Require(inventory.PrepareClaim("claim-1",ContainerState.PrototypeId,item.id,actor),"Первый запрос резервирует предмет");
            Require(actor.inventory.Count==0 && world.pendingGrants.Count==1,"Резерв не выдаёт предмет до записи копий");
            second.x=1;
            Require(!inventory.PrepareClaim("claim-2",ContainerState.PrototypeId,item.id,second),"Другой клиент не получает резерв");
            inventory.CommitGrant(world.pendingGrants[0]);
            Require(actor.inventory.Count==1 && second.inventory.Count==0,"Общий предмет получает один игрок");

            actor.down=true;
            var directory=Path.Combine(Path.GetTempPath(),"Remains-contracts-"+Guid.NewGuid().ToString("N"));
            var saves=new SaveStore(directory);saves.SaveWorld(world);saves.SaveWorld(world);
            var restored=saves.LoadWorld();
            Require(restored.worldId==world.worldId && restored.Find(actor.id).down && restored.Find(actor.id).health==78,
                "Сохранение восстанавливает идентичность мира и состояние тела");
            Require(restored.completedTransactions.Contains("claim-1") && File.Exists(Path.Combine(directory,"world.json.previous")),
                "Сохранение сохраняет транзакцию и предыдущую версию");
            MovementChecks.Run();
            SessionChecks.Run();
            MeleeSweepChecks.Run();
            AmmunitionChecks.Run(); RecoveryChecks.Run(); RevivalChecks.Run(); GrenadeChecks.Run(); NpcAttackChecks.Run(); ContainerChecks.Run(); StateExtensionChecks.Run();
            actor.connected=true;actor.down=true;second.connected=false;second.down=false;
            actor.injuries.Add(new InjuryState{id="infection",periodicDamage=true});
            actor.injuries.Add(new InjuryState{id="fracture",periodicDamage=false});
            Require(PartyRules.ResolveDefeat(world) && actor.level=="hub" && actor.health==1 &&
                actor.injuries.Count==1 && actor.injuries[0].id=="fracture", "Отключённый игрок не мешает поражению; хаб сохраняет непериодические ранения");
            var stale=actor.Copy();stale.lifecycleSequence--;stale.revision=actor.revision+100;
            Require(!ActorVersion.CanPublishOwnerState(stale,actor) && ActorVersion.IsOlder(stale,actor),
                "Поздний пакет прежнего состояния не отменяет поднятие/поражение группы");
            var spear=new PrototypeDamageRule();
            Require(spear.Calculate("spear",10)>spear.Calculate("spear",1) && spear.Calculate("spear",1000)==76,
                "Урон копья зависит от скорости и имеет определённый данными предел");
            Debug.Log("Проверки контрактов: PASS — урон пострадавшего, дедупликация, тело, передача владельца, резерв/выдача, сохранение.");
        }
        private static void Require(bool condition,string detail)
        {
            if(!condition) throw new InvalidOperationException("Проверка контракта не прошла: "+detail);
        }
    }
}
