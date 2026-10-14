using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Remains.Domain;
using Remains.Serialization;

namespace Remains.Persistence
{
    // Composes a world snapshot from its large per-element lists (containers, props, lamps: ~93% of a factory
    // world) without the strict round-trip parse of every element on every checkpoint. Each element is still
    // serialized; its text is strictly parsed (StateJson.FromJson, as the item type of its list) only when that
    // exact text was not accepted by the previous composition. The rest of the world (the skeleton: the same JSON
    // with those lists empty) is strictly parsed every time. The composed text is byte-identical to
    // StateJson.ToJson(world): element serialization does not depend on its position in the document.
    public static class SnapshotPartCache
    {
        private static readonly object gate=new();
        private static Dictionary<Type,HashSet<string>> accepted=new();   // texts accepted by the last composition

        public readonly struct Composition
        {
            public readonly string Json;
            public readonly int Parts,Parsed;
            public Composition(string json,int parts,int parsed) {Json=json;Parts=parts;Parsed=parsed;}
        }

        public static Composition Compose(WorldState world)
        {
            if(world==null) throw new ArgumentNullException(nameof(world));
            lock(gate)
            {
                string skeleton=StateJson.ToJson(world,new JsonConverter[] {new PartList<ContainerState>(null),new PartList<PropState>(null),new PartList<LampState>(null)});
                StateJson.FromJson<WorldState>(skeleton);   // strict: everything outside the cached lists
                var next=new Dictionary<Type,HashSet<string>>();
                var lists=new JsonConverter[] {new PartList<ContainerState>(next),new PartList<PropState>(next),new PartList<LampState>(next)};
                string json=StateJson.ToJson(world,lists);
                int parts=0,parsed=0;
                foreach(IPartList list in lists) {parts+=list.Parts;parsed+=list.Parsed;}
                accepted=next;   // only after the whole composition succeeded
                return new Composition(json,parts,parsed);
            }
        }

        // Test hook: the next composition parses every part again.
        public static void Clear() {lock(gate) accepted=new Dictionary<Type,HashSet<string>>();}

        private interface IPartList {int Parts {get;} int Parsed {get;}}

        // One declared list type (List<T> field of WorldState). Skeleton mode (next==null) writes an empty array.
        private sealed class PartList<T> : JsonConverter,IPartList where T:class
        {
            private readonly Dictionary<Type,HashSet<string>> next;
            public int Parts {get;private set;}
            public int Parsed {get;private set;}
            public PartList(Dictionary<Type,HashSet<string>> next) {this.next=next;}
            public override bool CanRead=>false;
            public override bool CanConvert(Type objectType)=>objectType==typeof(List<T>);
            public override object ReadJson(JsonReader reader,Type objectType,object existingValue,JsonSerializer serializer)
                =>throw new NotSupportedException("Кэш частей снимка только записывает.");
            public override void WriteJson(JsonWriter writer,object value,JsonSerializer serializer)
            {
                var list=(List<T>)value;
                writer.WriteStartArray();
                if(next!=null)
                {
                    accepted.TryGetValue(typeof(T),out var previous);
                    if(!next.TryGetValue(typeof(T),out var current)) next.Add(typeof(T),current=new HashSet<string>(StringComparer.Ordinal));
                    foreach(var item in list)
                    {
                        string text=item==null?"null":StateJson.ToJson(item);
                        if(item!=null && !(previous?.Contains(text)??false) && !current.Contains(text))
                        {
                            StateJson.FromJson<T>(text);   // the same strict check the whole-world parse applies to it
                            Parsed++;
                        }
                        current.Add(text);Parts++;
                        writer.WriteRawValue(text);
                    }
                }
                writer.WriteEndArray();
            }
        }
    }
}
