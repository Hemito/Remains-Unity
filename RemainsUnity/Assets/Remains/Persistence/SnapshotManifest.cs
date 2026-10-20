using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace Remains.Persistence
{
    // A wire identity, deliberately different from SHA256 of the archive JSON. Previous is only a cache:
    // no cached node is accepted without matching its exact ordered keys and text. All owned arrays are private.
    public sealed class SnapshotManifest
    {
        public const int Format=1;
        public string Id {get;}
        // Per-build work, excluding cache hits. Branches includes leaf/list/root descriptors.
        public int HashedTexts {get;}
        public int HashedBranches {get;}
        private readonly string template,templateHash;
        private readonly ListState[] lists;
        private readonly Node listTree;

        private sealed class Node
        {
            public readonly string Hash;
            public readonly Node Left,Right;
            public Node(string hash,Node left=null,Node right=null) {Hash=hash;Left=left;Right=right;}
        }
        private sealed class ListState
        {
            public readonly string Name;
            public readonly string[] Keys,Texts,TextHashes;
            public readonly Node[] Leaves;
            public readonly Node Tree,Descriptor;
            public ListState(string name,string[] keys,string[] texts,string[] hashes,Node[] leaves,Node tree,Node descriptor)
            {Name=name;Keys=keys;Texts=texts;TextHashes=hashes;Leaves=leaves;Tree=tree;Descriptor=descriptor;}
        }

        private SnapshotManifest(string template,string templateHash,ListState[] lists,Node listTree,string id,int texts,int branches)
        {
            this.template=template;this.templateHash=templateHash;this.lists=lists;this.listTree=listTree;
            Id="m1:"+id;HashedTexts=texts;HashedBranches=branches;
        }

        public static SnapshotManifest Build(string template,IReadOnlyList<SnapshotPartCache.PartSet> lists,SnapshotManifest previous=null)
        {
            if(template==null) throw new ArgumentNullException(nameof(template));
            if(lists==null) throw new ArgumentNullException(nameof(lists));
            return new Builder(previous).Build(template,lists);
        }

        private sealed class ReferenceStrings : IEqualityComparer<string>
        {
            public static readonly ReferenceStrings Instance=new();
            public bool Equals(string a,string b)=>ReferenceEquals(a,b);
            public int GetHashCode(string value)=>RuntimeHelpers.GetHashCode(value);
        }
        private sealed class Builder
        {
            private static readonly Encoding strictUtf8=new UTF8Encoding(false,true);
            private readonly SnapshotManifest previous;
            private readonly Dictionary<string,string> textHashes=new(ReferenceStrings.Instance);
            private int texts,branches;
            private Dictionary<string,string> previousTextHashes;
            public Builder(SnapshotManifest previous) {this.previous=previous;}
            private bool PreviousTextHash(string value,out string hash)
            {
                hash=null;
                if(previous==null) return false;
                if(previousTextHashes==null)
                {
                    previousTextHashes=new Dictionary<string,string>(ReferenceStrings.Instance);
                    previousTextHashes[previous.template]=previous.templateHash;
                    foreach(var list in previous.lists)
                        for(int i=0;i<list.Texts.Length;i++) previousTextHashes[list.Texts[i]]=list.TextHashes[i];
                }
                return previousTextHashes.TryGetValue(value,out hash);
            }

            public SnapshotManifest Build(string template,IReadOnlyList<SnapshotPartCache.PartSet> input)
            {
                string templateHash=previous!=null && string.Equals(template,previous.template,StringComparison.Ordinal)?
                    previous.templateHash:TextHash(template,"шаблон");
                var oldLists=new Dictionary<string,ListState>(StringComparer.Ordinal);
                if(previous!=null) foreach(var list in previous.lists) oldLists.Add(list.Name,list);
                var names=new HashSet<string>(StringComparer.Ordinal);
                var lists=new ListState[input.Count];var descriptors=new Node[input.Count];
                bool aligned=previous!=null && previous.lists.Length==input.Count;
                for(int i=0;i<input.Count;i++)
                {
                    var set=input[i];
                    if(set==null) throw Invalid("список с индексом "+i+" отсутствует");
                    CheckId(set.Name,"имя списка, индекс="+i);
                    if(!names.Add(set.Name)) throw Invalid("повтор имени списка "+set.Name);
                    oldLists.TryGetValue(set.Name,out var old);
                    lists[i]=BuildList(set,old);descriptors[i]=lists[i].Descriptor;
                    aligned&=previous!=null && i<previous.lists.Length && previous.lists[i].Name==set.Name;
                }
                Node tree;
                if(aligned)
                {
                    var changed=new List<int>();
                    for(int i=0;i<lists.Length;i++)
                        if(!ReferenceEquals(descriptors[i],previous.lists[i].Descriptor)) changed.Add(i);
                    tree=ReplaceMany(previous.listTree,0,lists.Length,descriptors,changed,0,changed.Count);
                }
                else tree=Tree(descriptors,0,descriptors.Length);
                // If absolutely nothing changed, even the final root hash is reused.
                string id=previous!=null && templateHash==previous.templateHash && ReferenceEquals(tree,previous.listTree)?
                    previous.Id.Substring(3):Hash("root",templateHash,Count(lists.Length),tree?.Hash??"");
                return new SnapshotManifest(template,templateHash,lists,tree,id,texts,branches);
            }

            private ListState BuildList(SnapshotPartCache.PartSet set,ListState old)
            {
                if(set.Keys==null || set.Texts==null || set.Keys.Length!=set.Texts.Length)
                    throw Invalid("список "+set.Name+": массивы ключей/текстов отсутствуют или имеют разную длину");
                // Exact equality to private validated keys proves uniqueness and Unicode validity again.
                // Public input arrays are never retained; callers still own and may mutate them between builds.
                bool sameKeys=old!=null && old.Keys.Length==set.Keys.Length;
                for(int i=0;sameKeys && i<set.Keys.Length;i++) sameKeys=string.Equals(set.Keys[i],old.Keys[i],StringComparison.Ordinal);
                if(sameKeys)
                {
                    List<int> changedPositions=null;
                    for(int i=0;i<set.Texts.Length;i++)
                    {
                        if(set.Texts[i]==null) throw Invalid("список "+set.Name+", ключ "+old.Keys[i]+": отсутствует текст (JSON null передаётся строкой)");
                        if(!string.Equals(set.Texts[i],old.Texts[i],StringComparison.Ordinal))
                            (changedPositions??=new List<int>()).Add(i);
                    }
                    if(changedPositions==null) return old;
                    var nextValues=(string[])old.Texts.Clone();
                    var nextHashes=(string[])old.TextHashes.Clone();
                    var nextLeaves=(Node[])old.Leaves.Clone();
                    foreach(int i in changedPositions)
                    {
                        nextValues[i]=set.Texts[i];
                        nextHashes[i]=TextHash(nextValues[i],"список "+set.Name+", ключ "+old.Keys[i]);
                        nextLeaves[i]=new Node(Hash("leaf",old.Keys[i],nextHashes[i]));
                    }
                    var nextTree=ReplaceMany(old.Tree,0,old.Keys.Length,nextLeaves,changedPositions,0,changedPositions.Count);
                    return new ListState(set.Name,old.Keys,nextValues,nextHashes,nextLeaves,nextTree,
                        new Node(Hash("list",set.Name,Count(old.Keys.Length),nextTree?.Hash??"")));
                }
                var keys=(string[])set.Keys.Clone();var values=(string[])set.Texts.Clone();
                var unique=new HashSet<string>(StringComparer.Ordinal);
                bool aligned=old!=null && old.Keys.Length==keys.Length;
                for(int i=0;i<keys.Length;i++)
                {
                    CheckId(keys[i],"ключ списка "+set.Name+", индекс="+i);
                    if(!unique.Add(keys[i])) throw Invalid("список "+set.Name+": повтор ключа "+keys[i]);
                    if(values[i]==null) throw Invalid("список "+set.Name+", ключ "+keys[i]+": отсутствует текст (JSON null передаётся строкой)");
                    aligned&=old!=null && i<old.Keys.Length && old.Keys[i]==keys[i];
                }
                Dictionary<string,int> positions=null;
                if(old!=null && !aligned)
                {
                    positions=new Dictionary<string,int>(old.Keys.Length,StringComparer.Ordinal);
                    for(int i=0;i<old.Keys.Length;i++) positions.Add(old.Keys[i],i);
                }
                var hashes=new string[keys.Length];var leaves=new Node[keys.Length];var changed=new List<int>();
                for(int i=0;i<keys.Length;i++)
                {
                    int at=aligned?i:-1;
                    if(!aligned && positions!=null && positions.TryGetValue(keys[i],out int found)) at=found;
                    if(at>=0 && string.Equals(values[i],old.Texts[at],StringComparison.Ordinal))
                    {hashes[i]=old.TextHashes[at];leaves[i]=old.Leaves[at];}
                    else
                    {
                        hashes[i]=TextHash(values[i],"список "+set.Name+", ключ "+keys[i],true);
                        leaves[i]=new Node(Hash("leaf",keys[i],hashes[i]));
                    }
                    if(aligned && !ReferenceEquals(leaves[i],old.Leaves[i])) changed.Add(i);
                }
                if(aligned && changed.Count==0) return old;
                Node tree;
                if(aligned)
                {
                    tree=ReplaceMany(old.Tree,0,keys.Length,leaves,changed,0,changed.Count);
                }
                else tree=Tree(leaves,0,leaves.Length);
                var descriptor=new Node(Hash("list",set.Name,Count(keys.Length),tree?.Hash??""));
                return new ListState(set.Name,keys,values,hashes,leaves,tree,descriptor);
            }

            private Node Tree(Node[] leaves,int start,int count)
            {
                if(count==0) return null;
                if(count==1) return leaves[start];
                int left=count/2;
                return Branch(Tree(leaves,start,left),Tree(leaves,start+left,count-left));
            }
            // Sorted changed positions partition along the existing tree. Shared ancestors are hashed once,
            // after both children are final, rather than once per changed leaf. Untouched subtrees retain identity.
            private Node ReplaceMany(Node node,int start,int count,Node[] replacements,List<int> changed,int from,int to)
            {
                if(from==to) return node;
                if(count==1) return replacements[start];
                int left=count/2;
                int split=from;
                while(split<to && changed[split]<start+left) split++;
                return Branch(ReplaceMany(node.Left,start,left,replacements,changed,from,split),
                    ReplaceMany(node.Right,start+left,count-left,replacements,changed,split,to));
            }
            private Node Branch(Node left,Node right)=>new(Hash("branch",left.Hash,right.Hash),left,right);

            private string TextHash(string value,string context,bool reusePrevious=false)
            {
                if(textHashes.TryGetValue(value,out string cached)) return cached;
                if(reusePrevious && PreviousTextHash(value,out cached)) return cached;
                try {strictUtf8.GetByteCount(value);}
                catch(EncoderFallbackException error) {throw new InvalidDataException("Некорректный UTF-16 в манифесте: "+context+".",error);}
                texts++;
                string hash=HashRaw("text",value);textHashes.Add(value,hash);return hash;
            }
            private string Hash(string domain,params string[] values) {branches++;return HashRaw(domain,values);}
            // Every segment is strict UTF-8, prefixed by its UTF-8 byte length; the domain/version are explicit.
            // Delimiters in names, keys, or JSON cannot change framing. Only immutable digest strings are retained.
            private static string HashRaw(string domain,params string[] values)
            {
                var hash=TextSha256.Begin();hash.Append("Remains.SnapshotManifest/1/");hash.Append(domain);hash.Append(':');
                foreach(string value in values)
                {
                    hash.Append(Count(strictUtf8.GetByteCount(value)));hash.Append(':');hash.Append(value);
                }
                return hash.Finish();
            }
            private static void CheckId(string id,string context)
            {
                if(string.IsNullOrEmpty(id)) throw Invalid("пустой "+context);
                try {strictUtf8.GetByteCount(id);}
                catch(EncoderFallbackException error) {throw new InvalidDataException("Некорректный UTF-16 в манифесте: "+context+".",error);}
            }
            private static string Count(int count)=>count.ToString(CultureInfo.InvariantCulture);
            private static InvalidDataException Invalid(string reason)=>new("Некорректный манифест снимка: "+reason+".");
        }
    }
}
