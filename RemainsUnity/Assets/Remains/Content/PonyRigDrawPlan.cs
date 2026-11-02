using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Remains.Content
{
    // Built once per selected wardrobe, outside animation updates. Geometry stays in the current shared bank for this first integration.
    public sealed class PonyRigDrawPlan
    {
        public sealed class Frame {public PonyRigNode[] Nodes;public int[] PartNodes;public Bounds Bounds;}
        private readonly Dictionary<string,Frame[]> clips=new(StringComparer.Ordinal);
        private readonly Dictionary<string,Frame[]> closedClips=new(StringComparer.Ordinal);
        private readonly Dictionary<string,Frame[]> alertClips=new(StringComparer.Ordinal),deadClips=new(StringComparer.Ordinal);
        private static PonyRigMotionAsset motion;
        private static ConditionalWeakTable<WearableDrawPlan,PonyRigDrawPlan> prepared=new();
        public Bounds CameraEnvelope {get;private set;}
        public Frame Get(string clip,int frame,bool closed=false)=>(closed?closedClips:clips)[clip][frame];
        public Frame Get(string clip,int frame,PonyFacialExpression expression)=>(expression switch {PonyFacialExpression.Closed=>closedClips,PonyFacialExpression.Alert=>alertClips,PonyFacialExpression.Dead=>deadClips,_=>clips})[clip][frame];
        public static void Load()
        {
            if(motion!=null)return;
            var asset=GameAssets.Load<PonyRigMotionAsset>("Original/Pony/rig-motion")??throw new InvalidOperationException("Нет игрового банка куклы Original/Pony/rig-motion.");
            asset.Validate();motion=asset;
        }
        public static PonyRigDrawPlan Prepare(WearableDrawPlan plan)
        {Load();return prepared.GetValue(plan,p=>new PonyRigDrawPlan(p));}
        public static PonyRigDrawPlan For(WearableDrawPlan plan)=>prepared.TryGetValue(plan,out var value)?value:
            throw new InvalidOperationException("Границы куклы запрошены до события подготовки внешности.");
        public static void Reset(){motion=null;prepared=new();}
        public static Frame PrepareFrame(string clip,int index,PonyRenderPart[] parts)
        {
            Load();var track=Array.Find(motion.Clips,c=>c.id==clip)??throw new InvalidOperationException("Кукла пони: нет трека "+clip);
            return PrepareFrame(track.frames[index].nodes,parts,clip+"/"+index);
        }
        private static Frame PrepareFrame(PonyRigNode[] nodes,PonyRenderPart[] parts,string context)
        {
            var lookup=new Dictionary<string,int>(StringComparer.Ordinal);
            var matrices=new Matrix4x4[nodes.Length];
            for(int n=0;n<nodes.Length;n++){lookup.Add(nodes[n].top,n);matrices[n]=nodes[n].transform.Matrix;}
            var frame=new Frame {Nodes=nodes,PartNodes=new int[parts.Length]};bool found=false;
            for(int p=0;p<parts.Length;p++)
            {
                var part=parts[p];int slash=part.sourcePath.IndexOf('/');string top=slash<0?part.sourcePath:part.sourcePath.Substring(0,slash);
                if(!lookup.TryGetValue(top,out int node))throw new InvalidOperationException($"Кукла пони: нет узла для детали {context}/{part.sourcePath}.");
                frame.PartNodes[p]=node;
                var bounds=TransformBounds(part.mesh.bounds,matrices[node]);
                if(!found){frame.Bounds=bounds;found=true;}else frame.Bounds.Encapsulate(bounds);
            }
            if(!found)throw new InvalidOperationException("Кукла пони: пустой рисунок "+context);
            return frame;
        }
        public static Bounds CameraBounds(PonyRenderAsset asset)
        {
            Load();bool found=false;Bounds envelope=default;
            void Include(PonyRenderClip[] bank)
            {
                foreach(var clip in bank)
                {
                    var track=Array.Find(motion.Clips,c=>c.id==clip.id)??throw new InvalidOperationException("Кукла пони: нет трека "+clip.id);
                    for(int f=0;f<clip.frames.Length;f++)foreach(var part in clip.frames[f].parts)
                    {
                        int slash=part.sourcePath.IndexOf('/');string top=slash<0?part.sourcePath:part.sourcePath.Substring(0,slash);
                        var node=Array.Find(track.frames[f].nodes,n=>n.top==top)??throw new InvalidOperationException("Кукла пони: нет узла "+part.sourcePath);
                        var transformed=TransformBounds(part.mesh.bounds,node.transform.Matrix);
                        if(!found){envelope=transformed;found=true;}else envelope.Encapsulate(transformed);
                    }
                }
            }
            Include(asset.Clips);foreach(var style in asset.HeadStyles)Include(style.clips);
            float x=Mathf.Max(Mathf.Abs(envelope.min.x),Mathf.Abs(envelope.max.x));
            envelope.SetMinMax(new Vector3(-x,envelope.min.y,envelope.min.z),new Vector3(x,envelope.max.y,envelope.max.z));return envelope;
        }
        public static Bounds TransformBounds(Bounds bounds,Matrix4x4 matrix)
        {
            var result=new Bounds(matrix.MultiplyPoint3x4(bounds.min),Vector3.zero);
            for(int corner=1;corner<4;corner++)result.Encapsulate(matrix.MultiplyPoint3x4(new Vector3((corner&1)==0?bounds.min.x:bounds.max.x,(corner&2)==0?bounds.min.y:bounds.max.y,0)));
            return result;
        }
        private PonyRigDrawPlan(WearableDrawPlan plan)
        {
            bool envelopeFound=false;Bounds envelope=default;
            foreach(var clip in motion.Clips)
            {
                var parts=plan.Frames(clip.id);var closed=plan.Frames(clip.id,true);var frames=new Frame[parts.Length];var closedFrames=new Frame[parts.Length];
                for(int f=0;f<frames.Length;f++)
                {
                    var frame=PrepareFrame(clip.frames[f].nodes,parts[f],clip.id+"/"+f);
                    frames[f]=frame;
                    closedFrames[f]=ReferenceEquals(parts[f],closed[f])?frame:PrepareFrame(clip.frames[f].nodes,closed[f],clip.id+"/closed/"+f);
                    if(!envelopeFound){envelope=frame.Bounds;envelopeFound=true;}else envelope.Encapsulate(frame.Bounds);
                }
                clips.Add(clip.id,frames);closedClips.Add(clip.id,closedFrames);
                foreach(var expression in new[]{PonyFacialExpression.Alert,PonyFacialExpression.Dead})
                {
                    var variants=plan.Frames(clip.id,expression);var preparedFrames=new Frame[frames.Length];
                    for(int f=0;f<frames.Length;f++)preparedFrames[f]=ReferenceEquals(parts[f],variants[f])?frames[f]:PrepareFrame(clip.frames[f].nodes,variants[f],clip.id+"/"+expression+"/"+f);
                    (expression==PonyFacialExpression.Alert?alertClips:deadClips).Add(clip.id,preparedFrames);
                }
            }
            float x=Mathf.Max(Mathf.Abs(envelope.min.x),Mathf.Abs(envelope.max.x));
            envelope.SetMinMax(new Vector3(-x,envelope.min.y,envelope.min.z),new Vector3(x,envelope.max.y,envelope.max.z));CameraEnvelope=envelope;
        }
    }
}
