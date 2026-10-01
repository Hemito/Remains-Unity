using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace Remains.Content
{
    public sealed class ContentDifference
    {public string id,saved,current;}
    public static class ContentComparison
    {
        public static List<ContentDifference> Compare(ContentManifest saved,ContentManifest current,bool includeUnchanged=false)
        {
            Validate(current,"текущий");
            if(saved!=null) Validate(saved,"сохранённый");
            var rows=new List<ContentDifference>();
            if(saved==null)
                rows.Add(new ContentDifference {id="Манифест",saved="Не записан; состав неизвестен",current=current.scope+" · schema "+current.schema});
            else if(saved.schema!=current.schema || saved.scope!=current.scope)
                rows.Add(new ContentDifference {id="Формат",saved=saved.scope+" · "+saved.schema,current=current.scope+" · "+current.schema});
            var old=saved==null?new List<ContentPackRecord>():saved.packs;
            foreach(string id in old.Select(p=>p.id).Concat(current.packs.Select(p=>p.id)).Distinct())
            {
                int a=old.FindIndex(p=>p.id==id),b=current.packs.FindIndex(p=>p.id==id);
                if(!includeUnchanged && a>=0 && b>=0 && a==b && old[a].version==current.packs[b].version && old[a].fingerprint==current.packs[b].fingerprint) continue;
                rows.Add(new ContentDifference {id=id,saved=Describe(old,a),current=Describe(current.packs,b)});
            }
            if(saved!=null && saved.fingerprint!=current.fingerprint && rows.Count==0)
                rows.Add(new ContentDifference {id="Общий отпечаток",saved=saved.fingerprint,current=current.fingerprint});
            return rows;
        }
        private static void Validate(ContentManifest manifest,string source)
        {
            if(manifest?.packs==null)
                throw new InvalidDataException($"Некорректный {source} манифест: отсутствует список пакетов.");
            var ids=new HashSet<string>(System.StringComparer.Ordinal);
            for(int i=0;i<manifest.packs.Count;i++)
            {
                var pack=manifest.packs[i];
                if(pack==null || string.IsNullOrWhiteSpace(pack.id))
                    throw new InvalidDataException($"Некорректный {source} манифест: позиция={i+1}, отсутствует пакет или его ID.");
                if(!ids.Add(pack.id))
                    throw new InvalidDataException($"Некорректный {source} манифест: позиция={i+1}, повторный ID={pack.id}.");
            }
        }
        private static string Describe(List<ContentPackRecord> packs,int index)
        {
            if(index<0) return "Отсутствует";
            var pack=packs[index];string hash=pack.fingerprint??"не указан";
            return $"#{index+1} · версия {pack.version}\n{hash}";
        }
    }
}
