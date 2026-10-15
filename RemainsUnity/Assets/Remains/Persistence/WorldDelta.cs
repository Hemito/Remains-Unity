using System;
using System.Collections.Generic;
using System.IO;
using Remains.Domain;
using Remains.Serialization;

namespace Remains.Persistence
{
    // Delta replication (host-migration.md, stage 2): a world revision as "base snapshot → snapshot": the new
    // template (world JSON with part-list markers), the changed or new parts, and a list's order only when it
    // changed. The receiver checks the host's JSON, streamed from template and parts without building it, against
    // the snapshot ID (SHA-256 of that JSON) and strictly parses only the template and the changed parts.
    [Serializable] public sealed class WorldDeltaPayload
    {
        public int format=1;
        public string world,baseSnapshot,snapshot;
        public string template;
        public List<WorldDeltaList> lists=new();
    }
    [Serializable] public sealed class WorldDeltaList
    {
        public string name;
        public string[] order;            // null: the base order
        public string[] keys,texts;       // changed or new parts
    }

    public static class WorldDelta
    {
        public static WorldDeltaPayload Build(string worldId,string baseId,SnapshotPartCache.Composition from,string snapshotId,SnapshotPartCache.Composition to)
        {
            if(from==null || to==null) throw new ArgumentNullException(from==null?nameof(from):nameof(to));
            var payload=new WorldDeltaPayload {world=worldId,baseSnapshot=baseId,snapshot=snapshotId,template=to.Template};
            foreach(var next in to.Lists)
            {
                var previous=Array.Find(from.Lists,list=>list.Name==next.Name);
                var before=new Dictionary<string,string>(StringComparer.Ordinal);
                if(previous!=null) for(int i=0;i<previous.Keys.Length;i++) before[previous.Keys[i]]=previous.Texts[i];
                var keys=new List<string>();var texts=new List<string>();
                for(int i=0;i<next.Keys.Length;i++)
                    if(!before.TryGetValue(next.Keys[i],out var text) || text!=next.Texts[i]) {keys.Add(next.Keys[i]);texts.Add(next.Texts[i]);}
                bool sameOrder=previous!=null && previous.Keys.Length==next.Keys.Length;
                if(sameOrder) for(int i=0;i<next.Keys.Length && sameOrder;i++) sameOrder=previous.Keys[i]==next.Keys[i];
                payload.lists.Add(new WorldDeltaList {name=next.Name,order=sameOrder?null:next.Keys,keys=keys.ToArray(),texts=texts.ToArray()});
            }
            return payload;
        }

        public static int ChangedParts(WorldDeltaPayload payload)
        {
            int count=0;foreach(var list in payload.lists) count+=list.keys?.Length??0;return count;
        }
    }

    // The receiver's copy of the host's part texts, to apply deltas. Built from a full snapshot (trusted re-split
    // of its exact text), advanced by each verified delta; any mismatch disables it until the next full snapshot.
    public sealed class WorldReplica
    {
        // Part texts in list order and each key's position: an unchanged order updates texts in place of a copy.
        private sealed class List
        {
            public string[] Keys,Texts;public Dictionary<string,int> Index;
            public static List Of(string[] keys,string[] texts)
            {
                var index=new Dictionary<string,int>(keys.Length,StringComparer.Ordinal);
                for(int i=0;i<keys.Length;i++) index[keys[i]]=i;
                return new List {Keys=keys,Texts=texts,Index=index};
            }
        }
        public string WorldId {get;private set;}
        public string SnapshotId {get;private set;}
        private string template;
        private readonly Dictionary<string,List> lists=new(StringComparer.Ordinal);

        public static WorldReplica FromSnapshot(RecoverySnapshot snapshot)
        {
            if(snapshot==null) throw new ArgumentNullException(nameof(snapshot));
            var copy=StateJson.FromTrustedJson<WorldState>(snapshot.Json);
            var split=SnapshotPartCache.Split(copy);
            if(SnapshotPartCache.Splice(split.Template,split.Lists)!=snapshot.Json)
                throw new InvalidDataException($"Снимок мира не раскладывается на части без потерь: world={snapshot.WorldId}, snapshot={snapshot.Id}.");
            var replica=new WorldReplica {WorldId=snapshot.WorldId,SnapshotId=snapshot.Id,template=split.Template};
            foreach(var set in split.Lists) replica.lists[set.Name]=List.Of(set.Keys,set.Texts);
            return replica;
        }

