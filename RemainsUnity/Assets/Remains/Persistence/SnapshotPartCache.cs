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
        // Diagnostics of the last composition: changed parts per list and the first changed spot of each.
        public static string LastReport {get;private set;}
        // Per composed world and list: the objects, keys and texts of its last composition, by list position. When a
        // list holds the same objects in the same order, its unmarked parts are taken by position (no per-part
        // dictionaries or key strings: ~10 ms per factory snapshot before); otherwise parts are matched by key.
        private sealed class ListMemory {public object[] Objects;public string[] Keys,Texts;public uint[] Slots;}
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<WorldState,Dictionary<string,ListMemory>> memories=new();
        private static readonly HashSet<string> warnedUnmarked=new(StringComparer.Ordinal);
        public static int LastUnmarked {get;private set;}
        // Rolling audit: each composition re-serializes the parts whose key slot matches this round (a stable hash
        // of the part key), so every part is verified once per VerifyEvery compositions without a whole-world spike.
        public const int VerifyEvery=30;
        private static long compositions;

        // A world as a template plus ordered part lists (delta replication, host-migration.md): the template is the
        // world JSON with each part list replaced by a unique marker (a raw U+0001 never appears in serializer output);
        // Splice puts the lists back, giving exactly StateJson.ToJson(world).
        public sealed class PartSet
        {
            public readonly string Name;public readonly string[] Keys,Texts;
            public PartSet(string name,string[] keys,string[] texts) {Name=name;Keys=keys;Texts=texts;}
        }
        public sealed class Composition
        {
            public readonly string Template;
            public readonly PartSet[] Lists;
            public readonly int Parts,Parsed;
            public Composition(string template,PartSet[] lists,int parts,int parsed)
            {Template=template;Lists=lists;Parts=parts;Parsed=parsed;}
            // The whole text; built on demand (RecoverySnapshot keeps it lazy, off the game thread where possible).
            public string Json=>Splice(Template,Lists);
        }
        public static readonly string[] ListNames={"containers","props","lamps","doors","actors","factories","projectiles"};
        // Lists serialized on every composition (their objects change without marks, e.g. local motion, flight
        // progress); an unchanged text is still accepted by key without a strict parse, and deltas carry only changes.
        internal static bool AlwaysSerialized(string list)=>list=="actors" || list=="projectiles";
        // Part key within its list: the stable ID (a factory by its level).
        internal static string PartId(object item)=>item switch
        {
            PropState body=>body.id,LampState lamp=>lamp.id,DoorState door=>door.id,ActorState actor=>actor.id,WorldFactory factory=>factory.level,
            ProjectileState flight=>flight.launch?.id,_=>null
        };
        internal static string ListName(Type type)=>type==typeof(ContainerState)?"containers":type==typeof(PropState)?"props":type==typeof(LampState)?"lamps":
            type==typeof(DoorState)?"doors":type==typeof(ActorState)?"actors":type==typeof(WorldFactory)?"factories":type==typeof(ProjectileState)?"projectiles":
            throw new ArgumentException("Тип не является частью мира: "+type.Name);
        public static string Marker(string list)=>""+list+"";

        // Receives the spliced text in order (Splice builds the string, Fingerprint hashes it without building it).
        public interface ITextSink {void Append(string text,int start,int count);void Append(char value);}
        private sealed class BuilderSink : ITextSink
        {
            public readonly System.Text.StringBuilder Builder;
            public BuilderSink(int capacity) {Builder=new System.Text.StringBuilder(capacity);}
            public void Append(string text,int start,int count)=>Builder.Append(text,start,count);
            public void Append(char value)=>Builder.Append(value);
        }
        public static string Splice(string template,IReadOnlyList<PartSet> lists)
        {
            int size=template.Length;
            foreach(var list in lists) foreach(string text in list.Texts) size+=text.Length+1;
            var sink=new BuilderSink(size+8);
            Write(template,lists,sink);
            return sink.Builder.ToString();
        }
        // SHA-256 of Splice(template, lists), streamed: no whole-text string (RecoverySnapshot.Fingerprint).
        public static string Fingerprint(string template,IReadOnlyList<PartSet> lists)
        {
            var hasher=TextSha256.Begin();
            Write(template,lists,hasher);
            return hasher.Finish();
        }
        // Splice(template, lists) written to a text writer without building the string (save files).
        public static void WriteTo(string template,IReadOnlyList<PartSet> lists,System.IO.TextWriter writer)=>Write(template,lists,new WriterSink(writer));
        private sealed class WriterSink : ITextSink
        {
            private readonly System.IO.TextWriter writer;
            public WriterSink(System.IO.TextWriter writer) {this.writer=writer;}
            public void Append(string text,int start,int count) {if(start==0 && count==text.Length) writer.Write(text);else writer.Write(text.ToCharArray(start,count));}
            public void Append(char value)=>writer.Write(value);
        }
        private static void Write(string template,IReadOnlyList<PartSet> lists,ITextSink sink)
        {
            int at=0;
            foreach(var list in lists)
            {
                string marker=Marker(list.Name);
                int found=template.IndexOf(marker,at,StringComparison.Ordinal);
                if(found<0) throw new System.IO.InvalidDataException("Шаблон мира без метки списка "+list.Name+".");
                sink.Append(template,at,found-at);sink.Append('[');
                for(int i=0;i<list.Texts.Length;i++) {if(i>0) sink.Append(',');string text=list.Texts[i];sink.Append(text,0,text.Length);}
                sink.Append(']');
                at=found+marker.Length;
            }
            sink.Append(template,at,template.Length-at);
        }
        // The template with empty lists: the strict check of everything outside the parts.
        public static string Skeleton(string template)
        {
            foreach(string name in ListNames) template=template.Replace(Marker(name),"[]");
            return template;
        }

        public static Composition Compose(WorldState world)
        {
            if(world==null) throw new ArgumentNullException(nameof(world));
            lock(gate)
            {
                var next=new Dictionary<Type,HashSet<string>>();
                memories.TryGetValue(world,out var memory);
                var remembered=new Dictionary<string,ListMemory>(StringComparer.Ordinal);
                var audit=new Audit(world) {Verify=memory==null,Round=++compositions};
                var lists=new JsonConverter[] {new PartList<ContainerState>(next,audit,memory,remembered),new PartList<PropState>(next,audit,memory,remembered),
                    new PartList<LampState>(next,audit,memory,remembered),new PartList<DoorState>(next,audit,memory,remembered),
                    new PartList<ActorState>(next,audit,memory,remembered),new PartList<WorldFactory>(next,audit,memory,remembered),
                    new PartList<ProjectileState>(next,audit,memory,remembered)};
                string template;
                using(Remains.Diagnostics.FrameHitchLog.Section("Снимок: части")) template=StateJson.ToJson(world,lists);
                using(Remains.Diagnostics.FrameHitchLog.Section("Снимок: скелет")) StateJson.FromJson<WorldState>(Skeleton(template));   // strict outside the parts
                int parts=0,parsed=0;var report=new System.Text.StringBuilder("снимок: изменено");
                var sets=new PartSet[lists.Length];
                for(int i=0;i<lists.Length;i++)
                {
                    var list=(IPartList)lists[i];parts+=list.Parts;parsed+=list.Parsed;report.Append(' ').Append(list.Report);
                    sets[i]=list.Set;
                }
                // Marker order in the template follows WorldState field order; Splice expects the same order.
                Array.Sort(sets,(a,b)=>template.IndexOf(Marker(a.Name),StringComparison.Ordinal).CompareTo(template.IndexOf(Marker(b.Name),StringComparison.Ordinal)));
                LastUnmarked=audit.Unmarked;
                report.Append(audit.Verify?"; сверка всех частей":"; готовых текстов "+audit.Reused+", сверено "+audit.Verified);
                if(audit.Unmarked>0) report.Append("; непомеченных изменений ").Append(audit.Unmarked).Append(" [").Append(audit.Example).Append(']');
                LastReport=report.ToString();
                accepted=next;   // only after the whole composition succeeded
                memories.AddOrUpdate(world,remembered);   // only after the whole composition succeeded
                WorldChanges.Clear(world);
                return new Composition(template,sets,parts,parsed);
            }
        }

        // Template and part lists of an already validated world (the receiver of a full snapshot builds its replica):
        // no strict parse, no audit, no shared state.
        public static (string Template,PartSet[] Lists) Split(WorldState world)
        {
            if(world==null) throw new ArgumentNullException(nameof(world));
            var lists=new JsonConverter[] {new PlainList<ContainerState>(),new PlainList<PropState>(),new PlainList<LampState>(),
                new PlainList<DoorState>(),new PlainList<ActorState>(),new PlainList<WorldFactory>(),new PlainList<ProjectileState>()};
            string template=StateJson.ToJson(world,lists);
            var sets=new PartSet[lists.Length];
            for(int i=0;i<lists.Length;i++) sets[i]=((IPlainList)lists[i]).Set;
            Array.Sort(sets,(a,b)=>template.IndexOf(Marker(a.Name),StringComparison.Ordinal).CompareTo(template.IndexOf(Marker(b.Name),StringComparison.Ordinal)));
            return (template,sets);
        }
        private interface IPlainList {PartSet Set {get;}}
        private sealed class PlainList<T> : JsonConverter,IPlainList where T:class
        {
            private readonly List<string> keys=new(),texts=new();
            public PartSet Set=>new(Name,keys.ToArray(),texts.ToArray());
            private static readonly string Name=ListName(typeof(T));
            public override bool CanRead=>false;
            public override bool CanConvert(Type objectType)=>objectType==typeof(List<T>);
            public override object ReadJson(JsonReader reader,Type objectType,object existingValue,JsonSerializer serializer)
                =>throw new NotSupportedException("Раскладка мира только записывает.");
            public override void WriteJson(JsonWriter writer,object value,JsonSerializer serializer)
            {
                writer.WriteRawValue(Marker(Name));
                int index=0;
                foreach(var item in (List<T>)value)
                {
                    string id=PartId(item);
                    keys.Add(Name+":"+(id??"#"+index));texts.Add(item==null?"null":StateJson.ToJson(item));index++;
                }
            }
        }

        // Test hook: the next composition parses every part again.
        public static void Clear() {lock(gate) accepted=new Dictionary<Type,HashSet<string>>();}

        private interface IPartList {int Parts {get;} int Parsed {get;} string Report {get;} PartSet Set {get;}}

        // A part whose text changed while the same object stayed unmarked: a mutation site without WorldChanges.Mark.
        private sealed class Audit
        {
            public readonly WorldState World;
            public readonly bool Tracked;
            public int Unmarked,Reused,Verified;public string Example;
            public bool Verify;   // this composition re-serializes everything (no previous texts of this world)
            public long Round;
            public Audit(WorldState world) {World=world;Tracked=WorldChanges.IsTracked(world);}
            // Rolling audit slot: follows the part's key, not its list position (removals before a part would shift
            // its index together with Round and could keep it out of the audit forever; Codex review 2026-10-15).
            public static uint SlotOf(string key)=>(uint)StringComparer.Ordinal.GetHashCode(key);
            public bool VerifyNow(uint slot)
            {
                if(Verify || (slot+(ulong)Round)%VerifyEvery!=0) return false;
                Verified++;return true;
            }
            public bool Marked(object part)=>WorldChanges.IsMarked(World,part);
            // A part that is the same object as before changed its text without a mark: a mutation site misses
            // WorldChanges.Mark. Always-serialized parts change unmarked by design and are not reported.
            public void ReportUnmarked(string key,string was,string now)
            {
                Unmarked++;
                string field=Field(was,now);
                Example??=key+" ("+field+")";
                if(warnedUnmarked.Add(key.Substring(0,key.IndexOf(':'))+"."+field))
                    UnityEngine.Debug.LogWarning($"Непомеченное изменение части мира: {key}, поле {field} (нет WorldChanges.Mark в месте изменения). Было: {Around(was,now)}");
            }
            // The JSON key whose value holds the first difference.
            private static string Field(string was,string now)
            {
                int at=0;while(at<was.Length && at<now.Length && was[at]==now[at]) at++;
                int quote=was.LastIndexOf("\":",Math.Max(0,at-1),StringComparison.Ordinal);
                if(quote<0) return "?";
                int start=was.LastIndexOf('"',Math.Max(0,quote-1));
                return start<0?"?":was.Substring(start+1,quote-start-1);
            }
            private static string Around(string was,string now)
            {
                int at=0;while(at<was.Length && at<now.Length && was[at]==now[at]) at++;
                int from=Math.Max(0,at-40);
                return was.Substring(from,Math.Min(80,was.Length-from))+" → "+now.Substring(from,Math.Min(80,now.Length-from));
            }
        }

        // One declared list type (List<T> field of WorldState): writes its marker into the template, collects the part texts.
        private sealed class PartList<T> : JsonConverter,IPartList where T:class
        {
            private readonly Dictionary<Type,HashSet<string>> next;
            private readonly Audit audit;
            private readonly Dictionary<string,ListMemory> memory,remembered;
            public int Parts {get;private set;}
            public int Parsed {get;private set;}
            private string example;
            public string Report=>typeof(T).Name+" "+Parsed+"/"+Parts+(example==null?"":" ["+example+"]");
            private string[] keys=Array.Empty<string>(),texts=Array.Empty<string>();
            public PartSet Set=>new(Name,keys,texts);
            private static readonly string Name=ListName(typeof(T));
            private static readonly bool Always=AlwaysSerialized(Name);
            public PartList(Dictionary<Type,HashSet<string>> next,Audit audit=null,Dictionary<string,ListMemory> memory=null,Dictionary<string,ListMemory> remembered=null)
            {this.next=next;this.audit=audit;this.memory=memory;this.remembered=remembered;}
            public override bool CanRead=>false;
            public override bool CanConvert(Type objectType)=>objectType==typeof(List<T>);
            public override object ReadJson(JsonReader reader,Type objectType,object existingValue,JsonSerializer serializer)
                =>throw new NotSupportedException("Кэш частей снимка только записывает.");
            public override void WriteJson(JsonWriter writer,object value,JsonSerializer serializer)
            {
                var list=(List<T>)value;
                // The part list stays out of the template: a marker the splice replaces with the parts.
                writer.WriteRawValue(Marker(Name));
                accepted.TryGetValue(typeof(T),out var previous);
                ListMemory before=null;memory?.TryGetValue(Name,out before);
                bool reuse=audit!=null && audit.Tracked && !audit.Verify && before!=null;
                int count=list.Count;
                // Same objects at the same positions: parts are taken by position.
                bool aligned=reuse && before.Objects.Length==count;
                for(int i=0;aligned && i<count;i++) aligned=ReferenceEquals(before.Objects[i],list[i]);
                // Texts accepted by this composition that were not taken from the previous one (changed and new
                // parts): a later composition skips their strict check. Unchanged parts need no set (taken by
                // position or key); a set carried over would grow with every changed actor or body text.
                var current=new HashSet<string>(StringComparer.Ordinal);
                next[typeof(T)]=current;
                Dictionary<string,(object part,string text)> byKey=null;
                if(reuse && !aligned)
                {
                    byKey=new Dictionary<string,(object part,string text)>(before.Keys.Length,StringComparer.Ordinal);
                    for(int i=0;i<before.Keys.Length;i++) if(before.Objects[i]!=null) byKey[before.Keys[i]]=(before.Objects[i],before.Texts[i]);
                }
                var objects=new object[count];keys=new string[count];texts=new string[count];var slots=new uint[count];
                for(int i=0;i<count;i++)
                {
                    var item=list[i];objects[i]=item;
                    string key,text,was=null;uint slot;bool known=false;
                    if(aligned && item!=null)
                    {
                        key=before.Keys[i];slot=before.Slots[i];was=before.Texts[i];known=true;
                        if(!Always && !audit.VerifyNow(slot) && !audit.Marked(item)) {text=was;audit.Reused++;}
                        else
                        {
                            string id=PartId(item);
                            string fresh=Name+":"+(id??"#"+i);
                            if(fresh!=key) {key=fresh;slot=Audit.SlotOf(key);}
                            text=StateJson.ToJson(item);
                        }
                    }
                    else
                    {
                        string id=PartId(item);
                        key=Name+":"+(id??"#"+i);slot=Audit.SlotOf(key);
                        if(byKey!=null && item!=null && id!=null && byKey.TryGetValue(key,out var old) && ReferenceEquals(old.part,item))
                        {was=old.text;known=true;}
                        if(known && !Always && !audit.VerifyNow(slot) && !audit.Marked(item)) {text=was;audit.Reused++;}
                        else text=item==null?"null":StateJson.ToJson(item);
                    }
                    if(item!=null && !ReferenceEquals(text,was))
                    {
                        bool same=known && string.Equals(text,was,StringComparison.Ordinal);
                        // A text not accepted before gets the strict check the whole-world parse applies to it.
                        if(!same && !(previous?.Contains(text)??false) && !current.Contains(text))
                        {
                            StateJson.FromJson<T>(text);
                            Parsed++;
                            if(example==null && was!=null) example=Difference(was,text);
                        }
                        if(known && !same && !Always && !audit.Marked(item)) audit.ReportUnmarked(key,was,text);
                        current.Add(text);
                    }
                    keys[i]=key;texts[i]=text;slots[i]=slot;Parts++;
                }
                remembered?.Add(Name,new ListMemory {Objects=objects,Keys=keys,Texts=texts,Slots=slots});
            }

            private static string Difference(string was,string now)
            {
                int at=0;while(at<was.Length && at<now.Length && was[at]==now[at]) at++;
                int from=Math.Max(0,at-40);
                return was.Substring(from,Math.Min(70,was.Length-from))+" → "+now.Substring(from,Math.Min(70,now.Length-from));
            }
        }
    }
}
