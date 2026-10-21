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
        public const int Format=3;   // 3: versioned manifest IDs instead of full-JSON SHA for delta bases/results.
        public int format=Format;
        public string world,baseSnapshot,snapshot;
        public string template;
        public List<WorldDeltaList> lists=new();
    }
    [Serializable] public sealed class WorldDeltaList
    {
        public string name;
        public string[] order;            // null: the base order
        public string[] keys,texts;       // changed or new parts
        // Actors: a changed actor known to the base as its template and only the changed cold texts (null: as in the
        // base); its texts[i] is then null. A ~50 KB player no longer travels whole on every move.
        public string[] templates;
        public string[][] cold;
    }

    public static class WorldDelta
    {
        public static WorldDeltaPayload Build(string worldId,string baseId,SnapshotPartCache.Composition from,string snapshotId,SnapshotPartCache.Composition to)
        {
            using var measure=Remains.Diagnostics.FrameHitchLog.Section("Снимок: построение изменения (хост)");
            if(from==null || to==null) throw new ArgumentNullException(from==null?nameof(from):nameof(to));
            var payload=new WorldDeltaPayload {world=worldId,baseSnapshot=baseId,snapshot=snapshotId,template=to.Template};
            foreach(var next in to.Lists)
            {
                var previous=Array.Find(from.Lists,list=>list.Name==next.Name);
                bool sameOrder=previous!=null && previous.Keys.Length==next.Keys.Length;
                if(sameOrder) for(int i=0;i<next.Keys.Length && sameOrder;i++) sameOrder=previous.Keys[i]==next.Keys[i];
                Dictionary<string,int> before=null;
                if(previous!=null && !sameOrder)
                {
                    before=new Dictionary<string,int>(previous.Keys.Length,StringComparer.Ordinal);
                    for(int i=0;i<previous.Keys.Length;i++) before.Add(previous.Keys[i],i);
                }
                var keys=new List<string>();var texts=new List<string>();
                bool actorPieces=next.Pieces!=null && previous?.Pieces!=null;
                List<string> templates=actorPieces?new List<string>():null;
                List<string[]> cold=actorPieces?new List<string[]>():null;
                for(int i=0;i<next.Keys.Length;i++)
                {
                    int at=sameOrder?i:-1;
                    if(!sameOrder && before!=null && before.TryGetValue(next.Keys[i],out int found)) at=found;
                    if(at>=0 && previous.Texts[at]==next.Texts[i]) continue;
                    keys.Add(next.Keys[i]);
                    var prior=actorPieces && at>=0?previous.Pieces[at]:null;
                    if(prior!=null && next.Pieces[i]!=null)
                    {
                        var changed=new string[next.Pieces[i].Cold.Length];
                        for(int c=0;c<changed.Length;c++) changed[c]=next.Pieces[i].Cold[c]==prior.Cold[c]?null:next.Pieces[i].Cold[c];
                        texts.Add(null);templates.Add(next.Pieces[i].Template);cold.Add(changed);
                    }
                    else {texts.Add(next.Texts[i]);templates?.Add(null);cold?.Add(null);}
                }
                payload.lists.Add(new WorldDeltaList {name=next.Name,order=sameOrder?null:next.Keys,keys=keys.ToArray(),texts=texts.ToArray(),
                    templates=templates?.ToArray(),cold=cold?.ToArray()});
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
        private SnapshotManifest manifest;
        private readonly Dictionary<string,List> lists=new(StringComparer.Ordinal);
        // The objects the last apply put into each reusable list, by position: the live list still holding them at the
        // same positions takes unchanged parts by reference, without key comparisons (~2200 parts per snapshot).
        private readonly Dictionary<string,object[]> produced=new(StringComparer.Ordinal);
        // Private, validated canonical actors. Live motion/inventory reconciliation must never mutate this graph.
        private Dictionary<string,(string text,ActorState actor)> canonicalActors=new(StringComparer.Ordinal);
        private sealed class Positions
        {
            public readonly object[] Last;public readonly HashSet<int> Changed;
            public Positions(object[] last,HashSet<int> changed) {Last=last;Changed=changed;}
        }

        public static WorldReplica FromSnapshot(RecoverySnapshot snapshot)
        {
            if(snapshot==null) throw new ArgumentNullException(nameof(snapshot));
            var copy=StateJson.FromTrustedJson<WorldState>(snapshot.Json);
            var split=SnapshotPartCache.Split(copy);
            if(SnapshotPartCache.Splice(split.Template,split.Lists)!=snapshot.Json)
                throw new InvalidDataException($"Снимок мира не раскладывается на части без потерь: world={snapshot.WorldId}, snapshot={snapshot.Id}.");
            var replica=new WorldReplica {WorldId=snapshot.WorldId,SnapshotId=snapshot.ReplicationId,template=split.Template,manifest=snapshot.Manifest};
            foreach(var set in split.Lists) replica.lists[set.Name]=List.Of(set.Keys,set.Texts);
            var actorSet=Array.Find(split.Lists,set=>set.Name=="actors");
            for(int i=0;i<actorSet.Keys.Length;i++) replica.canonicalActors.Add(actorSet.Keys[i],(actorSet.Texts[i],copy.actors[i]));
            return replica;
        }

        // Applies a delta to this replica and returns the verified snapshot with its live world: the template and the
        // changed parts are strictly parsed; unchanged parts reuse the current live objects of the same keys.
        public RecoverySnapshot Apply(WorldDeltaPayload payload,WorldState current)
            =>ApplyCore(payload,current,false,out _,out _);

        // Runtime opt-in: unchanged canonical actors keep their existing live projections. Detached recovery
        // consumers still read the immutable snapshot, never this graph or the replica's private actor cache.
        public RecoverySnapshot ApplyLive(WorldDeltaPayload payload,WorldState current,out WorldState live,out HashSet<string> reusedActors)
            =>ApplyCore(payload,current,true,out live,out reusedActors);

        public ActorState CopyCanonicalActor(string id)=>id!=null && canonicalActors.TryGetValue("actors:"+id,out var entry)?ValidatedStateCopy.Copy(entry.actor):null;

        // The verified canonical order supplies positions without rebuilding an index for every live root.
        // A locally reordered list is still supported by the checked live-world index.
        public PropState FindLiveBody(WorldState world,string key)
        {
            if(world==null || world.worldId!=WorldId || key==null) return null;
            bool container=key.StartsWith("containers:",StringComparison.Ordinal);
            string name=container?"containers":key.StartsWith("props:",StringComparison.Ordinal)?"props":null;
            if(name==null || !lists.TryGetValue(name,out var list) || !list.Index.TryGetValue(key,out int at)) return null;
            PropState body=container?(at<world.containers.Count?world.containers[at]:null):(at<world.props.Count?world.props[at]:null);
            string id=key.Substring(name.Length+1);
            if(body?.id==id) return body;
            body=WorldProps.Find(world,id);
            return body!=null && (body is ContainerState)==container?body:null;
        }

        private RecoverySnapshot ApplyCore(WorldDeltaPayload payload,WorldState current,bool reuseActors,out WorldState live,out HashSet<string> reusedActors)
        {
            live=null;reusedActors=null;
            if(payload==null || payload.format!=WorldDeltaPayload.Format) throw new InvalidDataException("Неизвестный формат изменения мира.");
            if(payload.world!=WorldId || payload.baseSnapshot!=SnapshotId)
                throw new InvalidDataException($"Изменение мира от другой базы: ожидалась {SnapshotId}, получена {payload.baseSnapshot}, world={payload.world}.");
            var sets=new List<SnapshotPartCache.PartSet>();var nextLists=new Dictionary<string,List>(StringComparer.Ordinal);
            var changed=new HashSet<string>(StringComparer.Ordinal);
            var changedAt=new Dictionary<string,HashSet<int>>(StringComparer.Ordinal);   // same-order lists: changed positions
            var verified=new HashSet<string>(StringComparer.Ordinal);   // changed actors already checked by pieces
            var parsedActors=new Dictionary<string,ActorState>(StringComparer.Ordinal);
            var nextPieces=new Dictionary<string,(ActorText.Pieces pieces,string text)>(actorPieces,StringComparer.Ordinal);
            foreach(var delta in payload.lists)
            {
                if(!lists.TryGetValue(delta.name,out var before)) throw new InvalidDataException("Изменение неизвестного списка мира: "+delta.name);
                if((delta.keys?.Length??0)!=(delta.texts?.Length??0)) throw new InvalidDataException("Изменение списка "+delta.name+": ключи и тексты не совпадают.");
                int count=delta.keys?.Length??0;
                if(delta.templates!=null || delta.cold!=null) ExpandActors(delta,before,verified,parsedActors,nextPieces);
                for(int i=0;i<count;i++) changed.Add(delta.keys[i]);
                List next;
                if(delta.order==null)
                {
                    // Same keys in the same order: the changed texts replace theirs by position.
                    var ordered=count==0?before.Texts:(string[])before.Texts.Clone();
                    var at=new HashSet<int>();
                    for(int i=0;i<count;i++)
                    {
                        if(!before.Index.TryGetValue(delta.keys[i],out int position))
                            throw new InvalidDataException("Изменение списка "+delta.name+": новая часть "+delta.keys[i]+" без нового порядка.");
                        ordered[position]=delta.texts[i];at.Add(position);
                    }
                    changedAt[delta.name]=at;
                    next=count==0?before:new List {Keys=before.Keys,Texts=ordered,Index=before.Index};
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
            var outputs=new Dictionary<string,object[]>(StringComparer.Ordinal);
            var nextActors=new Dictionary<string,(string text,ActorState actor)>(StringComparer.Ordinal);
            using(Remains.Diagnostics.FrameHitchLog.Section("Снимок: части (клиент)"))
            foreach(var set in sets)
            {
                Positions positions=same && changedAt.TryGetValue(set.Name,out var at) && produced.TryGetValue(set.Name,out var last)?new Positions(last,at):null;
                switch(set.Name)
                {
                    case "containers": outputs[set.Name]=Fill(world.containers,set,same?current.containers:null,changed,body=>body.id,_=>true,positions);break;
                    case "props": outputs[set.Name]=Fill(world.props,set,same?current.props:null,changed,body=>body.id,body=>body.GetType()==typeof(PropState),positions);break;
                    case "lamps": outputs[set.Name]=Fill(world.lamps,set,same?current.lamps:null,changed,lamp=>lamp.id,_=>true,positions);break;
                    case "doors": outputs[set.Name]=Fill(world.doors,set,same?current.doors:null,changed,door=>door.id,_=>true,positions);break;
                    // Fresh live actors from private canonical graphs; unchanged texts need no repeated JSON parse.
                    case "actors":
                        for(int i=0;i<set.Keys.Length;i++)
                        {
                            string key=set.Keys[i],text=set.Texts[i];
                            var canonical=parsedActors.TryGetValue(key,out var parsed)?parsed:canonicalActors.TryGetValue(key,out var prior) && prior.text==text?prior.actor:
                                changed.Contains(key) && !verified.Contains(key)?StateJson.FromJson<ActorState>(text):StateJson.FromTrustedJson<ActorState>(text);
                            nextActors.Add(key,(text,canonical));
                            world.actors.Add(reuseActors?canonical:ValidatedStateCopy.Copy(canonical));
                        }
                        break;
                    // Projectiles still change locally.
                    case "factories": outputs[set.Name]=Fill(world.factories,set,same?current.factories:null,changed,factory=>factory.level,_=>true,positions);break;
                    case "projectiles": Fill(world.projectiles,set,null,changed,flight=>flight.launch?.id,_=>false,null);break;
                    // Mines change only on the host (trigger, removal); unchanged ones keep their live objects.
                    case "mines": outputs[set.Name]=Fill(world.mines,set,same?current.mines:null,changed,mine=>mine.id,_=>true,positions);break;
                    // Traps and charges change only on the host, as mines.
                    case "hazards": outputs[set.Name]=Fill(world.hazards,set,same?current.hazards:null,changed,hazard=>hazard.id,_=>true,positions);break;
                    default: throw new InvalidDataException("Неизвестный список мира: "+set.Name);
                }
            }
            var snapshot=RecoverySnapshot.Assembled(payload.template,sets,payload.snapshot,world,retainPreparedLive:!reuseActors,previousManifest:manifest);
            if(reuseActors)
            {
                // The snapshot has validated private canonical actors. Only after all checks succeed replace
                // the temporary list with live references/copies; no failed delta can mutate the current graph.
                var actors=new List<ActorState>(world.actors.Count);
                var reused=new HashSet<string>(StringComparer.Ordinal);
                Dictionary<string,ActorState> byId=null;
                if(same)
                {
                    byId=new Dictionary<string,ActorState>(StringComparer.Ordinal);
                    foreach(var actor in current.actors) if(actor!=null) byId.Add(actor.id,actor);
                }
                foreach(var canonical in world.actors)
                {
                    string key="actors:"+canonical.id;
                    if(!changed.Contains(key) && byId!=null && byId.TryGetValue(canonical.id,out var candidate) &&
                        ActorMotionReplication.SameLifetime(candidate,canonical))
                    {actors.Add(candidate);reused.Add(canonical.id);}
                    else actors.Add(ValidatedStateCopy.Copy(canonical));
                }
                world.actors=actors;live=world;reusedActors=reused;
            }
            template=payload.template;SnapshotId=payload.snapshot;manifest=snapshot.Manifest;
            lists.Clear();foreach(var pair in nextLists) lists.Add(pair.Key,pair.Value);
            produced.Clear();foreach(var pair in outputs) produced.Add(pair.Key,pair.Value);
            canonicalActors=nextActors;
            actorPieces.Clear();foreach(var pair in nextPieces) if(nextActors.ContainsKey(pair.Key)) actorPieces.Add(pair.Key,pair.Value);
            return snapshot;
        }

        // Unchanged parts reuse the live object of the same key: normally at the same position (checked by ID, no
        // allocation); any other order falls back to an index of the live list built once. Without a live object an
        // unchanged text is read by the trusted reader: it was strictly parsed when it reached this replica.
        // An actor sent as template plus changed cold texts: its full text from the base pieces, strictly checked by
        // pieces (the template and each changed cold text); the delta then carries the full text as any part.
        private void ExpandActors(WorldDeltaList delta,List before,HashSet<string> verified,Dictionary<string,ActorState> parsed,
            Dictionary<string,(ActorText.Pieces pieces,string text)> nextPieces)
        {
            int count=delta.keys?.Length??0;
            if(delta.name!="actors" || delta.templates==null || delta.cold==null || delta.templates.Length!=count || delta.cold.Length!=count)
                throw new InvalidDataException("Изменение списка "+delta.name+": неверные части персонажей.");
            for(int i=0;i<count;i++)
            {
                if(delta.templates[i]==null) {if(delta.texts[i]==null) throw new InvalidDataException("Изменение персонажа "+delta.keys[i]+" без текста.");continue;}
                if(delta.texts[i]!=null || delta.cold[i]==null || delta.cold[i].Length!=ActorText.Cold.Count)
                    throw new InvalidDataException("Изменение персонажа "+delta.keys[i]+": неверные части.");
                if(!before.Index.TryGetValue(delta.keys[i],out int position))
                    throw new InvalidDataException("Изменение персонажа "+delta.keys[i]+" по частям без прежнего текста.");
                var prior=actorPieces.TryGetValue(delta.keys[i],out var cached) && cached.text==before.Texts[position]?cached.pieces:
                    ActorText.Pieces.OfText(before.Texts[position]);
                var merged=new string[ActorText.Cold.Count];
                for(int c=0;c<merged.Length;c++) merged[c]=delta.cold[i][c]??prior.Cold[c];
                string text=ActorText.Join(delta.templates[i],merged);
                var sent=delta.cold[i];
                canonicalActors.TryGetValue(delta.keys[i],out var previousActor);
                parsed.Add(delta.keys[i],ActorText.Read(delta.templates[i],merged,c=>sent[c]!=null,previousActor.text==before.Texts[position]?previousActor.actor:null));
                delta.texts[i]=text;verified.Add(delta.keys[i]);
                nextPieces[delta.keys[i]]=(new ActorText.Pieces(delta.templates[i],merged),text);
            }
        }
        // Pieces of the actor texts last expanded, by key (valid for that exact text).
        private readonly Dictionary<string,(ActorText.Pieces pieces,string text)> actorPieces=new(StringComparer.Ordinal);

        private static object[] Fill<T>(List<T> target,SnapshotPartCache.PartSet set,List<T> live,HashSet<string> changed,Func<T,string> id,Func<T,bool> reusable,
            Positions positions,HashSet<string> verified=null)
            where T:class
        {
            if(target.Capacity<set.Keys.Length) target.Capacity=set.Keys.Length;
            int prefix=set.Name.Length+1;
            Dictionary<string,T> byKey=null;
            bool aligned=positions!=null && live!=null && positions.Last.Length==set.Keys.Length;
            for(int i=0;i<set.Keys.Length;i++)
            {
                // The object this replica put here last time, still in the live list at this position and unchanged.
                if(aligned && !positions.Changed.Contains(i) && i<live.Count && live[i]!=null && ReferenceEquals(live[i],positions.Last[i]))
                {target.Add(live[i]);continue;}
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
                // A changed part is strictly parsed unless its pieces were already checked (actors by template).
                target.Add(item??(changed.Contains(key) && !(verified?.Contains(key)??false)?StateJson.FromJson<T>(set.Texts[i]):StateJson.FromTrustedJson<T>(set.Texts[i])));
            }
            // Preserve the positional cache when every output reference is unchanged.
            bool identical=positions!=null && positions.Last.Length==target.Count;
            for(int i=0;identical && i<target.Count;i++) identical=ReferenceEquals(positions.Last[i],target[i]);
            if(identical) return positions.Last;
            var result=new object[target.Count];for(int i=0;i<result.Length;i++) result[i]=target[i];
            return result;
        }
    }
}
