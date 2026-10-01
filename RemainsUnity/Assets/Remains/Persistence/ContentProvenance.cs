using System.Collections.Generic;
using System.IO;
using Remains.Content;

namespace Remains.Persistence
{
    public static class ContentProvenance
    {
        public static void Record(ref ContentManifest saved,ref List<ContentManifest> history)
        {
            var current=WeaponCatalog.ManifestCopy();
            if(saved!=null && (saved.schema!=1 || saved.scope!=current.scope))
                throw new InvalidDataException($"Неподдерживаемый манифест контента: schema={saved.schema}, scope={saved.scope}.");
            if(saved?.fingerprint==current.fingerprint) return;
            history??=new();
            if(saved!=null) history.Add(saved.Copy());
            saved=current;
        }
    }
}
