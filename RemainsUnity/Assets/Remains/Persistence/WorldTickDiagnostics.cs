using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Remains.Diagnostics;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Persistence
{
    // Explains a world tick that took long to apply: what it carried and which fields changed.
    // Runs only after a slow tick, and the field comparison at most once per DetailSeconds.
    public static class WorldTickDiagnostics
    {
        public const double SlowMilliseconds=25;
        private const double DetailSeconds=2;
        private const int DetailParts=24,DetailFields=12;
        private static double nextDetail;
        private static bool failed;

        public static void Report(WorldTick tick,WorldState before,double milliseconds,double now)
        {
            if(tick==null || milliseconds<SlowMilliseconds || failed) return;
            try
            {
                var text=new StringBuilder();
                text.Append("Тик ").Append(tick.seq).Append(": ").Append(milliseconds.ToString("F0")).Append(" мс");
                if(tick.skeleton!=null) text.Append(", поля мира ").Append(tick.skeleton.Length);
                foreach(var list in tick.lists) AppendList(text,list);
                if(now>=nextDetail)
                {
                    nextDetail=now+DetailSeconds;
                    AppendChangedFields(text,tick,before);
                }
                FrameHitchLog.Note(text.ToString());
            }
            catch(Exception error) when(error is InvalidOperationException || error is ArgumentException || error is Newtonsoft.Json.JsonException)
            {
                failed=true;
                Debug.LogWarning($"Описание тяжёлого тика мира отключено до конца запуска: seq={tick.seq}. {error.Message}");
            }
        }
        private static void AppendList(StringBuilder text,WorldTickList list)
        {
            int parts=list.keys?.Length??0,total=0,largest=0;string largestKey=null;
            for(int i=0;i<(list.texts?.Length??0);i++)
            {
                int length=list.texts[i]?.Length??0;total+=length;
                if(length>largest) {largest=length;largestKey=list.keys[i];}
            }
            text.Append(", ").Append(list.name).Append(' ').Append(parts).Append(" частей ").Append(total/1024).Append(" КБ");
            if(largestKey!=null) text.Append(" (больше всех ").Append(largestKey).Append(' ').Append(largest/1024).Append(" КБ)");
            if(list.removed?.Length>0) text.Append(" удалено ").Append(list.removed.Length);
            if(list.order!=null) text.Append(" порядок ").Append(list.order.Length);
        }
        private static void AppendChangedFields(StringBuilder text,WorldTick tick,WorldState before)
        {
            var fields=new Dictionary<string,int>(StringComparer.Ordinal);
            int compared=0,added=0;
            foreach(var list in tick.lists)
            {
                int index=Array.IndexOf(WorldTickLists.Names,list.name);
                if(index<0 || list.keys==null) continue;
                var source=WorldTickLists.List(before,index);
                var items=new Dictionary<string,object>(source.Count,StringComparer.Ordinal);
                for(int i=0;i<source.Count;i++) items[WorldTickLists.Key(list.name,source[i],i)]=source[i];
                for(int i=0;i<list.keys.Length && compared<DetailParts;i++)
                {
                    if(!items.TryGetValue(list.keys[i],out var previous) || previous==null) {added++;continue;}
                    compared++;
                    var old=JObject.Parse(StateJson.ToJson(previous));var next=JObject.Parse(list.texts[i]);
                    foreach(var name in old.Properties().Select(p=>p.Name).Union(next.Properties().Select(p=>p.Name)))
                        if(!JToken.DeepEquals(old[name],next[name]))
                        {
                            string field=list.name+"."+name;
                            fields[field]=fields.TryGetValue(field,out int count)?count+1:1;
                        }
                }
            }
            text.Append("; изменились у ").Append(compared).Append(" частей");
            if(added>0) text.Append(", новых ").Append(added);
            text.Append(": ").Append(string.Join(", ",fields.OrderByDescending(pair=>pair.Value).Take(DetailFields).Select(pair=>pair.Key+"×"+pair.Value)));
        }
    }
}
