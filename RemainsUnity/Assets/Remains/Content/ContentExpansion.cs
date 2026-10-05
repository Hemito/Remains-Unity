using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Domain;
using UnityEngine;

namespace Remains.Content
{
    [Serializable] public class ContentExpansion
    {
        [InspectorName("Предыдущая версия")] public string previousPatch;
        [InspectorName("ID добавленных объектов")] public string[] additions=Array.Empty<string>();
    }
    // One additive-update rule for physical objects and NPCs. Removed old objects stay removed.
    public static class ContentExpansionRules
    {
        public static T InLevel<T>(T expansion,string level) where T:ContentExpansion,new()
        {
            if(expansion==null || string.IsNullOrWhiteSpace(expansion.previousPatch))
            {
                if(expansion?.additions?.Length>0) throw new InvalidOperationException("Дополнение не имеет предыдущей версии: "+level);
                return null;
            }
            if(expansion.additions==null) throw new InvalidOperationException("Нет ID дополнения: "+level);
            return new T {previousPatch=level+":"+expansion.previousPatch,additions=expansion.additions.Select(id=>level+":"+id).ToArray()};
        }
        public static HashSet<string> SelectedIds(WorldState world,string patch,string level,ContentExpansion expansion,IEnumerable<string> ids)
        {
            if(expansion==null) return null;
            var known=ids.ToArray();
            if(known.Any(string.IsNullOrWhiteSpace) || known.Distinct(StringComparer.Ordinal).Count()!=known.Length ||
                string.IsNullOrWhiteSpace(expansion.previousPatch) || !expansion.previousPatch.StartsWith(level+":",StringComparison.Ordinal) ||
                expansion.previousPatch==patch || expansion.additions==null || expansion.additions.Length==0 ||
                expansion.additions.Any(id=>string.IsNullOrWhiteSpace(id) || !known.Contains(id,StringComparer.Ordinal)) ||
                expansion.additions.Distinct(StringComparer.Ordinal).Count()!=expansion.additions.Length)
                throw new InvalidOperationException("Некорректное добавочное обновление расстановки: "+patch);
            return world.contentPatches.Contains(expansion.previousPatch)?expansion.additions.ToHashSet(StringComparer.Ordinal):null;
        }
    }
}
