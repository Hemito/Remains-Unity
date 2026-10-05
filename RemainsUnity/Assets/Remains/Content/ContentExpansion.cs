using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Domain;
using UnityEngine;

namespace Remains.Content
{
    [Serializable] public class ContentExpansionStep
    {
        [InspectorName("Предыдущая версия")] public string previousPatch;
        [InspectorName("ID добавленных объектов")] public string[] additions=Array.Empty<string>();
    }
    [Serializable] public class ContentExpansion : ContentExpansionStep
    {
        [InspectorName("Более ранние версии (от новых к старым)")] public ContentExpansionStep[] earlierVersions=Array.Empty<ContentExpansionStep>();
    }
    // One additive-update rule for physical objects and NPCs. Removed old objects stay removed.
    public static class ContentExpansionRules
    {
        public static T InLevel<T>(T expansion,string level) where T:ContentExpansion,new()
        {
            if(expansion==null || string.IsNullOrWhiteSpace(expansion.previousPatch))
            {
                if(expansion?.additions?.Length>0 || expansion?.earlierVersions?.Length>0) throw new InvalidOperationException("Дополнение не имеет предыдущей версии: "+level);
                return null;
            }
            if(expansion.additions==null) throw new InvalidOperationException("Нет ID дополнения: "+level);
            ContentExpansionStep Prefix(ContentExpansionStep step)
            {
                if(step==null || string.IsNullOrWhiteSpace(step.previousPatch) || step.additions==null)
                    throw new InvalidOperationException("Неполная более ранняя версия дополнения: "+level);
                return new ContentExpansionStep {previousPatch=level+":"+step.previousPatch,additions=step.additions.Select(id=>level+":"+id).ToArray()};
            }
            return new T {previousPatch=level+":"+expansion.previousPatch,additions=expansion.additions.Select(id=>level+":"+id).ToArray(),
                earlierVersions=(expansion.earlierVersions??Array.Empty<ContentExpansionStep>()).Select(Prefix).ToArray()};
        }
        public static HashSet<string> SelectedIds(WorldState world,string patch,string level,ContentExpansion expansion,IEnumerable<string> ids)
        {
            if(expansion==null) return null;
            var known=ids.ToArray();
            if(known.Any(string.IsNullOrWhiteSpace) || known.Distinct(StringComparer.Ordinal).Count()!=known.Length)
                throw new InvalidOperationException("Некорректное добавочное обновление расстановки: "+patch);
            var steps=new[]{(ContentExpansionStep)expansion}.Concat(expansion.earlierVersions??Array.Empty<ContentExpansionStep>()).ToArray();
            var predecessors=new HashSet<string>(StringComparer.Ordinal);HashSet<string> selected=null,previous=null;
            foreach(var step in steps)
            {
                if(step==null || string.IsNullOrWhiteSpace(step.previousPatch) || !step.previousPatch.StartsWith(level+":",StringComparison.Ordinal) ||
                    step.previousPatch==patch || !predecessors.Add(step.previousPatch) || step.additions==null || step.additions.Length==0 ||
                    step.additions.Any(id=>string.IsNullOrWhiteSpace(id) || !known.Contains(id,StringComparer.Ordinal)) ||
                    step.additions.Distinct(StringComparer.Ordinal).Count()!=step.additions.Length)
                    throw new InvalidOperationException("Некорректная предыдущая версия дополнения: "+patch);
                var additions=step.additions.ToHashSet(StringComparer.Ordinal);
                if(previous!=null && !previous.IsSubsetOf(additions))
                    throw new InvalidOperationException("Более ранняя версия теряет добавление новой версии: "+patch);
                previous=additions;
                if(selected==null && world.contentPatches.Contains(step.previousPatch)) selected=additions;
            }
            return selected;
        }
    }
}
