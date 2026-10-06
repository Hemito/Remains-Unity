using System;
using System.Linq;
using Remains.Content;

namespace Remains.Editor
{
    // Extend our cumulative manifest once; installers remain the only runtime update rule.
    internal static class FactoryExpansionAuthoring
    {
        public static ContentExpansion Extend(string previous,ContentExpansion prior,string[] additions,string level="factory:0")
        {
            string prefix=level+":";
            string Local(string id)
            {
                if(id==null || !id.StartsWith(prefix,StringComparison.Ordinal))
                    throw new InvalidOperationException("Дополнение имеет чужой уровень: "+id+", ожидается "+level);
                return id.Substring(prefix.Length);
            }
            if(additions==null || additions.Length==0 || additions.Distinct(StringComparer.Ordinal).Count()!=additions.Length)
                throw new InvalidOperationException("Дополнение не содержит уникального списка новых объектов: "+previous);
            var current=additions.Select(Local).ToArray();
            var earlier=prior==null?Array.Empty<ContentExpansionStep>():
                new[]{(ContentExpansionStep)prior}.Concat(prior.earlierVersions??Array.Empty<ContentExpansionStep>())
                .Select(step=>new ContentExpansionStep {previousPatch=Local(step.previousPatch),
                    additions=current.Concat(step.additions.Select(Local)).Distinct(StringComparer.Ordinal).ToArray()}).ToArray();
            return new ContentExpansion {previousPatch=Local(previous),additions=current,earlierVersions=earlier};
        }
    }
}
