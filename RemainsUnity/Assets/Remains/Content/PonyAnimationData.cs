using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Content
{
    [Serializable] public sealed class PonyLibrary { public PonyClip[] clips; }
    [Serializable] public sealed class PonyClip { public string id; public float fps; public bool nonLooping; public PonyFrame[] frames; }
    [Serializable] public sealed class PonyFrame { public PonyPart[] parts; }
    [Serializable] public sealed class PonyPart
    {
        public string shape,texture,path,slot;
        public float[] matrix,color,bounds;
    }

    public static class PonyAnimationData
    {
        public static PonyLibrary Library { get; private set; }
        private static readonly Dictionary<string,PonyClip> clips=new();
        public static void Load()
        {
            if(Library!=null) return;
            var json=Resources.Load<TextAsset>("Original/Pony/pony");
            if(json==null) throw new InvalidOperationException("Нет данных анимации Original/Pony/pony.");
            Library=JsonUtility.FromJson<PonyLibrary>(json.text);
            foreach(var clip in Library.clips) clips.Add(clip.id,clip);
        }
        public static int FrameIndex(string pose,float elapsed,bool down)
        {
            var clip=clips[pose];
            int frame=(int)(Mathf.Max(0,elapsed)*clip.fps);
            return down?clip.frames.Length-1:clip.nonLooping?Mathf.Min(frame,clip.frames.Length-1):frame%clip.frames.Length;
        }
        public static float Duration(string pose)=>clips[pose].frames.Length/clips[pose].fps;
        public static bool TryCenter(string pose,int frame,string[] shapes,out Vector2 center)
        {
            center=Vector2.zero;
            if(!clips.TryGetValue(pose,out var clip)) return false;
            Vector2 minimum=new(float.PositiveInfinity,float.PositiveInfinity),maximum=new(float.NegativeInfinity,float.NegativeInfinity);
            bool found=false;
            foreach(var part in clip.frames[frame].parts)
            {
                if(Array.IndexOf(shapes,part.shape)<0) continue;
                var b=part.bounds;var m=part.matrix;
                for(int corner=0;corner<4;corner++)
                {
                    float x=corner%2==0?b[0]:b[2],y=corner<2?b[1]:b[3];
                    var point=TransformPoint(part,new Vector2(x,y));
                    minimum=Vector2.Min(minimum,point);maximum=Vector2.Max(maximum,point);
                }
                found=true;
            }
            center=(minimum+maximum)/2;
            return found;
        }
        public static Vector3[] Quad(PonyPart part)
        {
            var b=part.bounds;
            return new Vector3[] {TransformPoint(part,new Vector2(b[0],b[3])),TransformPoint(part,new Vector2(b[2],b[3])),
                TransformPoint(part,new Vector2(b[0],b[1])),TransformPoint(part,new Vector2(b[2],b[1]))};
        }
        public static Vector2 TransformPoint(PonyPart part,Vector2 point)
        {
            var m=part.matrix;
            return new Vector2((m[0]*point.x+m[2]*point.y+m[4])/1000,-(m[1]*point.x+m[3]*point.y+m[5])/1000);
        }
    }
}
