using System;
using System.Collections.Generic;
using UnityEngine;
namespace Remains.Content
{
    [Serializable]
    public sealed class InventoryIconEntry
    {
        [InspectorName("ID предмета")] public string Id;
        [InspectorName("Иконка")] public Sprite Sprite;
    }
    [CreateAssetMenu(menuName="Remains/Иконки инвентаря",fileName="InventoryIcons")]
    public sealed class InventoryIconAsset : ScriptableObject
    {
        [SerializeField,InspectorName("Предметы")] private InventoryIconEntry[] entries=Array.Empty<InventoryIconEntry>();
        public Dictionary<string,Sprite> CreateLookup()
        {
            if(entries==null) throw new InvalidOperationException($"Каталог иконок {name}: список предметов отсутствует.");
            var result=new Dictionary<string,Sprite>(StringComparer.Ordinal);
            for(int i=0;i<entries.Length;i++)
            {
                var entry=entries[i];
                if(entry==null || string.IsNullOrWhiteSpace(entry.Id) || entry.Sprite==null)
                    throw new InvalidOperationException($"Каталог иконок {name}: не заполнены ID или спрайт записи {i}.");
                if(!result.TryAdd(entry.Id,entry.Sprite))
                    throw new InvalidOperationException($"Каталог иконок {name}: повторный ID {entry.Id}.");
            }
            return result;
        }
        private void OnValidate()
        {
            try {CreateLookup();} catch(InvalidOperationException error) {Debug.LogWarning(error.Message,this);}
        }
    }
}
