using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Persistence
{
    public static class NpcWeaponInventoryMigration
    {
        private static bool ArmedType(ActorState actor)=>!actor.player && ActorTypes.Get(actor.actorType) is {combatAi:true,hasWeapon:true};
        public static bool NeedsInstall(WorldState world)=>world.actors.Any(a=>ArmedType(a) && a.weaponInventoryVersion!=1);
        public static bool Apply(WorldState world,Action preserveSource=null)
        {
            var changes=new List<(int index,ActorState actor,string notice)>();
            var itemIds=new HashSet<string>(world.actors.SelectMany(a=>a.inventory).Concat(world.containers.SelectMany(c=>c.items)).Select(i=>i.id),StringComparer.Ordinal);
            for(int index=0;index<world.actors.Count;index++)
            {
                var source=world.actors[index];if(!ArmedType(source) || source.weaponInventoryVersion==1) continue;
                if(source.weaponInventoryVersion!=0) throw new InvalidOperationException($"Неизвестная версия оружия NPC: actor={source.id}, version={source.weaponInventoryVersion}.");
                var actor=StateJson.Copy(source);ItemStack selected=null;bool initialized=false;
                if(!string.IsNullOrEmpty(actor.weapon))
                {
                    var definition=WeaponCatalog.Get(actor.weapon);
                    var matches=actor.inventory.Where(i=>i.definition==actor.weapon).ToArray();
                    if(matches.Length>1 || matches.Length==1 && (matches[0].count!=1 || matches[0].weaponState==null))
                        throw new InvalidOperationException($"Неоднозначный старый предмет оружия NPC: actor={actor.id}, weapon={actor.weapon}, matches={matches.Length}.");
                    if(matches.Length==1) selected=matches[0];
                    // A dead legacy body may already have been looted. Never replenish it.
                    else if(!actor.down && actor.health>0)
                    {
                        string id=ItemId(world.worldId,actor.id,actor.weapon);
                        if(!itemIds.Add(id)) throw new InvalidOperationException($"ID преобразуемого оружия NPC уже занят: actor={actor.id}, item={id}.");
                        var magazines=actor.magazines.Where(m=>m.weapon==actor.weapon).ToArray();
                        if(magazines.Length>1) throw new InvalidOperationException("Повтор старого магазина NPC: "+actor.id);
                        var magazine=magazines.SingleOrDefault();initialized=magazine==null;
                        var state=magazine==null?new WeaponItemState {rounds=definition.magazineSize}:
                            StateJson.FromJson<WeaponItemState>(StateJson.ToJson(magazine));
                        if(state.rounds<0 || state.rounds>definition.magazineSize || state.revision<0)
                            throw new InvalidOperationException($"Некорректный старый магазин NPC: actor={actor.id}, weapon={actor.weapon}, rounds={state.rounds}, revision={state.revision}.");
                        selected=new ItemStack {id=id,definition=actor.weapon,count=1,weaponState=state};actor.inventory.Add(selected);
                    }
                }
                actor.weaponInventoryVersion=1;
                if(!WeaponInventory.Select(actor,selected?.id,out string reason)) throw new InvalidOperationException($"Не преобразовано оружие NPC {actor.id}: {reason}");
                changes.Add((index,actor,$"Явно преобразован инвентарь оружия NPC: world={world.worldId}, actor={actor.id}, item={selected?.id ?? "нет"}, rounds={selected?.weaponState?.rounds ?? 0}, initialMagazine={initialized}, down={source.down}. Резерв не выдан, старые сведения сохранены, тело не пополнено."));
            }
            if(changes.Count==0) return false;
            if(world.revision==long.MaxValue) throw new InvalidOperationException("Исчерпан номер мира при преобразовании оружия NPC.");
            preserveSource?.Invoke();
            foreach(var change in changes) {world.actors[change.index]=change.actor;Debug.LogWarning(change.notice);}
            world.revision++;return true;
        }
        public static string ItemId(string world,string actor,string weapon)
        {
            using var hash=SHA256.Create();
            return "legacy-npc-weapon:"+BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(world+"\0"+actor+"\0"+weapon))).Replace("-","").ToLowerInvariant();
        }
    }
}
