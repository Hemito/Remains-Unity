using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Remains.Domain;
namespace Remains.Persistence
{
    // Explicit legacy conversion. Caller must preserve its source before writing the result.
    public static class WeaponInventoryMigration
    {
        private static readonly string[] prototypeWeapons={"spear","crowbar","pistol","energy"};
        public static ActorState Prepare(ActorState source)
        {
            if(source==null) throw new ArgumentNullException(nameof(source));
            var actor=source.Copy();
            if(actor.weaponInventoryVersion==1) return actor;
            if(actor.weaponInventoryVersion!=0 || actor.loadoutVersion!=1)
                throw new InvalidOperationException($"Нельзя подготовить оружие: actor={actor.id}, inventoryVersion={actor.weaponInventoryVersion}, loadoutVersion={actor.loadoutVersion}.");
            if(actor.inventory.Any(item=>item.weaponState!=null))
                throw new InvalidOperationException($"Смешаны старое оснащение и экземпляры оружия: actor={actor.id}. Требуется разбор сохранения.");
            var magazines=new Dictionary<string,WeaponMagazine>();
            foreach(var magazine in actor.magazines)
                if(magazine==null || string.IsNullOrWhiteSpace(magazine.weapon) || magazine.rounds<0 || magazine.revision<0 || !magazines.TryAdd(magazine.weapon,magazine))
                    throw new InvalidOperationException($"Повреждён или повторён старый магазин: actor={actor.id}, weapon={magazine?.weapon}.");
            var definitions=new List<string>(prototypeWeapons);
            foreach(string definition in magazines.Keys)
                if(!definitions.Contains(definition)) definitions.Add(definition);
            if(!string.IsNullOrEmpty(actor.weapon) && !definitions.Contains(actor.weapon)) definitions.Add(actor.weapon);
            string identity=string.IsNullOrWhiteSpace(actor.character)?actor.id:actor.character;
            if(string.IsNullOrWhiteSpace(identity)) throw new InvalidOperationException("У старого персонажа нет устойчивого ID для миграции оружия.");
            actor.weaponShortcuts.Clear();actor.equippedWeaponItem=null;
            foreach(string definition in definitions)
            {
                // Stable across conversion of the character and its persisted world body.
                using var hash=SHA256.Create();
                string id="legacy-weapon:"+BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(identity+"\0"+definition))).Replace("-","").ToLowerInvariant();
                if(actor.inventory.Any(item=>item.id==id)) throw new InvalidOperationException($"Конфликт ID при миграции оружия: actor={actor.id}, item={id}.");
                var state=new WeaponItemState();
                if(magazines.TryGetValue(definition,out var magazine))
                {
                    state=magazine.Copy();
                    state.extensions?.Remove("weapon"); // Definition is carried by ItemStack.
                }
                actor.inventory.Add(new ItemStack {id=id,definition=definition,count=1,weaponState=state});
                if(actor.weapon==definition) actor.equippedWeaponItem=id;
                int slot=Array.IndexOf(prototypeWeapons,definition);
                if(slot>=0) actor.weaponShortcuts.Add(new WeaponShortcut {slot=(slot+1).ToString(System.Globalization.CultureInfo.InvariantCulture),item=id});
            }
            actor.magazines.Clear();actor.weaponInventoryVersion=1;actor.weaponSelectionRevision=1;actor.revision++;
            return actor;
        }
    }
}
