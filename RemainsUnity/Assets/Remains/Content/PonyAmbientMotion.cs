using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Content
{
    public static class PonyAmbientMotion
    {
        private static PonyAmbientMotionAsset bank;
        private static PonyRigTransform[][][] frames;
        private static PonyRigNode[] rest;
        public static PonyAmbientMotionAsset Bank=>bank??throw new InvalidOperationException("Движения ожидания пони не подготовлены.");
        public static void Reset(){bank=null;frames=null;rest=null;}
        public static void Prepare(PonyRigNode[] nodes)
        {
            if(ReferenceEquals(rest,nodes))return;
            if(bank==null){bank=GameAssets.Load<PonyAmbientMotionAsset>(PonyAmbientMotionAsset.Resource)??throw new InvalidOperationException("Нет банка ожидания пони.");bank.Validate();}
            var lookup=new Dictionary<string,int>(StringComparer.Ordinal);
            for(int n=0;n<nodes.Length;n++)lookup.Add(nodes[n].socket,n);
            if(lookup.Count!=17)throw new InvalidOperationException("Ожидание пони: нужна стойка из17 узлов.");
            frames=new PonyRigTransform[bank.Clips.Length][][];
            for(int c=0;c<frames.Length;c++)
            {
                var clip=bank.Clips[c];frames[c]=new PonyRigTransform[clip.frames.Length][];
                for(int f=0;f<clip.frames.Length;f++)
                {
                    var result=new PonyRigTransform[nodes.Length];
                    foreach(var node in clip.frames[f].nodes)
                    {
                        int n=lookup[node.socket];result[n]=PonyRigTransform.FromMatrix(node.transform.Matrix*nodes[n].transform.Matrix);
                    }
                    frames[c][f]=result;
                }
            }
            rest=nodes;
        }
        public static PonyRigTransform[] Frame(int clip,int frame)=>frames[clip][frame];
    }
}
