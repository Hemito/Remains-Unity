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
        public string recovery;           // a recovery point: the world template (part lists as markers) of this tick
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
        internal static bool Slow(int index)=>Names[index]=="actors" || Names[index]=="projectiles";
        // A receiver's working copy: the same part objects in new lists (a tick then replaces only its parts there).
        public static WorldState ShallowCopy(WorldState world)
        {
            var copy=new WorldState();
            foreach(var field in WorldFields) field.SetValue(copy,field.GetValue(world));
            foreach(var field in ListFields) field.SetValue(copy,Activator.CreateInstance(field.FieldType,field.GetValue(world)));
            return copy;
        }
        internal static Type ItemType(int index)=>ListFields[index].FieldType.GetGenericArguments()[0];
        internal static string Key(string list,object item,int position)=>list+":"+(SnapshotPartCache.PartId(item)??"#"+position);
        // The world JSON with each part list as its SnapshotPartCache marker (a composition's template).
        public static string Template(WorldState world)=>StateJson.ToJson(world,MarkerLists);
        private static readonly JsonConverter[] MarkerLists={new MarkerList()};
        // Converter checks run for every value written; a set lookup instead of a scan.
        private static readonly HashSet<Type> ListTypes=new(Array.ConvertAll(ListFields,field=>field.FieldType));
        private sealed class MarkerList : JsonConverter
        {
            public override bool CanRead=>false;
            public override bool CanConvert(Type type)=>ListTypes.Contains(type);
            public override object ReadJson(JsonReader reader,Type type,object existing,JsonSerializer serializer)=>throw new NotSupportedException();
            public override void WriteJson(JsonWriter writer,object value,JsonSerializer serializer)
            {
                var field=Array.Find(ListFields,f=>f.FieldType==value.GetType());
                writer.WriteRawValue(SnapshotPartCache.Marker(field.Name));
            }
        }
        // The world with every part list written as [] (the skeleton both sides compare and apply).
        public static string Skeleton(WorldState world)=>StateJson.ToJson(world,EmptyLists);
        // The same without the per-frame fields that have their own messages (level clocks, moving platforms, grenade
        // flights): it tells whether a fast tick must carry the skeleton; a slow tick compares the whole one.
        public static string StableSkeleton(WorldState world)=>StateJson.ToJson(world,EmptyStable);
        private static readonly HashSet<Type> Volatile=new() {typeof(List<LevelSimulationTime>),typeof(List<MovingPlatformState>),typeof(List<GrenadeState>)};
        private static readonly JsonConverter[] EmptyLists={new Empty(false)};
        private static readonly JsonConverter[] EmptyStable={new Empty(true)};
        private sealed class Empty : JsonConverter
        {
            private readonly bool volatileToo;
            public Empty(bool volatileToo) {this.volatileToo=volatileToo;}
            public override bool CanRead=>false;
            public override bool CanConvert(Type type)=>ListTypes.Contains(type) || volatileToo && Volatile.Contains(type);
            public override object ReadJson(JsonReader reader,Type type,object existing,JsonSerializer serializer)=>throw new NotSupportedException();
            public override void WriteJson(JsonWriter writer,object value,JsonSerializer serializer) {writer.WriteStartArray();writer.WriteEndArray();}
        }
    }

    internal sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance=new();
        public new bool Equals(object a,object b)=>ReferenceEquals(a,b);
        public int GetHashCode(object value)=>System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
    }
    // Host side: remembers each list's objects, keys and texts of the previous tick.
    public sealed class WorldTickComposer
    {
        private sealed class ListMemory {public object[] Objects=Array.Empty<object>();public string[] Keys=Array.Empty<string>(),Texts=Array.Empty<string>();public long[] Revisions=Array.Empty<long>();public bool[] Finished=Array.Empty<bool>();public uint[] Hashes=Array.Empty<uint>();}
        private readonly ListMemory[] memory=new ListMemory[WorldTickLists.Names.Length];
        private string worldId,skeleton,stable;
        private long stableRevision=long.MinValue;
        private long seq;
        public long Seq=>seq;
        public int LastChangedParts {get;private set;}
        // Marked parts this call serialized (the caller unmarks them: this composer is the marks' only consumer).
        public readonly List<object> Consumed=new();
        // Motion-marked actors this call serialized (the caller drops their motion marks).
        public readonly List<object> MotionConsumed=new();
        // A motion-only change (WorldChanges.MarkMotion: the sample already reached clients in its PoseMessage) is
        // refreshed in one call of MotionSlots by the key's slot, fast or slow: each moving actor reaches the
        // canonical copies within MotionSlots ticks (about a second) and no tick carries every moving actor at once.
        public const int MotionSlots=20;
        private long passes;
        private bool InSlot(uint hash)=>((ulong)hash+(ulong)passes)%MotionSlots==0;
        private static uint Hash(string key)=>(uint)StringComparer.Ordinal.GetHashCode(key);
        // Rolling audit (as SnapshotPartCache did): each slow tick re-serializes the unmarked parts of one slot in
        // AuditEvery; a changed text is a mutation without WorldChanges.Mark, reported and sent.
        public const int AuditEvery=30;
        // Passes per slow tick in the session cadence (50 ms ticks, 1 s slow ticks): the lists diffed by every tick
        // audit one slot of AuditEvery*SlowEvery per pass, so no slow tick audits a thirtieth of the world at once.
        public const int SlowEvery=20;
        private long round;
        public int LastUnmarked {get;private set;}
        private static readonly HashSet<string> warnedUnmarked=new(StringComparer.Ordinal);
        // The world at the last tick as a snapshot composition: the template and the remembered part texts.
        public SnapshotPartCache.Composition Composition(string template)
        {
            if(worldId==null) throw new InvalidOperationException("Состав тиков ещё не видел мир.");
            var sets=new SnapshotPartCache.PartSet[memory.Length];int parts=0;
            for(int i=0;i<memory.Length;i++) {sets[i]=new SnapshotPartCache.PartSet(WorldTickLists.Names[i],memory[i].Keys,memory[i].Texts);parts+=memory[i].Keys.Length;}
            Array.Sort(sets,(a,b)=>template.IndexOf(SnapshotPartCache.Marker(a.Name),StringComparison.Ordinal).CompareTo(template.IndexOf(SnapshotPartCache.Marker(b.Name),StringComparison.Ordinal)));
            return new SnapshotPartCache.Composition(template,sets,parts,0);
        }

        public WorldTickComposer() {for(int i=0;i<memory.Length;i++) memory[i]=new ListMemory();}

        // The first call (or a new world) remembers the world and returns null: the receiver starts from a full copy.
        // Afterwards: the tick since the previous call, or null when nothing changed.
        // slow=false skips the lists that have their own fast messages (actors: ActorMessage/PoseMessage, projectiles:
        // their flight messages); their state still reaches the receiver's world on the next slow tick. A fast tick
        // still refreshes the actors of its motion slot (motionMarked).
        public WorldTick Next(WorldState world,Func<object,bool> marked,bool slow=true,bool force=false,Func<object,bool> motionMarked=null)
        {
            if(world==null) throw new ArgumentNullException(nameof(world));
            bool fresh=worldId!=world.worldId;
            if(fresh) slow=true;
            var tick=new WorldTick {world=world.worldId,seq=seq+1};
            int changed=0;Consumed.Clear();MotionConsumed.Clear();LastUnmarked=0;
            if(slow) round++;
            passes++;
            for(int index=0;index<memory.Length;index++)
            {
                var list=WorldTickLists.List(world,index);
                WorldTickList entry;
                if(!slow && WorldTickLists.Slow(index))
                {
                    if(WorldTickLists.Names[index]!="actors" || motionMarked==null || fresh) continue;
                    entry=RefreshMotion(list,memory[index],marked,motionMarked,ref changed);
                }
                else entry=Diff(WorldTickLists.Names[index],list,memory[index],fresh,marked,motionMarked,slow,ref changed);
                if(entry!=null) tick.lists.Add(entry);
            }
            // The skeleton travels whole; a fast tick sends it only when more than the per-frame fields changed, and
            // looks only after a world revision (shared operations commit one); anything else reaches the slow tick.
            if(slow || world.revision!=stableRevision)
            {
                string nextStable=WorldTickLists.StableSkeleton(world);
                if(slow || nextStable!=stable)
                {
                    string nextSkeleton=WorldTickLists.Skeleton(world);
                    if(nextSkeleton!=skeleton) {tick.skeleton=nextSkeleton;skeleton=nextSkeleton;}
                    stable=nextStable;
                }
                stableRevision=world.revision;
            }
            LastChangedParts=changed;
            if(fresh) {worldId=world.worldId;seq=0;tick.seq=1;if(!force) return null;}
            if(tick.skeleton==null && tick.lists.Count==0 && !force) return null;
            seq=tick.seq;
            return tick;
        }

        private bool Audited(bool slowList,uint hash,bool slow)=>slowList?slow && ((ulong)hash+(ulong)round)%AuditEvery==0:
            ((ulong)hash+(ulong)passes)%(AuditEvery*SlowEvery)==0;
        private void ReportUnmarked(string key,string was,string now)
        {
            LastUnmarked++;
            int at=0;while(at<was.Length && at<now.Length && was[at]==now[at]) at++;
            int quote=was.LastIndexOf("\":",Math.Max(0,at-1),StringComparison.Ordinal);
            int start=quote<0?-1:was.LastIndexOf('"',Math.Max(0,quote-1));
            string field=start<0?"?":was.Substring(start+1,quote-start-1);
            if(warnedUnmarked.Add(key.Substring(0,key.IndexOf(':'))+"."+field))
                UnityEngine.Debug.LogWarning($"Непомеченное изменение части мира (аудит тиков): {key}, поле {field} — нет WorldChanges.Mark в месте изменения; часть отправлена.");
        }
        // Fast tick: the motion-slot actors of an unchanged actor list. Marked, revised or replaced actors wait for the
        // slow tick, which serializes them whole (their state keeps the timing it had).
        private WorldTickList RefreshMotion(System.Collections.IList list,ListMemory before,Func<object,bool> marked,Func<object,bool> motionMarked,ref int changed)
        {
            int count=list.Count;
            if(before.Objects.Length!=count || before.Revisions.Length!=count || before.Hashes.Length!=count) return null;
            for(int i=0;i<count;i++) if(!ReferenceEquals(before.Objects[i],list[i])) return null;
            string[] texts=null;List<string> changedKeys=null,changedTexts=null;
            for(int i=0;i<count;i++)
            {
                var item=list[i];
                if(item==null || !InSlot(before.Hashes[i]) || !motionMarked(item) || marked(item) || ((ActorState)item).revision!=before.Revisions[i]) continue;
                MotionConsumed.Add(item);
                string text=StateJson.ToJson(item);
                if(text==before.Texts[i]) continue;
                texts??=(string[])before.Texts.Clone();   // a returned composition may still hold the previous array
                texts[i]=text;
                (changedKeys??=new List<string>()).Add(before.Keys[i]);(changedTexts??=new List<string>()).Add(text);
            }
            if(texts==null) return null;
            before.Texts=texts;changed+=changedKeys.Count;
            return new WorldTickList {name="actors",keys=changedKeys.ToArray(),texts=changedTexts.ToArray()};
        }
        private WorldTickList Diff(string name,System.Collections.IList list,ListMemory before,bool fresh,Func<object,bool> marked,Func<object,bool> motionMarked,bool slow,ref int changed)
        {
            int count=list.Count;
            var objects=new object[count];
            bool always=SnapshotPartCache.AlwaysSerialized(name);
            // Same objects at the same positions (the usual tick): only marked parts are serialized again.
            bool aligned=!fresh && before.Objects.Length==count;
            for(int i=0;aligned && i<count;i++) aligned=ReferenceEquals(before.Objects[i],list[i]);
            var keys=aligned?before.Keys:new string[count];
            var hashes=aligned?before.Hashes:new uint[count];
            bool slowList=WorldTickLists.Slow(Array.IndexOf(WorldTickLists.Names,name));
            var texts=aligned?(string[])before.Texts.Clone():new string[count];
            var changedKeys=new List<string>();var changedTexts=new List<string>();
            // Actors also change through their own revision (ownership, health, generation) without a mark, as the
            // snapshot composition treats them (SnapshotPartCache.ComposeActor).
            bool actors=name=="actors";
            var revisions=actors?new long[count]:null;
            // Flights: serialized while flying and once more when they finish (the landing state), then no longer.
            var finished=always?new bool[count]:null;
            Dictionary<string,string> previousText=null;
            Dictionary<object,int> previousIndex=null;
            if(!aligned && !fresh)
            {
                previousText=new Dictionary<string,string>(before.Keys.Length,StringComparer.Ordinal);
                for(int i=0;i<before.Keys.Length;i++) previousText[before.Keys[i]]=before.Texts[i];
                // The same object elsewhere in the list keeps its text unless marked or revised (no re-serialization of a
                // whole list because one actor object was replaced).
                previousIndex=new Dictionary<object,int>(before.Objects.Length,ReferenceComparer.Instance);
                for(int i=0;i<before.Objects.Length;i++) if(before.Objects[i]!=null) previousIndex[before.Objects[i]]=i;
            }
            for(int i=0;i<count;i++)
            {
                var item=list[i];objects[i]=item;
                if(actors) revisions[i]=((ActorState)item)?.revision??0;
                if(always) finished[i]=item is ProjectileState done && done.finished;
                if(aligned)
                {
                    bool revised=actors && before.Revisions.Length==count && revisions[i]!=before.Revisions[i];
                    bool isMarked=item!=null && marked(item);
                    if(isMarked) Consumed.Add(item);
                    // Flights progress without marks; a finished flight no longer changes.
                    bool flying=always && !(finished[i] && before.Finished.Length==count && before.Finished[i]);
                    // A motion-only change waits for its slot; its stale text is not an unmarked change.
                    bool moved=actors && item!=null && motionMarked!=null && motionMarked(item);
                    bool refresh=moved && InSlot(hashes[i]);
                    bool audited=!isMarked && !revised && !flying && !moved && Audited(slowList,hashes[i],slow);
                    if(!flying && !revised && !isMarked && !audited && !refresh) continue;
                    if(moved) MotionConsumed.Add(item);
                    string text=item==null?"null":StateJson.ToJson(item);
                    if(text==texts[i]) continue;
                    if(audited) ReportUnmarked(keys[i],texts[i],text);
                    texts[i]=text;changedKeys.Add(keys[i]);changedTexts.Add(text);
                    continue;
                }
                bool markedItem=item!=null && marked(item);
                if(markedItem) Consumed.Add(item);
                keys[i]=WorldTickLists.Key(name,item,i);hashes[i]=Hash(keys[i]);
                int at=-1;
                bool known=!fresh && item!=null && previousIndex.TryGetValue(item,out at);
                bool flyingItem=always && !(finished[i] && known && before.Finished.Length>at && before.Finished[at]);
                bool movedItem=actors && item!=null && motionMarked!=null && motionMarked(item);
                if(known && !flyingItem && !markedItem && !(movedItem && InSlot(hashes[i])) &&
                    (!actors || before.Revisions.Length>at && before.Revisions[at]==revisions[i]) && before.Keys[at]==keys[i])
                {texts[i]=before.Texts[at];continue;}
                if(movedItem) MotionConsumed.Add(item);
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
            before.Objects=objects;before.Keys=keys;before.Hashes=hashes;before.Texts=texts;if(actors) before.Revisions=revisions;if(always) before.Finished=finished;
            changed+=changedKeys.Count;
            if(fresh || changedKeys.Count==0 && removed==null && order==null) return null;
            return new WorldTickList {name=name,keys=changedKeys.ToArray(),texts=changedTexts.ToArray(),removed=removed,order=order};
        }
    }

    // Receiver side: the part texts of a world kept by its ticks, so a recovery copy is composed exactly as the host's
    // (the same texts and template) without serializing the world.
    public sealed class WorldTickTexts
    {
        private readonly List<string>[] keys=new List<string>[WorldTickLists.Names.Length];
        private readonly Dictionary<string,string>[] texts=new Dictionary<string,string>[WorldTickLists.Names.Length];
        public WorldTickTexts(WorldState world)
        {
            var split=SnapshotPartCache.Split(world);
            foreach(var set in split.Lists)
            {
                int index=Array.IndexOf(WorldTickLists.Names,set.Name);
                keys[index]=new List<string>(set.Keys);texts[index]=new Dictionary<string,string>(set.Keys.Length,StringComparer.Ordinal);
                for(int i=0;i<set.Keys.Length;i++) texts[index][set.Keys[i]]=set.Texts[i];
            }
        }
        public void Apply(WorldTick tick)
        {
            foreach(var entry in tick.lists)
            {
                int index=Array.IndexOf(WorldTickLists.Names,entry.name);
                if(entry.removed!=null) foreach(string key in entry.removed) texts[index].Remove(key);
                for(int i=0;i<(entry.keys?.Length??0);i++) texts[index][entry.keys[i]]=entry.texts[i];
                if(entry.order!=null) {keys[index].Clear();keys[index].AddRange(entry.order);}
            }
        }
        public SnapshotPartCache.Composition Composition(string template)
        {
            var sets=new SnapshotPartCache.PartSet[keys.Length];int parts=0;
            for(int i=0;i<keys.Length;i++)
            {
                var list=keys[i];var values=new string[list.Count];
                for(int k=0;k<values.Length;k++)
                    if(!texts[i].TryGetValue(list[k],out values[k])) throw new System.IO.InvalidDataException("Тексты тиков: нет части "+list[k]);
                sets[i]=new SnapshotPartCache.PartSet(WorldTickLists.Names[i],list.ToArray(),values);parts+=values.Length;
            }
            Array.Sort(sets,(a,b)=>template.IndexOf(SnapshotPartCache.Marker(a.Name),StringComparison.Ordinal).CompareTo(template.IndexOf(SnapshotPartCache.Marker(b.Name),StringComparison.Ordinal)));
            return new SnapshotPartCache.Composition(template,sets,parts,0);
        }
    }

    // Receiver side: applies a tick to a world by part keys. The whole tick is checked before anything changes.
    public static class WorldTickApplier
    {
        public sealed class Parsed
        {
            internal WorldState Skeleton;
            internal readonly List<(int index,WorldTickList entry,object[] items)> Lists=new();
        }
        public static void Apply(WorldState world,WorldTick tick)=>Apply(world,Parse(world,tick),null);
        // Parse everything first: a malformed part leaves every world untouched.
        public static Parsed Parse(WorldState world,WorldTick tick)
        {
            if(world==null || tick==null) throw new ArgumentNullException(world==null?nameof(world):nameof(tick));
            if(tick.world!=world.worldId) throw new System.IO.InvalidDataException($"Тик другого мира: {tick.world}, ожидается {world.worldId}.");
            var result=new Parsed {Skeleton=tick.skeleton==null?null:StateJson.FromJson<WorldState>(tick.skeleton)};
            var parsed=result.Lists;
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
            return result;
        }
        // copy=null: the parsed objects themselves (once); otherwise detached copies (a second world from one parse).
        public static void Apply(WorldState world,Parsed tick,Func<object,Type,object> copy)
        {
            WorldState skeleton=tick.Skeleton==null?null:copy==null?tick.Skeleton:(WorldState)copy(tick.Skeleton,typeof(WorldState));
            foreach(var (index,source,parts) in tick.Lists)
            {
                var entry=source;var type=WorldTickLists.ItemType(index);
                var items=copy==null?parts:Array.ConvertAll(parts,item=>item==null?null:copy(item,type));
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
