using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Content
{
    [CreateAssetMenu(menuName="Remains/Каталог комнат",fileName="RoomCatalog")]
    public sealed class RoomCatalogAsset : ScriptableObject
    {
        [Serializable] public sealed class Entry
        {
            [InspectorName("Стабильный ID")] public string id;
            [InspectorName("Авторская комната")] public RoomAuthoring room;
        }
        [SerializeField,InspectorName("Определения комнат")] private Entry[] entries=Array.Empty<Entry>();
        public IEnumerable<Entry> Entries {get {Validate();return entries;}}
        public void Configure(Entry[] values) {entries=values;Validate();}
        public void Validate()
        {
            if(entries==null) throw new InvalidOperationException("Каталог комнат: отсутствует список.");
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var entry in entries)
                if(entry==null || string.IsNullOrWhiteSpace(entry.id) || entry.room==null || !ids.Add(entry.id))
                    throw new InvalidOperationException($"Каталог комнат {name}: отсутствующее/повторное определение {entry?.id}.");
        }
        public RoomAuthoring Resolve(string id)
        {
            Validate();
            foreach(var entry in entries) if(entry.id==id) return entry.room;
            throw new InvalidOperationException("Нет определения комнаты в каталоге: "+id);
        }
        private void OnValidate()
        {
            try {Validate();} catch(Exception error) {Debug.LogWarning(error.Message,this);}
        }
        public static RoomCatalogAsset Load()=>Resources.Load<RoomCatalogAsset>("Content/RoomCatalog")??
            throw new InvalidOperationException("Не найден каталог Content/RoomCatalog.");
    }
}
