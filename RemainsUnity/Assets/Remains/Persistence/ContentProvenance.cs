using System.Collections.Generic;
using System.IO;
using Remains.Content;

namespace Remains.Persistence
{
    public static class ContentProvenance
    {
        public static void Record(ref ContentManifest saved,ref List<ContentManifest> history)
            =>Record(ref saved,ref history,WeaponCatalog.ManifestCopy());
        public static void Record(ref ContentManifest saved,ref List<ContentManifest> history,ContentManifest current)
        {
            if(saved!=null && !saved.IsSupported)
                throw new InvalidDataException($"Неподдерживаемый манифест контента: schema={saved.schema}, scope={saved.scope}.");
            history??=new();
            Compact(history);
            if(saved!=null && ContentComparison.Compare(saved,current).Count==0) return;
            if(saved!=null) Append(history,saved);
            saved=current;
        }
        // History answers "which packs did this save ever run with" (ActorControlMigration.IsUnmodified): it keeps each
        // earlier manifest's packs and fingerprint, once per fingerprint, without the per-asset list (which made a
        // character profile grow by ~2 KB per content change and exceed the network join limit, TD-31).
        private static void Append(List<ContentManifest> history,ContentManifest manifest)
        {
            var entry=Summary(manifest);
            if(!history.Exists(previous=>previous.fingerprint==entry.fingerprint && entry.fingerprint!=null)) history.Add(entry);
        }
        private static void Compact(List<ContentManifest> history)
        {
            var kept=new List<ContentManifest>(history.Count);
            foreach(var manifest in history)
            {
                if(manifest==null) continue;
                var entry=Summary(manifest);
                if(entry.fingerprint==null || !kept.Exists(previous=>previous.fingerprint==entry.fingerprint)) kept.Add(entry);
            }
            history.Clear();history.AddRange(kept);
        }
        private static ContentManifest Summary(ContentManifest manifest)
        {
            var entry=manifest.Copy();
            entry.gameplayAssets=new List<ContentAssetRecord>();
            return entry;
        }
    }
}
