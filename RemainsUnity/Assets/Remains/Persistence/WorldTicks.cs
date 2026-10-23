using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Remains.Domain;
using Remains.Serialization;

namespace Remains.Persistence
{
    // Transport v2 (docs/history/transport-v2-design-20261023.md): a world tick carries only the parts that changed
    // since the previous tick (marked, replaced, added or removed objects) and the world's own fields when they
    // changed. Cost follows the change: unchanged parts are found by object identity, not by serialization.
    [Serializable] public sealed class WorldTick
    {
        public string world;
        public long seq;
        public string skeleton;           // the world without its part lists (null: unchanged)
        public List<WorldTickList> lists=new();
    }
    [Serializable] public sealed class WorldTickList
    {
        public string name;
        public string[] keys,texts;        // changed or added parts
        public string[] removed;           // keys gone from the list
        public string[] order;             // the whole key order, only when membership or order changed
    }

    public static class WorldTickLists
    {
        public static readonly string[] Names=SnapshotPartCache.ListNames;
        internal static readonly FieldInfo[] ListFields=Array.ConvertAll(Names,FieldOf);
        // The world's own fields (everything but the part lists): copied from the skeleton on the receiver.
        internal static readonly FieldInfo[] WorldFields=Array.FindAll(typeof(WorldState).GetFields(BindingFlags.Instance|BindingFlags.Public),
            field=>!field.IsNotSerialized && Array.IndexOf(Names,field.Name)<0);
        private static FieldInfo FieldOf(string name)=>typeof(WorldState).GetField(name)??throw new InvalidOperationException("Нет списка мира "+name);
        internal static System.Collections.IList List(WorldState world,int index)=>(System.Collections.IList)ListFields[index].GetValue(world);
        internal static Type ItemType(int index)=>ListFields[index].FieldType.GetGenericArguments()[0];
        internal static string Key(string list,object item,int position)=>list+":"+(SnapshotPartCache.PartId(item)??"#"+position);
        // The world with every part list written as [] (the skeleton both sides compare and apply).
        public static string Skeleton(WorldState world)=>StateJson.ToJson(world,EmptyLists);
        private static readonly JsonConverter[] EmptyLists={new Empty()};
        private sealed class Empty : JsonConverter
        {
            public override bool CanRead=>false;
            public override bool CanConvert(Type type)=>Array.Exists(ListFields,field=>field.FieldType==type);
            public override object ReadJson(JsonReader reader,Type type,object existing,JsonSerializer serializer)=>throw new NotSupportedException();
            public override void WriteJson(JsonWriter writer,object value,JsonSerializer serializer) {writer.WriteStartArray();writer.WriteEndArray();}
        }
    }

    // Host side: remembers each list's objects, keys and texts of the previous tick.
    public sealed class WorldTickComposer
    {
        private sealed class ListMemory {public object[] Objects=Array.Empty<object>();public string[] Keys=Array.Empty<string>(),Texts=Array.Empty<string>();}
        private readonly ListMemory[] memory=new ListMemory[WorldTickLists.Names.Length];
        private string worldId,skeleton;
        private long seq;
        public long Seq=>seq;
        public int LastChangedParts {get;private set;}

        public WorldTickComposer() {for(int i=0;i<memory.Length;i++) memory[i]=new ListMemory();}

        // The first call (or a new world) remembers the world and returns null: the receiver starts from a full copy.
        // Afterwards: the tick since the previous call, or null when nothing changed.
        public WorldTick Next(WorldState world,Func<object,bool> marked)
        {
            if(world==null) throw new ArgumentNullException(nameof(world));
            bool fresh=worldId!=world.worldId;
            var tick=new WorldTick {world=world.worldId,seq=seq+1};
            int changed=0;
            for(int index=0;index<memory.Length;index++)
            {
                var list=WorldTickLists.List(world,index);
                var entry=Diff(WorldTickLists.Names[index],list,memory[index],fresh,marked,ref changed);
                if(entry!=null) tick.lists.Add(entry);
            }
            string nextSkeleton=WorldTickLists.Skeleton(world);
            if(nextSkeleton!=skeleton) {tick.skeleton=nextSkeleton;skeleton=nextSkeleton;}
            LastChangedParts=changed;
            if(fresh) {worldId=world.worldId;seq=0;return null;}
            if(tick.skeleton==null && tick.lists.Count==0) return null;
            seq=tick.seq;
            return tick;
        }

