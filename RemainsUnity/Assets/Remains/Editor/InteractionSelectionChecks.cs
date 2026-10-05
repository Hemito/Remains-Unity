using System;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Editor
{
    public static class InteractionSelectionChecks
    {
        private sealed class ExtendedCursorAccess : ILootRule
        {
            public string AccessRejection(LootAccessContext context)=>!context.body && context.connected && !context.down &&
                context.actorLevel==context.containerLevel && context.distance<=8?null:"Тест: доступ отклонён.";
            public string ClaimRejection(LootClaimContext context)=>null;
        }
        public static void Run()
        {
            Remains.Content.ActorTypes.Load();
            var world=new WorldState();var rules=GameplayRuleSet.CreateDefault();
            var actor=new ActorState {id="player",player=true,connected=true,x=0,y=0};world.actors.Add(actor);
            var container=new ContainerState {id="box",level=actor.level,x=1.5f,y=0};world.containers.Add(container);
            var door=new DoorState {id="door",definition="test",level=actor.level,x=1,y=0,width=.2f,height=2,surfaceTexture="floor"};
            world.doors.Add(door);
            Require(InteractionSelection.Select(world,actor,rules).id==door.id,"Ближайшая дверь не выбрана.");
            container.x=.5f;Require(InteractionSelection.Select(world,actor,rules).id==container.id,"Ближайший контейнер не выбран.");
            container.x=1;Require(InteractionSelection.Select(world,actor,rules).kind==InteractionKind.Container,"Равная дистанция не сохраняет выбор контейнера.");
            actor.inventory.Add(new ItemStack {id="heal",definition="heal",count=1});
            var ally=new ActorState {id="ally",player=true,connected=true,down=true,x=1.8f,y=0};world.actors.Add(ally);
            Require(InteractionSelection.Select(world,actor,rules).kind==InteractionKind.Revival,"Окно или дверь перехватывает поднятие союзника.");
            var revival=InteractionSelection.Select(world,actor,rules,((InventorySource)container).Bounds.center);
            Require(revival.kind==InteractionKind.Revival && revival.id==ally.id && revival.bounds==InventorySource.BodyBounds(ally),
                "Курсор скрыл сохранённый приоритет поднятия союзника или его выделение.");
            actor.inventory.Clear();Require(InteractionSelection.Select(world,actor,rules).kind!=InteractionKind.Revival,"Недоступное лечение блокирует E.");
            world.actors.Remove(ally);container.level="hub";door.level="hub";
            Require(InteractionSelection.Select(world,actor,rules).kind==InteractionKind.None,"Выбрана цель из другой локации.");
            door.level=actor.level;actor.down=true;
            Require(InteractionSelection.Select(world,actor,rules).kind==InteractionKind.None,"Тело может взаимодействовать.");
            CursorSelection();LooseReservation();PendingClaims();
            Debug.Log("Выбор E: PASS — совместимость, выбор курсором, габариты, движущиеся источники, тело, дальность, блокировка и целый подтверждаемый подбор.");
        }
        private static void CursorSelection()
        {
            var world=new WorldState();var rules=GameplayRuleSet.CreateDefault();
            var actor=new ActorState {id="cursor-player",player=true,connected=true,x=0,y=0};world.actors.Add(actor);
            var near=new ContainerState {id="near",level=actor.level,x=.2f,y=0,width=.3f,height=.3f};
            var chosen=new ContainerState {id="chosen",level=actor.level,x=1.6f,y=0,width=.3f,height=.3f};
            var far=new ContainerState {id="far",level=actor.level,x=5,y=0,width=.3f,height=.3f};
            world.containers.Add(near);world.containers.Add(chosen);world.containers.Add(far);
            var cursor=((InventorySource)chosen).Bounds.center;
            Require(InteractionSelection.Select(world,actor,rules,cursor).id==chosen.id,"Курсор проигрывает близости контейнера к персонажу.");
            actor.x=.3f;
            Require(InteractionSelection.Select(world,actor,rules,cursor).id==chosen.id,"Перемещение игрока меняет выбранный курсором доступный объект.");
            Require(InteractionSelection.Select(world,actor,rules,((InventorySource)far).Bounds.center).id!=far.id,"Курсор обходит дальность доступа.");
            var extended=new GameplayRuleSet(rules.Damage,rules.Knockdown,rules.PartyDefeat,new ExtendedCursorAccess());
            Require(InteractionSelection.Select(world,actor,extended,((InventorySource)far).Bounds.center).id==far.id,"Курсор игнорирует заменённое модом правило доступа.");
            chosen.x=5;
            Require(InteractionSelection.Select(world,actor,rules,cursor).id==near.id,"Движение источника не обновило доступность.");
            chosen.x=1.6f;chosen.presentationItem="p10";chosen.items.Add(SharedInventory.Create("p10",7));
            Require(InteractionSelection.Select(world,actor,rules,cursor).kind==InteractionKind.Pickup &&
                InteractionSelection.Select(world,actor,rules,((InventorySource)near).Bounds.center).kind==InteractionKind.Container,
                "Свободная вещь не отделена от обычного ящика.");
            var focus=new InteractionFocus();focus.Step(world,actor,rules,cursor,false,null);
            Require(focus.Target.id==chosen.id && focus.Target.bounds==((InventorySource)chosen).Bounds,"Источник живой цели потерял ID или габариты.");
            focus.Step(world,actor,rules,cursor,true,null);Require(focus.Target.kind==InteractionKind.None,"UI не блокирует живую цель.");
            focus.Step(world,actor,rules,null,false,null);Require(focus.Target.kind==InteractionKind.None,"Отсутствующий указатель включает ближайшую цель.");
            var body=new ActorState {id="cursor-body",player=true,connected=false,down=true,x=-.8f,y=0,level=actor.level};world.actors.Add(body);
            var source=InventorySource.Resolve(world,InventorySource.BodyId(body.id));
            Require(InteractionSelection.Select(world,actor,rules,source.Bounds.center).kind==InteractionKind.Container &&
                InteractionSelection.Select(world,actor,rules,source.Bounds.center).id==source.id,"Тело потеряло контейнерное взаимодействие.");
            body.down=false;body.connected=true;
            Require(InteractionSelection.Select(world,actor,rules,source.Bounds.center).id!=source.id,"Активный персонаж доступен как тело.");
            chosen.level="hub";near.level="hub";
            Require(InteractionSelection.Select(world,actor,rules,cursor).kind==InteractionKind.None,"Курсор выбирает источник из другой локации.");
        }
        private static void LooseReservation()
        {
            var world=new WorldState();var actor=new ActorState {id="pickup-one",player=true,connected=true,x=0};
            var other=new ActorState {id="pickup-two",player=true,connected=true,x=0};world.actors.Add(actor);world.actors.Add(other);
            var item=SharedInventory.Create("p10",9);
            var loose=new ContainerState {id="drop:cursor-test",level=actor.level,x=.5f,presentationItem="p10"};loose.items.Add(item);world.containers.Add(loose);
            var claims=new SharedInventory(world);var rules=GameplayRuleSet.CreateDefault();
            Require(claims.PrepareClaim("pickup-whole",loose.id,item.id,actor,out _,0) && world.pendingGrants[0].item.count==9 &&
                actor.inventory.Count==0 && loose.items.Count==0 && !DroppedItemTransfer.PruneEmpty(world),"Резерв не сохраняет всю стопку и источник до выдачи.");
            Require(!claims.PrepareClaim("pickup-rival",loose.id,item.id,other) && !claims.PrepareClaim("pickup-whole",loose.id,item.id,actor),
                "Одновременный или повторный запрос выдаёт тот же предмет.");
            loose.x=.7f;
            Require(InteractionSelection.Select(world,actor,rules,((InventorySource)loose).Bounds.center).id==loose.id,
                "Движущийся источник исчез из цели во время подтверждения.");
            var restored=world.Copy();var pending=restored.pendingGrants[0];var restoredClaims=new SharedInventory(restored);
            restoredClaims.CommitGrant(pending);restoredClaims.CommitGrant(pending);
            Require(restored.Find(actor.id).inventory.Count==1 && restored.Find(actor.id).inventory[0].id==item.id &&
                restored.Find(actor.id).inventory[0].count==9 && restored.Find(other.id).inventory.Count==0 && DroppedItemTransfer.PruneEmpty(restored),
                "Восстановление/повтор выдачи потерял стабильный ID, количество или единственного получателя.");
            var paused=new ContainerState {id="drop:paused",level=actor.level,x=0,presentationItem="p10"};paused.items.Add(SharedInventory.Create("p10",3));world.containers.Add(paused);
            Require(!claims.PrepareClaim("paused-pickup",paused.id,paused.items[0].id,actor,out _,0,true) && paused.items[0].count==3,
                "Пауза позволила подобрать свободную вещь.");
        }
        private static void PendingClaims()
        {
            var actor=new ActorState {id="intent-player",generation=2,lifecycleSequence=3};var world=new WorldState();
            var pending=new PendingItemClaim();
            Require(pending.Capture("intent-one",actor,"drop:one","item-one") && !pending.Capture("intent-two",actor,"drop:two","item-two"),
                "Повтор E заменил уже отправленную цель.");
            pending.Prepared("unrelated",false);pending.Step(world,actor,true);
            Require(pending.Pending && pending.SourceId=="drop:one" && pending.ItemId=="item-one","Чужой ответ или отсутствие визуального источника сбросили запрос.");
            actor.x=100;pending.Prepared("intent-one",true);pending.Prepared("intent-one",true);pending.Prepared("intent-one",false);
            Require(pending.Pending && pending.ReservationAccepted && actor.inventory.Count==0,"Принятый резерв выдан заранее или повторный ответ сбросил его.");
            world.completedTransactions.Add("intent-one");pending.Step(world,actor,true);Require(!pending.Pending,"Подтверждённый commit не завершил локальное ожидание.");
            pending.Capture("intent-two",actor,"drop:two","item-two");pending.Prepared("intent-one",false);
            Require(pending.Transaction=="intent-two","Поздний ответ прежней цели сбросил новый запрос.");
            pending.Prepared("intent-two",false);Require(!pending.Pending,"Явный отказ не завершил ожидание.");
            pending.Capture("intent-three",actor,"drop:three","item-three");actor.lifecycleSequence++;pending.Step(world,actor,true);
            Require(!pending.Pending,"Смена жизненного цикла оставила старый запрос в интерфейсе.");
            pending.Capture("intent-four",actor,"drop:four","item-four");pending.Step(world,actor,false);
            Require(!pending.Pending,"Отключение не очистило локальный запрос.");
        }
        private static void Require(bool condition,string reason) {if(!condition) throw new InvalidOperationException(reason);}
    }
}
