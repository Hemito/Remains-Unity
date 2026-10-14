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
                using(Remains.Diagnostics.FrameHitchLog.Section("Снимок: скелет"))
                {
                    string skeleton=StateJson.ToJson(world,new JsonConverter[] {new PartList<ContainerState>(null),new PartList<PropState>(null),new PartList<LampState>(null)});
                    StateJson.FromJson<WorldState>(skeleton);   // strict: everything outside the cached lists
                }
                var next=new Dictionary<Type,HashSet<string>>();
                partTexts.TryGetValue(world,out var previousTexts);
                var audit=new Audit(world,previousTexts,new Dictionary<string,(object part,string text)>(StringComparer.Ordinal));
                var lists=new JsonConverter[] {new PartList<ContainerState>(next,audit),new PartList<PropState>(next,audit),new PartList<LampState>(next,audit)};
                string json;
                using(Remains.Diagnostics.FrameHitchLog.Section("Снимок: части")) json=StateJson.ToJson(world,lists);
                int parts=0,parsed=0;var report=new System.Text.StringBuilder("снимок: изменено");
                foreach(IPartList list in lists) {parts+=list.Parts;parsed+=list.Parsed;report.Append(' ').Append(list.Report);}
                LastUnmarked=audit.Unmarked;
                if(audit.Unmarked>0) report.Append("; непомеченных изменений ").Append(audit.Unmarked).Append(" [").Append(audit.Example).Append(']');
                LastReport=report.ToString();
                accepted=next;   // only after the whole composition succeeded
                partTexts.AddOrUpdate(world,audit.Current);
                WorldChanges.Clear(world);
                return new Composition(json,parts,parsed);
            }
        }

        // Test hook: the next composition parses every part again.
        public static void Clear() {lock(gate) accepted=new Dictionary<Type,HashSet<string>>();}

        private interface IPartList {int Parts {get;} int Parsed {get;} string Report {get;}}

        // A part whose text changed while the same object stayed unmarked: a mutation site without WorldChanges.Mark.
        private sealed class Audit
        {
            public readonly WorldState World;
            public readonly Dictionary<string,(object part,string text)> Previous,Current;
            public int Unmarked;public string Example;
            public Audit(WorldState world,Dictionary<string,(object part,string text)> previous,Dictionary<string,(object part,string text)> current)
            {World=world;Previous=previous;Current=current;}
            public void Observe(string kind,string id,object part,string text)
            {
                if(id==null) return;
                string key=kind+":"+id;
                if(Previous!=null && Previous.TryGetValue(key,out var before) && ReferenceEquals(before.part,part) && before.text!=text &&
                    !WorldChanges.IsMarked(World,part))
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

        // One declared list type (List<T> field of WorldState). Skeleton mode (next==null) writes an empty array.
        private sealed class PartList<T> : JsonConverter,IPartList where T:class
        {
            private readonly Dictionary<Type,HashSet<string>> next;
            private readonly Audit audit;
            public int Parts {get;private set;}
            public int Parsed {get;private set;}
            private string example;
            public string Report=>typeof(T).Name+" "+Parsed+"/"+Parts+(example==null?"":" ["+example+"]");
            public PartList(Dictionary<Type,HashSet<string>> next,Audit audit=null) {this.next=next;this.audit=audit;}
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
                    if(!lastTexts.TryGetValue(typeof(T),out var before)) lastTexts.Add(typeof(T),before=new List<string>());
                    int index=0;
                    foreach(var item in list)
                    {
                        string text=item==null?"null":StateJson.ToJson(item);
                        if(item!=null && !(previous?.Contains(text)??false) && !current.Contains(text))
                        {
                            StateJson.FromJson<T>(text);   // the same strict check the whole-world parse applies to it
                            Parsed++;
                            if(example==null && index<before.Count) example=Difference(before[index],text);
                        }
                        if(index<before.Count) before[index]=text; else before.Add(text);
                        if(item!=null) audit?.Observe(typeof(T).Name,item switch {PropState body=>body.id,LampState lamp=>lamp.id,_=>null},item,text);
                        index++;
                        current.Add(text);Parts++;
                        writer.WriteRawValue(text);
                    }
                }
                writer.WriteEndArray();
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
