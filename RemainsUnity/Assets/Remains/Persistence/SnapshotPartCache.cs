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
        private static readonly Dictionary<Type,List<string>> lastTexts=new();   // diagnostics: previous text by list position
        // Diagnostics of the last composition: changed parts per list and the first changed spot of each.
        public static string LastReport {get;private set;}
        // Stage 1 audit (WorldChanges): previous text of each part by ID, per composed world.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<WorldState,Dictionary<string,(object part,string text)>> partTexts=new();
        private static readonly HashSet<string> warnedUnmarked=new(StringComparer.Ordinal);
        public static int LastUnmarked {get;private set;}
        public const int VerifyEvery=10;
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
        public static readonly string[] ListNames={"containers","props","lamps","doors","actors","factories"};
        // Part key within its list: the stable ID (a factory by its level).
        internal static string PartId(object item)=>item switch
        {
            PropState body=>body.id,LampState lamp=>lamp.id,DoorState door=>door.id,ActorState actor=>actor.id,WorldFactory factory=>factory.level,_=>null
        };
        internal static string ListName(Type type)=>type==typeof(ContainerState)?"containers":type==typeof(PropState)?"props":type==typeof(LampState)?"lamps":
            type==typeof(DoorState)?"doors":type==typeof(ActorState)?"actors":type==typeof(WorldFactory)?"factories":
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
                partTexts.TryGetValue(world,out var previousTexts);
                var audit=new Audit(world,previousTexts,new Dictionary<string,(object part,string text)>(StringComparer.Ordinal))
                    {Verify=previousTexts==null || ++compositions%VerifyEvery==0};
                var lists=new JsonConverter[] {new PartList<ContainerState>(next,audit),new PartList<PropState>(next,audit),new PartList<LampState>(next,audit),
                    new PartList<DoorState>(next,audit),new PartList<ActorState>(next,audit),new PartList<WorldFactory>(next,audit)};
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
                report.Append(audit.Verify?"; сверка всех частей":"; готовых текстов "+audit.Reused);
                if(audit.Unmarked>0) report.Append("; непомеченных изменений ").Append(audit.Unmarked).Append(" [").Append(audit.Example).Append(']');
                LastReport=report.ToString();
                accepted=next;   // only after the whole composition succeeded
                partTexts.AddOrUpdate(world,audit.Current);
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
                new PlainList<DoorState>(),new PlainList<ActorState>(),new PlainList<WorldFactory>()};
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
            public readonly Dictionary<string,(object part,string text)> Previous,Current;
            public int Unmarked,Reused;public string Example;
            public bool Verify;   // this composition re-serializes everything and audits the marks
            public Audit(WorldState world,Dictionary<string,(object part,string text)> previous,Dictionary<string,(object part,string text)> current)
            {World=world;Previous=previous;Current=current;}
            public bool Reuse(string kind,string id,object part,out string text)
            {
                text=null;
                if(Verify || id==null || kind==nameof(ActorState) || !WorldChanges.IsTracked(World) || Previous==null || !Previous.TryGetValue(kind+":"+id,out var before) ||
                    !ReferenceEquals(before.part,part) || WorldChanges.IsMarked(World,part)) return false;
                text=before.text;Reused++;return true;
            }
            public void Observe(string kind,string id,object part,string text)
            {
                if(id==null) return;
                string key=kind+":"+id;
                // Actors are always re-serialized (never reused), so an unmarked actor change is expected, not a gap.
                if(kind!=nameof(ActorState) && Previous!=null && Previous.TryGetValue(key,out var before) && ReferenceEquals(before.part,part) &&
                    before.text!=text && !WorldChanges.IsMarked(World,part))
                {
                    Unmarked++;
                    string field=Field(before.text,text);
                    Example??=key+" ("+field+")";
                    if(warnedUnmarked.Add(kind+"."+field))
                        UnityEngine.Debug.LogWarning($"Непомеченное изменение части мира: {key}, поле {field} (нет WorldChanges.Mark в месте изменения). Было: {Around(before.text,text)}");
                }
                Current[key]=(part,text);
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
            public int Parts {get;private set;}
            public int Parsed {get;private set;}
            private string example;
            public string Report=>typeof(T).Name+" "+Parsed+"/"+Parts+(example==null?"":" ["+example+"]");
            private readonly List<string> keys=new(),texts=new();
            public PartSet Set=>new(Name,keys.ToArray(),texts.ToArray());
            private static readonly string Name=ListName(typeof(T));
            public PartList(Dictionary<Type,HashSet<string>> next,Audit audit=null) {this.next=next;this.audit=audit;}
            public override bool CanRead=>false;
            public override bool CanConvert(Type objectType)=>objectType==typeof(List<T>);
            public override object ReadJson(JsonReader reader,Type objectType,object existingValue,JsonSerializer serializer)
                =>throw new NotSupportedException("Кэш частей снимка только записывает.");
            public override void WriteJson(JsonWriter writer,object value,JsonSerializer serializer)
            {
                var list=(List<T>)value;
                // The part list stays out of the template: a marker the splice replaces with the parts.
                writer.WriteRawValue(Marker(Name));
                {
                    accepted.TryGetValue(typeof(T),out var previous);
                    if(!next.TryGetValue(typeof(T),out var current)) next.Add(typeof(T),current=new HashSet<string>(StringComparer.Ordinal));
                    if(!lastTexts.TryGetValue(typeof(T),out var before)) lastTexts.Add(typeof(T),before=new List<string>());
                    int index=0;
                    foreach(var item in list)
                    {
                        string id=PartId(item);
                        // Stage 1 on: an unmarked part that is still the same object keeps its stored text; every
                        // VerifyEvery-th composition re-serializes all parts and reports unmarked changes (Audit).
                        string text=item==null?"null":audit!=null && audit.Reuse(typeof(T).Name,id,item,out var stored)?stored:StateJson.ToJson(item);
                        if(item!=null && !(previous?.Contains(text)??false) && !current.Contains(text))
                        {
                            StateJson.FromJson<T>(text);   // the same strict check the whole-world parse applies to it
                            Parsed++;
                            if(example==null && index<before.Count) example=Difference(before[index],text);
                        }
                        if(index<before.Count) before[index]=text; else before.Add(text);
                        if(item!=null) audit?.Observe(typeof(T).Name,id,item,text);
                        index++;
                        current.Add(text);Parts++;
                        keys.Add(Name+":"+(id??"#"+(index-1)));texts.Add(text);
                    }
                }
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
