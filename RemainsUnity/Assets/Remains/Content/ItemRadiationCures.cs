using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;

namespace Remains.Content
{
    // The radiation an item takes away when taken: AllData item hrad (Invent: heal(hrad × healMult, 2)); 0 — none.
    // Only cures are offered (antiradin 200, milk 50): an item whose hrad adds radiation (Rad-Cola −33) has other effects
    // (hmana) not carried over yet, so it is not offered for its radiation alone.
    public static class ItemRadiationCures
    {
        private static readonly Dictionary<string,float> cache=new(StringComparer.Ordinal);
        public static float Amount(string definition)
        {
            if(definition==null || !definition.StartsWith("item:",StringComparison.Ordinal)) return 0;
            if(cache.TryGetValue(definition,out float amount)) return amount;
            amount=0;
            if(ExportedContentCatalog.Load().TryItem(definition,out var entry) && XElement.Parse(entry.rawXml).Attribute("hrad") is {} hrad)
            {
                amount=float.Parse(hrad.Value,CultureInfo.InvariantCulture);
                if(!float.IsFinite(amount)) throw new InvalidOperationException($"Некорректное лечение радиации предмета {definition}: hrad={hrad.Value}.");
                if(amount<0) amount=0;
            }
            cache[definition]=amount;return amount;
        }
    }
}
