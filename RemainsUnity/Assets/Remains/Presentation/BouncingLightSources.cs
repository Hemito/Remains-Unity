using System.Collections.Generic;
using Remains.Content;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Presentation
{
    // The same locally simulated flight supplies the sprite and its light; never a second physics body.
    public sealed class BouncingLightSources
    {
        private sealed class Entry {public string Id;public readonly Vector4[] Samples=new Vector4[1];}
        private readonly Dictionary<string,Entry> entries=new();
        private readonly HashSet<string> retained=new();
        private readonly List<string> removed=new();
        public void Append(GrenadeController controller,string level,List<WorldLightEmission> output,Rect? area=null)
            =>Append(controller.Flights,level,output,area);
        public void Append(IReadOnlyList<GrenadeFlight> flights,string level,List<WorldLightEmission> output,Rect? area=null)
        {
            retained.Clear();
            foreach(var flight in flights)
            {
                var state=flight.state;
                if(state.level!=level || state.remaining<=0)continue;
                var definition=WeaponCatalog.Get(state.weapon);
                if(!definition.IsThrowableLight)continue;
                var profile=definition.throwableLight;var at=flight.position;
                if(area.HasValue && !area.Value.Overlaps(new Rect(at-Vector2.one*profile.reach,Vector2.one*(profile.reach*2))))continue;
                retained.Add(state.id);
                if(!entries.TryGetValue(state.id,out var entry))entries.Add(state.id,entry=new Entry{Id="throwable-light:"+state.id});
                entry.Samples[0]=new Vector4(at.x,at.y,1,0);
                // Its last second fades smoothly; falloff in distance belongs to the common light shader.
                output.Add(new WorldLightEmission(entry.Id,entry.Samples,new Rect(at-Vector2.one*.02f,Vector2.one*.04f),
                    profile.radiance,profile.width,profile.reach,Mathf.Clamp01(state.remaining)));
            }
            removed.Clear();foreach(string id in entries.Keys)if(!retained.Contains(id))removed.Add(id);
            foreach(string id in removed)entries.Remove(id);
        }
        public void Clear(){entries.Clear();retained.Clear();removed.Clear();}
    }
}
