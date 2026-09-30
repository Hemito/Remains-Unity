using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Domain
{
    [Serializable] public sealed class HitRegionDefinition { public string id; public float x,y,radius; }
    [Serializable] public sealed class AnatomyDefinition { public string id; public HitRegionDefinition[] regions; }
    [Serializable] public sealed class AnatomyCatalog { public AnatomyDefinition[] anatomies; }
    public static class Anatomy
    {
        private static readonly Dictionary<string,AnatomyDefinition> definitions=new();
        public static void Load()
        {
            definitions.Clear();
            var asset=Resources.Load<TextAsset>("Content/anatomy");
            if(asset==null) throw new InvalidOperationException("Не найдены определения анатомии Content/anatomy.");
            var catalog=JsonUtility.FromJson<AnatomyCatalog>(asset.text);
            foreach(var definition in catalog.anatomies)
            {
                var ids=new HashSet<string>();
                foreach(var region in definition.regions)
                    if(string.IsNullOrEmpty(region.id) || !ids.Add(region.id) || region.radius<=0)
                        throw new InvalidOperationException($"Некорректная область {region.id} анатомии {definition.id}.");
                definitions.Add(definition.id,definition);
            }
        }

        public static string Contact(ActorState actor,AttackEvent attack)
        {
            if(!definitions.TryGetValue(actor.anatomy,out var definition))
            {
                Debug.LogWarning($"Отсутствует анатомия {actor.anatomy} у {actor.id}; попадание пропущено.");
                return null;
            }
            foreach(var region in definition.regions)
            {
                var center=actor.Position+(actor.down?new Vector2(region.y-0.6f,0.2f):new Vector2(region.x*actor.facing,region.y));
                float distance=attack.weapon=="grenade"?Vector2.Distance(center,attack.End):HitGeometry.SegmentDistance(center,attack.Origin,attack.End);
                if(distance<(attack.weapon=="grenade"?3:region.radius)) return region.id;
            }
            return null;
        }
    }
}
