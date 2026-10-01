using System;
using System.Collections.Generic;
using Remains.Serialization;

namespace Remains.Content
{
    [Serializable] public sealed class ContentPackRecord : ExtensibleState
    {public string id,version,fingerprint;public string[] dependencies;}
    [Serializable] public sealed class ContentAssetRecord : ExtensibleState
    {public string id,fingerprint;}
    [Serializable] public sealed class ContentManifest : ExtensibleState
    {
        public int schema=1;
        public string scope="weapon-catalog",fingerprint;
        // Array order is load order; base is first.
        public List<ContentPackRecord> packs=new();
        public List<ContentAssetRecord> gameplayAssets=new();
        public bool IsSupported=>schema==1 && (scope=="weapon-catalog" || scope=="prototype-gameplay");
        public ContentManifest Copy()=>StateJson.Copy(this);
    }
}