        private static WorldTickList Diff(string name,System.Collections.IList list,ListMemory before,bool fresh,Func<object,bool> marked,ref int changed)
        {
            int count=list.Count;
            var objects=new object[count];
            bool always=SnapshotPartCache.AlwaysSerialized(name);
            // Same objects at the same positions (the usual tick): only marked parts are serialized again.
            bool aligned=!fresh && before.Objects.Length==count;
            for(int i=0;aligned && i<count;i++) aligned=ReferenceEquals(before.Objects[i],list[i]);
            var keys=aligned?before.Keys:new string[count];
            var texts=aligned?(string[])before.Texts.Clone():new string[count];
            var changedKeys=new List<string>();var changedTexts=new List<string>();
            Dictionary<string,string> previousText=null;
            if(!aligned && !fresh)
            {
                previousText=new Dictionary<string,string>(before.Keys.Length,StringComparer.Ordinal);
                for(int i=0;i<before.Keys.Length;i++) previousText[before.Keys[i]]=before.Texts[i];
            }
            for(int i=0;i<count;i++)
            {
                var item=list[i];objects[i]=item;
                if(aligned)
                {
                    if(!always && !marked(item)) continue;
                    string text=item==null?"null":StateJson.ToJson(item);
                    if(text==texts[i]) continue;
                    texts[i]=text;changedKeys.Add(keys[i]);changedTexts.Add(text);
                    continue;
                }
                keys[i]=WorldTickLists.Key(name,item,i);
                texts[i]=item==null?"null":StateJson.ToJson(item);
                if(fresh) continue;
                if(previousText!=null && previousText.TryGetValue(keys[i],out var was) && was==texts[i]) continue;
                changedKeys.Add(keys[i]);changedTexts.Add(texts[i]);
            }
            string[] removed=null,order=null;
            if(!aligned && !fresh)
            {
                var present=new HashSet<string>(keys,StringComparer.Ordinal);
                var gone=new List<string>();
                foreach(string key in before.Keys) if(!present.Contains(key)) gone.Add(key);
                if(gone.Count>0) removed=gone.ToArray();
                bool sameOrder=before.Keys.Length==keys.Length;
                for(int i=0;sameOrder && i<keys.Length;i++) sameOrder=before.Keys[i]==keys[i];
                if(!sameOrder) order=keys;
            }
            before.Objects=objects;before.Keys=keys;before.Texts=texts;
            changed+=changedKeys.Count;
            if(fresh || changedKeys.Count==0 && removed==null && order==null) return null;
            return new WorldTickList {name=name,keys=changedKeys.ToArray(),texts=changedTexts.ToArray(),removed=removed,order=order};
        }
    }

    // Receiver side: applies a tick to a world by part keys. The whole tick is checked before anything changes.
    public static class WorldTickApplier
    {
        public static void Apply(WorldState world,WorldTick tick)
        {
            if(world==null || tick==null) throw new ArgumentNullException(world==null?nameof(world):nameof(tick));
            if(tick.world!=world.worldId) throw new System.IO.InvalidDataException($"Тик другого мира: {tick.world}, ожидается {world.worldId}.");
            // Parse everything first: a malformed part leaves the world untouched.
            WorldState skeleton=tick.skeleton==null?null:StateJson.FromJson<WorldState>(tick.skeleton);
            var parsed=new List<(int index,WorldTickList entry,object[] items)>();
            foreach(var entry in tick.lists)
            {
                int index=Array.IndexOf(WorldTickLists.Names,entry.name);
                if(index<0) throw new System.IO.InvalidDataException("Тик: неизвестный список мира "+entry.name);
                if((entry.keys?.Length??0)!=(entry.texts?.Length??0)) throw new System.IO.InvalidDataException("Тик: ключи и тексты списка "+entry.name+" не совпадают.");
                var type=WorldTickLists.ItemType(index);
                var items=new object[entry.keys?.Length??0];
                for(int i=0;i<items.Length;i++) items[i]=entry.texts[i]=="null"?null:StateJson.FromJson(entry.texts[i],type);
                parsed.Add((index,entry,items));
            }
            foreach(var (index,entry,items) in parsed)
            {
                var list=WorldTickLists.List(world,index);
                var byKey=new Dictionary<string,object>(list.Count,StringComparer.Ordinal);
                for(int i=0;i<list.Count;i++) byKey[WorldTickLists.Key(entry.name,list[i],i)]=list[i];
                if(entry.removed!=null) foreach(string key in entry.removed) byKey.Remove(key);
                var added=new List<string>();
                for(int i=0;i<items.Length;i++) {if(!byKey.ContainsKey(entry.keys[i])) added.Add(entry.keys[i]);byKey[entry.keys[i]]=items[i];}
                if(entry.order!=null)
                {
                    list.Clear();
                    foreach(string key in entry.order)
                    {
                        if(!byKey.TryGetValue(key,out var item)) throw new System.IO.InvalidDataException("Тик: порядок списка "+entry.name+" ссылается на отсутствующую часть "+key);
                        list.Add(item);
                    }
                    continue;
                }
                if(added.Count>0) throw new System.IO.InvalidDataException("Тик: новые части списка "+entry.name+" без порядка: "+added[0]);
                for(int i=0;i<list.Count;i++)
                {
                    string key=WorldTickLists.Key(entry.name,list[i],i);
                    if(byKey.TryGetValue(key,out var item)) list[i]=item;
                }
            }
            if(skeleton!=null) foreach(var field in WorldTickLists.WorldFields) field.SetValue(world,field.GetValue(skeleton));
        }
    }
}
