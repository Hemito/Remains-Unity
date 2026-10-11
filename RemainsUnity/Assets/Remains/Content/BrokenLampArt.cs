using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Content
{
    public sealed class BrokenLampArt : ScriptableObject
    {
        public const string ResourcePath="Presentation/Lamps/Broken/LampArt";
        [Serializable] public sealed class Entry {public string definitionId;public Sprite sprite;}
        [SerializeField,InspectorName("Разбитые лампы")] private Entry[] entries=Array.Empty<Entry>();
        [SerializeField,InspectorName("Виды без новой разбитой графики")] private string[] notIncluded=Array.Empty<string>();
        private Dictionary<string,Sprite> index;
        private HashSet<string> omitted;

        public void Configure(Entry[] art,string[] knownOmissions)
        {entries=art;notIncluded=knownOmissions;index=null;omitted=null;Validate();}
        public bool TryGet(string definitionId,out Sprite sprite) {Validate();return index.TryGetValue(definitionId,out sprite);}
        public bool IsKnownOmission(string definitionId) {Validate();return omitted.Contains(definitionId);}
        public void Validate()
        {
            if(index!=null) return;
            var next=new Dictionary<string,Sprite>(StringComparer.Ordinal);
            var missing=new HashSet<string>(StringComparer.Ordinal);
            foreach(var entry in entries)
                if(entry==null || string.IsNullOrWhiteSpace(entry.definitionId) || entry.sprite==null || !next.TryAdd(entry.definitionId,entry.sprite))
                    throw new InvalidOperationException($"Разбитая графика ламп {name}: неверная или повторная запись {entry?.definitionId}.");
            foreach(string id in notIncluded)
                if(string.IsNullOrWhiteSpace(id) || next.ContainsKey(id) || !missing.Add(id))
                    throw new InvalidOperationException($"Разбитая графика ламп {name}: неверное исключение {id}.");
            index=next;omitted=missing;
        }
    }
}