        // Applies a delta to this replica and returns the verified snapshot with its live world: the template and the
        // changed parts are strictly parsed; unchanged parts reuse the current live objects of the same keys.
        public RecoverySnapshot Apply(WorldDeltaPayload payload,WorldState current)
        {
            if(payload==null || payload.format!=1) throw new InvalidDataException("Неизвестный формат изменения мира.");
            if(payload.world!=WorldId || payload.baseSnapshot!=SnapshotId)
                throw new InvalidDataException($"Изменение мира от другой базы: ожидалась {SnapshotId}, получена {payload.baseSnapshot}, world={payload.world}.");
            var sets=new List<SnapshotPartCache.PartSet>();var nextLists=new Dictionary<string,List>(StringComparer.Ordinal);
            var changed=new HashSet<string>(StringComparer.Ordinal);
            foreach(var delta in payload.lists)
            {
                if(!lists.TryGetValue(delta.name,out var before)) throw new InvalidDataException("Изменение неизвестного списка мира: "+delta.name);
                if((delta.keys?.Length??0)!=(delta.texts?.Length??0)) throw new InvalidDataException("Изменение списка "+delta.name+": ключи и тексты не совпадают.");
                int count=delta.keys?.Length??0;
                for(int i=0;i<count;i++) changed.Add(delta.keys[i]);
                List next;
                if(delta.order==null)
                {
                    // Same keys in the same order: the changed texts replace theirs by position.
                    var ordered=(string[])before.Texts.Clone();
                    for(int i=0;i<count;i++)
                    {
                        if(!before.Index.TryGetValue(delta.keys[i],out int at))
                            throw new InvalidDataException("Изменение списка "+delta.name+": новая часть "+delta.keys[i]+" без нового порядка.");
                        ordered[at]=delta.texts[i];
                    }
                    next=new List {Keys=before.Keys,Texts=ordered,Index=before.Index};
                }
                else
                {
                    var texts=new Dictionary<string,string>(before.Keys.Length+count,StringComparer.Ordinal);
                    for(int i=0;i<before.Keys.Length;i++) texts[before.Keys[i]]=before.Texts[i];
                    for(int i=0;i<count;i++) texts[delta.keys[i]]=delta.texts[i];
                    var ordered=new string[delta.order.Length];
                    for(int i=0;i<ordered.Length;i++)
                        if(!texts.TryGetValue(delta.order[i],out ordered[i])) throw new InvalidDataException("Изменение списка "+delta.name+": нет текста части "+delta.order[i]+".");
                    next=List.Of(delta.order,ordered);
                }
                sets.Add(new SnapshotPartCache.PartSet(delta.name,next.Keys,next.Texts));
                nextLists[delta.name]=next;
            }
            // The live world: strict template, strict changed parts, reused unchanged objects.
            WorldState world;
            using(Remains.Diagnostics.FrameHitchLog.Section("Снимок: скелет (клиент)")) world=StateJson.FromJson<WorldState>(SnapshotPartCache.Skeleton(payload.template));
            bool same=current?.worldId==WorldId;
            using(Remains.Diagnostics.FrameHitchLog.Section("Снимок: части (клиент)"))
            foreach(var set in sets)
                switch(set.Name)
                {
                    case "containers": Fill(world.containers,set,same?current.containers:null,changed,body=>body.id,_=>true);break;
                    case "props": Fill(world.props,set,same?current.props:null,changed,body=>body.id,body=>body.GetType()==typeof(PropState));break;
                    case "lamps": Fill(world.lamps,set,same?current.lamps:null,changed,lamp=>lamp.id,_=>true);break;
                    case "doors": Fill(world.doors,set,same?current.doors:null,changed,door=>door.id,_=>true);break;
                    // Actors and projectiles are always fresh objects: the client changes its own ones locally.
                    case "actors": Fill(world.actors,set,null,changed,actor=>actor.id,_=>false);break;
                    case "factories": Fill(world.factories,set,same?current.factories:null,changed,factory=>factory.level,_=>true);break;
                    case "projectiles": Fill(world.projectiles,set,null,changed,flight=>flight.launch?.id,_=>false);break;
                    default: throw new InvalidDataException("Неизвестный список мира: "+set.Name);
                }
            var snapshot=RecoverySnapshot.Assembled(payload.template,sets,payload.snapshot,world);
            template=payload.template;SnapshotId=payload.snapshot;
            lists.Clear();foreach(var pair in nextLists) lists.Add(pair.Key,pair.Value);
            return snapshot;
        }

        // Unchanged parts reuse the live object of the same key: normally at the same position (checked by ID, no
        // allocation); any other order falls back to an index of the live list built once. Without a live object an
        // unchanged text is read by the trusted reader: it was strictly parsed when it reached this replica.
        private static void Fill<T>(List<T> target,SnapshotPartCache.PartSet set,List<T> live,HashSet<string> changed,Func<T,string> id,Func<T,bool> reusable)
            where T:class
        {
            if(target.Capacity<set.Keys.Length) target.Capacity=set.Keys.Length;
            int prefix=set.Name.Length+1;
            Dictionary<string,T> byKey=null;
            for(int i=0;i<set.Keys.Length;i++)
            {
                string key=set.Keys[i];T item=null;
                if(live!=null && !changed.Contains(key))
                {
                    var candidate=i<live.Count?live[i]:null;
                    string candidateId=candidate==null?null:id(candidate);
                    if(candidateId!=null && key.Length==prefix+candidateId.Length && string.CompareOrdinal(key,prefix,candidateId,0,candidateId.Length)==0) item=candidate;
                    else
                    {
                        if(byKey==null)
                        {
                            byKey=new Dictionary<string,T>(live.Count,StringComparer.Ordinal);
                            foreach(var each in live) if(each!=null && id(each)!=null) byKey[set.Name+":"+id(each)]=each;
                        }
                        byKey.TryGetValue(key,out item);
                    }
                    if(item!=null && !reusable(item)) item=null;
                }
                target.Add(item??(changed.Contains(key)?StateJson.FromJson<T>(set.Texts[i]):StateJson.FromTrustedJson<T>(set.Texts[i])));
            }
        }
    }
}
