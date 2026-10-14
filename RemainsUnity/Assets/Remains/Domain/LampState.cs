using System;
using System.Collections.Generic;
using UnityEngine;
using Remains.Serialization;

namespace Remains.Domain
{
    [Serializable]
    public sealed class LampState : ExtensibleState
    {
        public string id,level;
        public float x,y,width,height;
        public bool broken;
        public long revision;
        public string breakingAttack;
        public Rect Bounds=>new(x,y,width,height);
        public void Validate()
        {
            if(string.IsNullOrWhiteSpace(id)||string.IsNullOrWhiteSpace(level)||!float.IsFinite(x)||!float.IsFinite(y)||
                !float.IsFinite(width)||!float.IsFinite(height)||width<=0||height<=0||revision<0)
                throw new InvalidOperationException($"Некорректная лампа: id={id}, level={level}, bounds={Bounds}, revision={revision}.");
        }
    }
    public static class LampOperations
    {
        public static bool Install(WorldState world,string level,IReadOnlyList<LampState> placements)
        {
            if(world==null||placements==null) throw new ArgumentNullException();
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var lamp in placements)
            {
                lamp.Validate();
                if(lamp.level!=level||!ids.Add(lamp.id)) throw new InvalidOperationException($"Повторная или чужая лампа: {lamp.id}, level={level}.");
                var existing=world.lamps.Find(value=>value.id==lamp.id);
                if(existing!=null && (existing.level!=level||existing.Bounds!=lamp.Bounds))
                    throw new InvalidOperationException($"Изменена сохранённая геометрия лампы {lamp.id}.");
            }
            bool changed=false;
            foreach(var lamp in placements) if(!world.lamps.Exists(value=>value.id==lamp.id))
            {world.lamps.Add(Remains.Serialization.StateJson.Copy(lamp));changed=true;}
            if(changed) world.lampRevision++;
            return changed;
        }
        public static bool Break(WorldState world,LampState lamp,string attack)
        {
            lamp.Validate();
            if(string.IsNullOrWhiteSpace(attack)) throw new ArgumentException("Не задан ID атаки лампы.");
            if(lamp.broken) return false;
            lamp.broken=true;lamp.breakingAttack=attack;lamp.revision++;world.lampRevision++;WorldChanges.Mark(world,lamp);return true;
        }
    }
}
