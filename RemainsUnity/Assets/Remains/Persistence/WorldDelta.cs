using System;
using System.Collections.Generic;
using System.IO;
using Remains.Domain;
using Remains.Serialization;

namespace Remains.Persistence
{
    // Delta replication (host-migration.md, stage 2): a world revision as "base snapshot → snapshot": the new
    // template (world JSON with part-list markers), the changed or new parts, and a list's order only when it
    // changed. The receiver rebuilds exactly the host's JSON (SnapshotPartCache.Splice), checks it against the
    // snapshot ID (SHA-256 of that JSON) and strictly parses only the template and the changed parts.
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
        private sealed class List
        {
            public string[] Keys;public Dictionary<string,string> Texts;
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
            foreach(var set in split.Lists)
            {
                var texts=new Dictionary<string,string>(StringComparer.Ordinal);
                for(int i=0;i<set.Keys.Length;i++) texts[set.Keys[i]]=set.Texts[i];
                replica.lists[set.Name]=new List {Keys=set.Keys,Texts=texts};
            }
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
                var texts=new Dictionary<string,string>(before.Texts,StringComparer.Ordinal);
                for(int i=0;i<(delta.keys?.Length??0);i++) {texts[delta.keys[i]]=delta.texts[i];changed.Add(delta.keys[i]);}
                var keys=delta.order??before.Keys;
                var ordered=new string[keys.Length];
                for(int i=0;i<keys.Length;i++)
                    if(!texts.TryGetValue(keys[i],out ordered[i])) throw new InvalidDataException("Изменение списка "+delta.name+": нет текста части "+keys[i]+".");
                if(delta.order!=null) {var kept=new Dictionary<string,string>(StringComparer.Ordinal);for(int i=0;i<keys.Length;i++) kept[keys[i]]=ordered[i];texts=kept;}
                sets.Add(new SnapshotPartCache.PartSet(delta.name,keys,ordered));
                nextLists[delta.name]=new List {Keys=keys,Texts=texts};
            }
            string json=SnapshotPartCache.Splice(payload.template,sets);
            // The live world: strict template, strict changed parts, reused unchanged objects.
            var world=StateJson.FromJson<WorldState>(SnapshotPartCache.Skeleton(payload.template));
            var reuse=new Dictionary<string,PropState>(StringComparer.Ordinal);var lamps=new Dictionary<string,LampState>(StringComparer.Ordinal);
            if(current?.worldId==WorldId)
            {
                foreach(var body in current.containers) reuse["containers:"+body.id]=body;
                foreach(var body in current.props) reuse["props:"+body.id]=body;
                foreach(var lamp in current.lamps) lamps["lamps:"+lamp.id]=lamp;
            }
            foreach(var set in sets)
                for(int i=0;i<set.Keys.Length;i++)
                {
                    string key=set.Keys[i];bool fresh=changed.Contains(key);
                    switch(set.Name)
                    {
                        case "containers":
                            world.containers.Add(!fresh && reuse.TryGetValue(key,out var c) && c is ContainerState container?container:StateJson.FromJson<ContainerState>(set.Texts[i]));break;
                        case "props":
                            world.props.Add(!fresh && reuse.TryGetValue(key,out var p) && p.GetType()==typeof(PropState)?p:StateJson.FromJson<PropState>(set.Texts[i]));break;
                        case "lamps":
                            world.lamps.Add(!fresh && lamps.TryGetValue(key,out var l)?l:StateJson.FromJson<LampState>(set.Texts[i]));break;
                        default: throw new InvalidDataException("Неизвестный список мира: "+set.Name);
                    }
                }
            var snapshot=RecoverySnapshot.Assembled(json,payload.snapshot,world);
            template=payload.template;SnapshotId=payload.snapshot;
            lists.Clear();foreach(var pair in nextLists) lists.Add(pair.Key,pair.Value);
            return snapshot;
        }
    }
}
