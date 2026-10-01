using System.Collections.Generic;
using System.Linq;

namespace Remains.Content
{
    public sealed class ContentDifference
    {public string id,saved,current;}
    public static class ContentComparison
    {
        public static List<ContentDifference> Compare(ContentManifest saved,ContentManifest current,bool includeUnchanged=false)
        {
            var rows=new List<ContentDifference>();
            if(saved==null)
                rows.Add(new ContentDifference {id="Манифест",saved="Не записан; состав неизвестен",current=current.scope+" · schema "+current.schema});
            else if(saved.schema!=current.schema || saved.scope!=current.scope)
                rows.Add(new ContentDifference {id="Формат",saved=saved.scope+" · "+saved.schema,current=current.scope+" · "+current.schema});
            var old=saved?.packs??new List<ContentPackRecord>();
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
        private static string Describe(List<ContentPackRecord> packs,int index)
        {
            if(index<0) return "Отсутствует";
            var pack=packs[index];string hash=pack.fingerprint??"не указан";
            return $"#{index+1} · версия {pack.version}\n{hash}";
        }
    }
}
