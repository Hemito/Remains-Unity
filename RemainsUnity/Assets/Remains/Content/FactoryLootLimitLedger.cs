using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Remains.Domain;

namespace Remains.Content
{
    public static class FactoryLootLimitLedger
    {
        public static Dictionary<string,int> WithUsage(IDictionary<string,int> baseline,IEnumerable<ContainerState> additions)
        {
            if(baseline==null)throw new InvalidOperationException("Нет мирового журнала лимитов лута.");
            var result=new Dictionary<string,int>(baseline,StringComparer.Ordinal);
            foreach(var container in additions)
            {
                if(container.extensions==null||!container.extensions.TryGetValue("factoryLootLimitUsage",out var token))continue;
                if(token is not JObject usage)throw new InvalidOperationException("Некорректный журнал лимитов контейнера: "+container.id);
                foreach(var entry in usage.Properties())
                {
                    int count=(int)entry.Value;
                    if(count<0)throw new InvalidOperationException("Отрицательное использование лимита: "+container.id+"/"+entry.Name);
                    result[entry.Name]=checked((result.TryGetValue(entry.Name,out int value)?value:0)+count);
                }
            }
            return result;
        }
        public static bool Equal(IDictionary<string,int> first,IDictionary<string,int> second)
        {
            if(first==null||second==null||first.Count!=second.Count)return false;
            foreach(var entry in first)if(!second.TryGetValue(entry.Key,out int value)||value!=entry.Value)return false;
            return true;
        }
    }
}
