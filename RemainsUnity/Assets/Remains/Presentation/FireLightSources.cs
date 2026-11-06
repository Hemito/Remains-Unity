using System.Collections.Generic;
using Remains.Domain;
using UnityEngine;

namespace Remains.Presentation
{
    // One emitter per canonical fire, with up to eight samples of its actual burning patches.
    // Bottle/corpse movement changes only their own light. Flame frame swaps have no effect on the field.
    public sealed class FireLightSources
    {
        private sealed class Entry {public string Id;public Vector4[] Points;public Rect Bounds;public float Width,Reach;}
        private readonly Dictionary<(int Kind,string Id),Entry> entries=new();
        private readonly HashSet<(int Kind,string Id)> retained=new();
        private readonly List<(int Kind,string Id)> gone=new();
        private readonly List<WorldLightEmission> emissions=new();
        public IReadOnlyList<WorldLightEmission> Emissions=>emissions;
        // Deliberate presentation tuning, independent of damage/fuel and animation-frame brightness.
        public static readonly Color Radiance=new(10,2.6f,.35f,1);
        public void Begin(){retained.Clear();emissions.Clear();}
        public void Fire(FireState fire,IReadOnlyList<FirePatch> patches,float gain)
        {
            if(patches.Count==0 || gain<=0)return;
            var key=(0,fire.id);retained.Add(key);
            if(!entries.TryGetValue(key,out var entry))
            {
                int count=Mathf.Min(DirectLight.MaximumSamples,patches.Count);var points=new Vector4[count];
                float total=0;Vector2 min=new(float.PositiveInfinity,float.PositiveInfinity),max=new(float.NegativeInfinity,float.NegativeInfinity);
                for(int i=0;i<count;i++)
                {
                    var patch=patches[(int)((long)i*patches.Count/count)];var at=patch.position+Vector2.up*.22f;
                    float weight=Mathf.Max(.05f,patch.radius*2);points[i]=new Vector4(at.x,at.y,weight,0);total+=weight;min=Vector2.Min(min,at);max=Vector2.Max(max,at);
                }
                for(int i=0;i<count;i++)points[i].z/=total;
                entry=new Entry{Id="fire:"+fire.id,Points=points,Bounds=Rect.MinMaxRect(min.x-.02f,min.y-.02f,max.x+.02f,max.y+.02f),Width=Mathf.Clamp(total,.2f,2),Reach=fire.torch?2.5f:4};entries.Add(key,entry);
            }
            Add(entry,gain);
        }
        public void Bottle(string id,Vector2 position)=>Moving(1,id,position+Vector2.up*.12f,.15f,2.5f);
        public void Actor(string id,Vector2 position)=>Moving(2,id,position,.3f,3);
        private void Moving(int kind,string id,Vector2 position,float width,float reach)
        {
            var key=(kind,id);retained.Add(key);
            if(!entries.TryGetValue(key,out var entry))
            {entry=new Entry{Id=(kind==1?"fire-flight:":"burning-actor:")+id,Points=new Vector4[1],Width=width,Reach=reach};entries.Add(key,entry);}
            entry.Points[0]=new Vector4(position.x,position.y,1,0);entry.Bounds=new Rect(position-Vector2.one*.02f,Vector2.one*.04f);Add(entry,1);
        }
        private void Add(Entry entry,float gain)=>emissions.Add(new WorldLightEmission(entry.Id,entry.Points,entry.Bounds,Radiance,entry.Width,entry.Reach,gain));
        public void End()
        {gone.Clear();foreach(var key in entries.Keys)if(!retained.Contains(key))gone.Add(key);foreach(var key in gone)entries.Remove(key);}
        public void Clear(){entries.Clear();retained.Clear();emissions.Clear();}
    }
}
