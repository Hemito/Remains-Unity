using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Content
{
    [Serializable] public sealed class PonyAmbientMotionNode {public string socket;public PonyRigTransform transform;}
    [Serializable] public sealed class PonyAmbientMotionFrame {public PonyAmbientMotionNode[] nodes;}
    [Serializable] public sealed class PonyAmbientMotionClip
    {
        public string id;
        public float fps;
        public PonyAmbientMotionFrame[] frames;
        public float Duration=>frames.Length/fps;
    }

    // Cosmetic deltas of the original's three free/stay motions. The already prepared idle meshes, masks and
    // wardrobe remain shared; a delta is left-multiplied onto that socket's actual idle rig placement.
    public sealed class PonyAmbientMotionAsset:ScriptableObject
    {
        public const string Resource="Original/Pony/ambient-motion";
        public const int Schema=1;
        public static readonly string[] Sockets={"hindLowerFar","hindUpperFar","frontUpperFar","frontMiddleFar","frontLowerFar","pipbuckFar",
            "tail","torso","neck","mane","head","frontUpperNear","frontMiddleNear","frontLowerNear","hindLowerNear","hindUpperNear","pipbuckNear"};
        [SerializeField,HideInInspector] private int schema;
        [SerializeField,HideInInspector] private string sourceHash,baseSourceHash;
        [SerializeField,HideInInspector] private PonyAmbientMotionClip[] clips;
        public PonyAmbientMotionClip[] Clips=>clips;
        public string SourceHash=>sourceHash;
        public string BaseSourceHash=>baseSourceHash;
        public void Configure(string source,string baseline,PonyAmbientMotionClip[] values)
        {schema=Schema;sourceHash=source;baseSourceHash=baseline;clips=values;}

        public void Validate()
        {
            if(schema!=Schema || string.IsNullOrWhiteSpace(sourceHash) || baseSourceHash!=PonyRenderAsset.SourceHash() || clips?.Length!=3)
                throw new InvalidOperationException("Ожидание пони: банк движений отсутствует или устарел; выполните Remains → Пони → Подготовить движения ожидания.");
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var clip in clips)
            {
                if(clip==null || clip.id is not ("free1" or "free2" or "free3") || !ids.Add(clip.id) ||
                    !float.IsFinite(clip.fps) || clip.fps<=0 || clip.frames==null || clip.frames.Length<2)
                    throw new InvalidOperationException("Ожидание пони: неполный клип "+clip?.id+".");
                for(int f=0;f<clip.frames.Length;f++)
                {
                    var frame=clip.frames[f];var sockets=new HashSet<string>(StringComparer.Ordinal);
                    if(frame?.nodes?.Length!=Sockets.Length)
                        throw new InvalidOperationException($"Ожидание пони: clip={clip.id}, frame={f}, требуется {Sockets.Length} узлов.");
                    foreach(var node in frame.nodes)
                    {
                        if(node==null || Array.IndexOf(Sockets,node.socket)<0 || !sockets.Add(node.socket))
                            throw new InvalidOperationException($"Ожидание пони: clip={clip.id}, frame={f}, неизвестное или повторное гнездо {node?.socket}.");
                        var matrix=node.transform.Matrix;
                        for(int i=0;i<16;i++)if(!float.IsFinite(matrix[i]))
                            throw new InvalidOperationException($"Ожидание пони: clip={clip.id}, frame={f}, socket={node.socket}, неконечная матрица.");
                        if(Math.Abs(matrix.determinant)<1e-10)
                            throw new InvalidOperationException($"Ожидание пони: clip={clip.id}, frame={f}, socket={node.socket}, вырожденная матрица.");
                    }
                }
            }
        }
    }
}
