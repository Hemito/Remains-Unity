using System;
using System.Collections.Generic;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;

namespace Remains.Gameplay
{
    public sealed class ItemShortcutActions
    {
        private readonly CoopSession session;
        private readonly Dictionary<string,Action<ItemStack,bool,Vector2?>> handlers=new(StringComparer.Ordinal);
        public ItemShortcutActions(CoopSession session,Action<string> equip)
        {
            this.session=session;
            Register(StandardItemActions.Equip,(item,_,_)=>equip(item.id));
            Register(StandardItemActions.Heal,(item,_,_)=>session.RequestSelfHeal(item.id));
            Register(StandardItemActions.Toss,(item,whole,aim)=>
            {
                if(!aim.HasValue) {session.ShowNotice("Укажите курсором направление броска.");return;}
                session.Deposits.Toss(item.id,aim.Value,whole?0:1);
            });
        }
        public void Register(string id,Action<ItemStack,bool,Vector2?> action,bool replace=false)
        {
            if(string.IsNullOrWhiteSpace(id) || action==null || !replace && handlers.ContainsKey(id))
                throw new InvalidOperationException("Некорректный или повторный обработчик предмета: "+id);
            handlers[id]=action;
        }
        public bool Activate(string slot,Vector2? aim,bool whole=false)
        {
            var actor=session.LocalActor;
            if(actor==null || !actor.connected || actor.down || session.Travel.Pending || session.Pause.IsPaused(actor.level)) return false;
            var item=ItemShortcuts.Get(actor,slot);
            if(item==null) {session.ShowNotice("Быстрая клавиша не назначена или предмет закончился.");return false;}
            if(InventoryAvailability.Count(actor,item)<=0) {session.ShowNotice("Предмет зарезервирован для передачи.");return false;}
            string id=session.Rules.ItemActions.Action(item);
            if(id==null || !handlers.TryGetValue(id,out var handler))
            {
                Debug.LogWarning($"Не найден обработчик действия предмета: actor={actor.id}, item={item.id}, definition={item.definition}, action={id}, rule={session.Rules.ItemActions.GetType().FullName}.");
                session.ShowNotice("Действие предмета не подключено. Подробности в журнале.");return false;
            }
            handler(item,whole,aim);return true;
        }
    }
}
