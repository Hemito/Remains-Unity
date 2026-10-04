using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Content
{
    [Serializable] public sealed class ContainerLootEntry
    {
        [InspectorName("ID строки")] public string id;
        [InspectorName("ID предмета")] public string definition;
        [SerializeReference,InspectorName("Состояние оружия (необязательно)")] public WeaponItemState weaponState;
        [InspectorName("Минимальное количество")] public int minimum;
        [InspectorName("Максимальное количество")] public int maximum;
    }
    [Serializable] public sealed class ContainerLootTable
    {
        [InspectorName("ID таблицы")] public string id;
        [InspectorName("Версия")] public string version;
        [InspectorName("Строки")] public ContainerLootEntry[] entries=Array.Empty<ContainerLootEntry>();
        public void Validate()
        {
            if(string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version) || entries==null)
                throw new InvalidOperationException("Не задана таблица лута или её версия: "+id);
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var entry in entries)
                if(entry==null || string.IsNullOrWhiteSpace(entry.id) || !ids.Add(entry.id) ||
                    string.IsNullOrWhiteSpace(entry.definition) || entry.minimum<0 || entry.maximum<entry.minimum ||
                    entry.weaponState!=null && entry.maximum>1)
                    throw new InvalidOperationException($"Некорректная строка лута: table={id}, entry={entry?.id}.");
        }
        public List<ItemStack> Generate(string world,string container)
        {
            Validate();
            if(string.IsNullOrWhiteSpace(world) || string.IsNullOrWhiteSpace(container)) throw new InvalidOperationException("Нет идентичности мира или контейнера для лута.");
            var result=new List<ItemStack>();
            foreach(var entry in entries)
            {
                byte[] hash;
                using(var stream=new MemoryStream())
                {
                    using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))
                    {writer.Write("remains-loot-v1");writer.Write(world);writer.Write(container);writer.Write(id);writer.Write(version);writer.Write(entry.id);}
                    using(var sha=SHA256.Create()) hash=sha.ComputeHash(stream.ToArray());
                }
                uint sample=(uint)hash[0] | (uint)hash[1]<<8 | (uint)hash[2]<<16 | (uint)hash[3]<<24;
                int count=(int)(entry.minimum+sample%((long)entry.maximum-entry.minimum+1));
                if(count==0) continue; // Explicit empty outcome, not a missing-content fallback.
                var item=new ItemStack {definition=entry.definition,count=count,weaponState=StateJson.Copy(entry.weaponState)};
                item.id="loot:"+BitConverter.ToString(hash).Replace("-","").ToLowerInvariant();result.Add(item);
            }
            return result;
        }
    }
}
