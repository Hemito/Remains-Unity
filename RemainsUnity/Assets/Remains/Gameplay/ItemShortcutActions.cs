using System;
using System.Collections.Generic;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;

namespace Remains.Gameplay
{
    public readonly struct ItemActionContext
    {
        public Vector2? Aim {get;}
        public bool WholeStack {get;}
        public bool ToggleEquipped {get;}
        public ItemActionContext(Vector2? aim,bool wholeStack=false,bool toggleEquipped=false)
        {Aim=aim;WholeStack=wholeStack;ToggleEquipped=toggleEquipped;}
    }
    public readonly struct ItemActionAvailability
    {
        public string ItemId {get;}
        public string ActionId {get;}
        public string Caption {get;}
        public string Rejection {get;}
        public bool Enabled=>ItemId!=null && ActionId!=null && Rejection==null;
        public ItemActionAvailability(string itemId,string actionId,string caption,string rejection)
        {ItemId=itemId;ActionId=actionId;Caption=caption;Rejection=rejection;}
    }
    public sealed class ItemShortcutActions
    {
        private sealed class Handler
        {
            public Action<ItemStack,ItemActionContext> Execute;
            public Func<ItemStack,ItemActionContext,string> Rejection,Caption;
        }
        private readonly CoopSession session;
        private readonly Dictionary<string,Handler> handlers=new(StringComparer.Ordinal);
        private readonly HashSet<string> missing=new(StringComparer.Ordinal);
        public ItemShortcutActions(CoopSession session,Action<string> equip)
        {
            this.session=session??throw new ArgumentNullException(nameof(session));
            if(equip==null) throw new ArgumentNullException(nameof(equip));
            Register(StandardItemActions.Equip,(item,context)=>equip(IsEquipped(item,context)?null:item.id),
                rejection:(item,_)=>WeaponInventory.IsEquippable(item)?null:"Этот предмет нельзя экипировать.",
                caption:(item,context)=>IsEquipped(item,context)?"Убрать оружие":"Взять в копыта");
            Register(StandardItemActions.Heal,(item,_)=>session.RequestSelfHeal(item.id),
                rejection:(item,_)=>HealingRules.Rejection(session.LocalActor,session.LocalActor,true));
            Register(StandardItemActions.Wear,(item,context)=>
            {
                if(!CosmeticEquipment.TryEquip(session.LocalActor,item.id,context.ToggleEquipped,out var reason))
                {session.ShowNotice(reason);return;}
                session.PublishActor(session.LocalActor);
            },rejection:(item,_)=>CosmeticEquipment.IsWearable(item)?null:"Для предмета не подключён внешний вид.",
                caption:(item,context)=>context.ToggleEquipped && CosmeticEquipment.IsEquipped(session.LocalActor,item.id)?"Снять":"Надеть");
            Action<ItemStack,ItemActionContext> hold=(item,_)=>
            {
                if(!HeldItemSelection.Select(session.LocalActor,item.id,out var reason)) {session.ShowNotice(reason);return;}
                session.PublishActor(session.LocalActor);
            };
            Func<ItemStack,ItemActionContext,string> holdRejection=(item,_)=>WeaponInventory.IsEquippable(item)?"Этот предмет не является обычной удерживаемой вещью.":null;
            Register(StandardItemActions.Hold,hold,rejection:holdRejection,caption:(_,_)=>"Взять в копыта");
            Register(StandardItemActions.Toss,hold,rejection:holdRejection,caption:(_,_)=>"Взять в копыта"); // Explicit compatibility for mods using the former action ID.
        }
        private bool IsEquipped(ItemStack item,ItemActionContext context)=>context.ToggleEquipped &&
            !HeldItemSelection.HasSelection(session.LocalActor) && session.LocalActor?.equippedWeaponItem==item.id;
        public bool TryThrow(Vector2 aim,bool whole=false)
        {
            if(session.GameplayFailurePending) return false;
            var actor=session.LocalActor;
            if(actor==null || !actor.connected || actor.down || session.Travel.Pending || session.Pause.IsPaused(actor.level)) return false;
            var item=HeldItemSelection.Selected(actor);
            if(item==null) {session.ShowNotice("Удерживаемый предмет закончился. Выберите другой предмет.");return false;}
            if(InventoryAvailability.Count(actor,item)<=0) {session.ShowNotice("Предмет зарезервирован для передачи.");return false;}
            return session.Deposits.Toss(item.id,aim,whole?0:1);
        }
        public void Register(string id,Action<ItemStack,bool,Vector2?> action,bool replace=false)
        {
            if(action==null) throw new ArgumentNullException(nameof(action));
            Register(id,(item,context)=>action(item,context.WholeStack,context.Aim),replace);
        }
        // Replacing a handler also replaces its predicate/caption; omitted delegates
        // intentionally leave only the shared actor/inventory gates, not old built-in rules.
        public void Register(string id,Action<ItemStack,ItemActionContext> action,bool replace=false,
            Func<ItemStack,ItemActionContext,string> rejection=null,Func<ItemStack,ItemActionContext,string> caption=null)
        {
            if(string.IsNullOrWhiteSpace(id) || action==null || !replace && handlers.ContainsKey(id))
                throw new InvalidOperationException("Некорректный или повторный обработчик предмета: "+id);
            handlers[id]=new Handler {Execute=action,Rejection=rejection,Caption=caption};
        }
        public bool Activate(string slot,Vector2? aim,bool whole=false)
        {
            if(session.GameplayFailurePending) return false;
            try
            {
                var item=ItemShortcuts.Get(session.LocalActor,slot);
                if(item==null) {session.ShowNotice("Быстрая клавиша не назначена или предмет закончился.");return false;}
                return ActivateItem(item.id,aim,whole);
            }
            catch(Exception error) {Failure(slot,"разрешение быстрой клавиши",error);return false;}
        }
        public ItemActionAvailability GetAvailability(string itemId,bool toggleEquipped=false,Vector2? aim=null,bool whole=false)
            =>Resolve(itemId,new ItemActionContext(aim,whole,toggleEquipped),out _,out _);
        public bool ActivateItem(string itemId,Vector2? aim=null,bool whole=false,bool toggleEquipped=false)
        {
            var context=new ItemActionContext(aim,whole,toggleEquipped);
            var availability=Resolve(itemId,context,out var item,out var handler);
            if(!availability.Enabled) {if(!session.GameplayFailurePending) session.ShowNotice(availability.Rejection);return false;}
            try {handler.Execute(item,context);return true;}
            catch(Exception error) {Failure(itemId,"исполнение action="+availability.ActionId,error);return false;}
        }
        private ItemActionAvailability Resolve(string itemId,ItemActionContext context,out ItemStack item,out Handler handler)
        {
            item=null;handler=null;
            if(session.GameplayFailurePending) return FailedAvailability(itemId);
            try {return ResolveAvailable(itemId,context,out item,out handler);}
            catch(Exception error)
            {item=null;handler=null;Failure(itemId,"проверка правила, подписи или доступности",error);return FailedAvailability(itemId);}
        }
        private static ItemActionAvailability FailedAvailability(string itemId)=>new(itemId,null,"Использовать","Ошибка действия предмета. Подробности — в журнале.");
        private void Failure(string itemId,string stage,Exception error)=>session.ReportGameplayPreparationFailure(
            new InvalidOperationException($"Ошибка действия предмета: actor={session.LocalActor?.id}, item={itemId}, этап={stage}. Действие не подменяется другим обработчиком.",error));
        private ItemActionAvailability ResolveAvailable(string itemId,ItemActionContext context,out ItemStack item,out Handler handler)
        {
            var actor=session.LocalActor;item=InventoryStacks.Find(actor,itemId);handler=null;
            string action=item==null?null:session.Rules.ItemActions.Action(item);
            string reason=actor==null || !actor.connected?"Персонаж не подключён.":
                actor.weaponInventoryVersion!=1?"Инвентарь предметов ещё не подготовлен.":
                session.Travel.Pending?"Переход в другую область ещё не завершён.":
                session.Pause.IsPaused(actor.level)?"Область на паузе. Действия временно недоступны.":
                actor.down?"Без сознания нельзя использовать предметы.":
                item==null?"Предмет закончился или отсутствует.":
                InventoryAvailability.Count(actor,item)<=0?"Предмет зарезервирован для передачи.":null;
            if(item!=null && (action==null || !handlers.TryGetValue(action,out handler)))
            {
                string key=session.Rules.ItemActions.GetType().FullName+":"+action;
                if(missing.Add(key)) Debug.LogWarning($"Не найден обработчик действия предмета: actor={actor.id}, item={item.id}, definition={item.definition}, action={action}, rule={session.Rules.ItemActions.GetType().FullName}.");
                reason??="Действие предмета не подключено. Подробности в журнале.";
            }
            string label=handler?.Caption==null?"Использовать":handler.Caption(item,context);
            if(string.IsNullOrWhiteSpace(label)) throw new InvalidOperationException($"Обработчик предмета {action} вернул пустую подпись: item={itemId}.");
            if(reason==null)
            {
                reason=handler?.Rejection?.Invoke(item,context);
                if(reason!=null && string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException($"Обработчик предмета {action} вернул пустую причину запрета: item={itemId}. Для разрешения верните null.");
            }
            return new ItemActionAvailability(item?.id,action,label,reason);
        }
    }
}
