using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Domain
{
    public readonly struct DynamicSurfaceSpec
    {
        public readonly string Id,Texture,MovementSound;
        public readonly bool OneWay,ShotsPassThrough;
        public DynamicSurfaceSpec(string id,bool oneWay,bool shotsPassThrough,string texture,string movementSound)
        {Id=id;OneWay=oneWay;ShotsPassThrough=shotsPassThrough;Texture=texture;MovementSound=movementSound;}
    }
    public readonly struct DynamicSurfacePose
    {
        public readonly string Id;
        public readonly RoomSurface Previous,Current;
        public readonly double PreviousElapsedSeconds,ElapsedSeconds;
        public readonly long Sequence;
        public readonly bool Continuous;
        public Vector2 Delta=>new(Current.x-Previous.x,Current.y-Previous.y);
        public DynamicSurfacePose(string id,RoomSurface previous,RoomSurface current,double previousElapsed,double elapsed,long sequence,bool continuous)
        {Id=id;Previous=previous;Current=current;PreviousElapsedSeconds=previousElapsed;ElapsedSeconds=elapsed;Sequence=sequence;Continuous=continuous;}
    }
    // Explicit topology rebuild; subsequent sampling writes only cached scalar bounds.
    // This frame never owns passengers, clocks, persistence or mechanism state.
    public sealed class DynamicSurfaceFrame
    {
        private sealed class Entry
        {
            public DynamicSurfaceSpec Spec;
            public readonly RoomSurface Previous=new(),Current=new();
            public long Sequence;
            public bool Continuous;
        }
        private Entry[] entries=Array.Empty<Entry>();
        private int published;
        private readonly Dictionary<string,int> indices=new(StringComparer.Ordinal);
        public int Count=>entries.Length;
        public long Sequence {get;private set;}
        public long TopologyVersion {get;private set;}
        public double PreviousElapsedSeconds {get;private set;}
        public double ElapsedSeconds {get;private set;}
        public bool IsPublished=>Count==0 || Sequence>0 && published==Count;
        public bool ReconcileIds(IReadOnlyList<DynamicSurfaceSpec> specs)
        {
            if(specs==null) throw new ArgumentNullException(nameof(specs));
            bool same=specs.Count==entries.Length;
            for(int i=0;same && i<specs.Count;i++) same=Same(specs[i],entries[i].Spec);
            if(same) return false;
            var next=new Entry[specs.Count];var unique=new HashSet<string>(StringComparer.Ordinal);
            for(int i=0;i<specs.Count;i++)
            {
                var spec=specs[i];
                if(string.IsNullOrWhiteSpace(spec.Id) || !unique.Add(spec.Id) || string.IsNullOrWhiteSpace(spec.Texture) || string.IsNullOrWhiteSpace(spec.MovementSound))
                    throw new InvalidOperationException($"Некорректные данные динамической опоры: id={spec.Id}, index={i}, texture={spec.Texture}, sound={spec.MovementSound}.");
                next[i]=new Entry {Spec=spec};Configure(next[i].Current,spec);Configure(next[i].Previous,spec);
            }
            entries=next;indices.Clear();for(int i=0;i<next.Length;i++) indices.Add(next[i].Spec.Id,i);
            Sequence=0;published=0;PreviousElapsedSeconds=ElapsedSeconds=0;TopologyVersion++;return true;
        }
        public void SetPose(string id,Rect previous,Rect current,double previousElapsed,double elapsed,long sequence,bool continuous)
        {
            if(id==null || !indices.TryGetValue(id,out int index)) throw new InvalidOperationException("Не зарегистрирована динамическая опора: "+id);
            SetPose(index,previous,current,previousElapsed,elapsed,sequence,continuous);
        }
        public void SetPose(int index,Rect previous,Rect current,double previousElapsed,double elapsed,long sequence,bool continuous)
        {
            if(index<0 || index>=entries.Length) throw new ArgumentOutOfRangeException(nameof(index));
            var entry=entries[index];
            if(!Finite(previous) || !Finite(current) || !double.IsFinite(previousElapsed) || !double.IsFinite(elapsed) || previousElapsed<0 || elapsed<previousElapsed ||
                sequence<=0 || sequence<Sequence || sequence<=entry.Sequence ||
                continuous && (previous.width!=current.width || previous.height!=current.height || elapsed==previousElapsed && previous!=current))
                throw new InvalidOperationException($"Некорректный физический кадр: surface={entry.Spec.Id}, sequence={sequence}/{Sequence}, clock={previousElapsed}->{elapsed}, continuous={continuous}.");
            if(sequence==Sequence && (PreviousElapsedSeconds!=previousElapsed || ElapsedSeconds!=elapsed))
                throw new InvalidOperationException($"Опоры одного кадра имеют разные часы: surface={entry.Spec.Id}, sequence={sequence}.");
            if(sequence!=Sequence) published=0;
            Sequence=sequence;PreviousElapsedSeconds=previousElapsed;ElapsedSeconds=elapsed;
            SetBounds(entry.Previous,previous);SetBounds(entry.Current,current);entry.Sequence=sequence;entry.Continuous=continuous;
            published++;
        }
        public DynamicSurfacePose Pose(int index)
        {
            RequirePublished();
            var entry=entries[index];
            if(entry.Sequence!=Sequence || Sequence==0)
                throw new InvalidOperationException($"Не опубликован общий кадр динамических опор: surface={entry.Spec.Id}, sequence={entry.Sequence}/{Sequence}.");
            return new DynamicSurfacePose(entry.Spec.Id,entry.Previous,entry.Current,PreviousElapsedSeconds,ElapsedSeconds,Sequence,entry.Continuous);
        }
        public void RequirePublished()
        {if(!IsPublished) throw new InvalidOperationException($"Незавершённый физический кадр динамических опор: sequence={Sequence}, published={published}/{Count}.");}
        private static bool Same(DynamicSurfaceSpec a,DynamicSurfaceSpec b)=>a.Id==b.Id && a.OneWay==b.OneWay && a.ShotsPassThrough==b.ShotsPassThrough && a.Texture==b.Texture && a.MovementSound==b.MovementSound;
        private static bool Finite(Rect bounds)=>float.IsFinite(bounds.xMin) && float.IsFinite(bounds.yMin) && float.IsFinite(bounds.xMax) && float.IsFinite(bounds.yMax) && bounds.width>0 && bounds.height>0;
        private static void Configure(RoomSurface surface,DynamicSurfaceSpec spec)
        {surface.oneWay=spec.OneWay;surface.shotsPassThrough=spec.ShotsPassThrough;surface.texture=spec.Texture;surface.movementSound=spec.MovementSound;}
        private static void SetBounds(RoomSurface surface,Rect bounds) {surface.x=bounds.x;surface.y=bounds.y;surface.width=bounds.width;surface.height=bounds.height;}
    }
}
