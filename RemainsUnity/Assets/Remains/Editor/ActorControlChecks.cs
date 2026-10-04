using System;
using System.IO;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Persistence;
using UnityEngine;

namespace Remains.Editor
{
    public static class ActorControlChecks
    {
        private sealed class FixedDamage : IDamageRule
        {
            private readonly float amount;
            public FixedDamage(float amount) {this.amount=amount;}
            public float Calculate(string weapon,float speed)=>amount;
        }
        private sealed class LivingKnockdown : IKnockdownRule
        {public bool ShouldEnter(KnockdownContext context)=>context.remainingHealth<50;}

        public static void Run()
        {
            var actor=new ActorState {id="player:reload",character="reload",player=true,connected=true,
                level="factory:0",x=2.8f,y=7.2f,health=11,vx=2,vy=3,climbing=true};
            actor.inventory.Add(SharedInventory.Create("heal",4));
            actor.resolvedAttacks.Add("old-attack");
            var before=actor.Copy();ActorSessionRules.Disconnect(actor);
            Require(!actor.down && ActorSessionRules.IsPassive(actor) && ActorPose.Clip(actor)=="down" && !actor.climbing,
                "Отключение должно убрать управление и позу лестницы, сохранив настоящий нокдаун отдельно");
            Require(actor.health==11 && actor.Position==before.Position && actor.Velocity==before.Velocity,
                "Отключение изменило HP, положение или импульс");
            var world=new WorldState();world.actors.Add(actor);
            Require(!PartyRules.ResolveDefeat(world),"Отключённое тело вызвало поражение группы");
            var healer=new ActorState {id="healer",player=true,connected=true,level=actor.level,x=actor.x,y=actor.y};
            Require(new NearbyLootRule().AccessRejection(new LootAccessContext(healer,InventorySource.Resolve(world,InventorySource.BodyId(actor.id))))==null,
                "Разделение состояний закрыло инвентарь отсутствующего игрока");
            var hit=new AttackEvent {id="offline-hit",weapon="pistol",level=actor.level};
            var receiver=new DamageReceiver(new FixedDamage(5));
            Require(receiver.Apply(actor,hit,true) && actor.health==6 && !actor.down && actor.damageSequence==1,
                "Отсутствующее тело не получило обычный урон");
            Require(!receiver.Apply(actor,hit,true) && actor.health==6,"Повтор попадания применил урон дважды");
            var position=actor.Position;
            ActorSessionRules.Connect(actor,"returned");
            Require(!actor.down && !ActorSessionRules.IsPassive(actor) && actor.health==6 && actor.Position==position &&
                actor.resolvedAttacks.Contains(hit.id) && actor.inventory[0].count==4 && !PartyRules.ResolveDefeat(world),
                "Возвращение живого игрока потеряло последствия отсутствия или отправило его в хаб");
            ActorSessionRules.Disconnect(actor);
            Require(new DamageReceiver(new FixedDamage(20)).Apply(actor,new AttackEvent {id="lethal",weapon="pistol",level=actor.level},true),
                "Не применён летальный урон отсутствующему телу");
            ActorSessionRules.Connect(actor,"returned-again");
            Require(actor.down && actor.health==0,"Возвращение отменило настоящий нокдаун");
            actor.health=25;actor.down=true;ActorSessionRules.Connect(actor,"mod-owner");
            Require(actor.down,"Соединение отменило нокдаун с положительным HP из изменённых правил");
            CheckSavedReload();
            Debug.Log("Состояние управления: PASS — отключение, урон без игрока, повтор попадания, возвращение, настоящий нокдаун и загрузка рейда.");
        }

        private static void CheckSavedReload()
        {
            string directory=Path.Combine(Path.GetTempPath(),"Remains-ActorControl-"+Guid.NewGuid().ToString("N"));
            try
            {
                var store=new SaveStore(directory);
                var actor=new ActorState {id="player:saved",character="saved",player=true,connected=false,down=true,
                    health=11,level="factory:0",x=71.39578f,y=4,loadoutVersion=1,weaponInventoryVersion=1};
                actor.inventory.Add(SharedInventory.Create("batt",11));actor.resolvedAttacks.Add("retained");
                var world=new WorldState();world.actors.Add(actor);store.SaveWorld(world);
                string source=File.ReadAllText(Path.Combine(directory,"world.json"));
                var loaded=store.LoadWorld();
                Require(ActorControlMigration.Prepare(loaded,new ZeroHealthKnockdownRule(),store.PreserveRecoverySource),"Старый формат не мигрирован");
                string[] backups=Directory.GetFiles(Path.Combine(directory,"Recovery"));
                Require(backups.Length==1 && File.ReadAllText(backups[0])==source,"Исходник не сохранён целиком перед миграцией");
                Require(!ActorControlMigration.Prepare(loaded,new ZeroHealthKnockdownRule(),()=>throw new Exception("Повторная миграция")),"Миграция повторилась");
                new OwnershipService().BeginSession(loaded);ActorSessionRules.Connect(loaded.actors[0],"new-session");
                var restored=loaded.actors[0];
                Require(!restored.down && restored.health==11 && restored.level==actor.level && restored.Position==actor.Position &&
                    restored.inventory[0].id==actor.inventory[0].id && restored.resolvedAttacks.Contains("retained") && !PartyRules.ResolveDefeat(loaded),
                    "Загрузка старого рейда не сохранила состояние живого игрока");
                ActorSessionRules.Disconnect(restored);store.SaveWorld(loaded);
                var current=store.LoadWorld();new OwnershipService().BeginSession(current);ActorSessionRules.Connect(current.actors[0],"third-session");
                Require(!current.actors[0].down && current.actors[0].health==11 && !PartyRules.ResolveDefeat(current),"Повторная загрузка снова создала нокдаун");
                actor.controlStateVersion=0;actor.down=true;actor.health=25;
                var modWorld=new WorldState();modWorld.actors.Add(actor);
                ActorControlMigration.Prepare(modWorld,new LivingKnockdown(),()=>"mod-source");
                Require(actor.down && actor.health==25,"Миграция отменила намеренный нокдаун изменённых правил");
                actor.controlStateVersion=0;
                modWorld.weaponContentHistory.Add(new Remains.Content.ContentManifest {packs=new()
                    {new Remains.Content.ContentPackRecord {id="former-gameplay-mod"}}});
                ActorControlMigration.Prepare(modWorld,new ZeroHealthKnockdownRule(),()=>"former-mod-source");
                Require(actor.down && actor.health==25,"Миграция забыла изменённые правила в истории сохранения");
            }
            finally {if(Directory.Exists(directory)) Directory.Delete(directory,true);}
        }
        private static void Require(bool value,string reason)
        {if(!value) throw new InvalidOperationException("Состояние управления: "+reason);}
    }
}
