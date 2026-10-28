using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Content
{
    // Two TRS nodes retain the full 2D affine, including shear and reflection.
    [Serializable] public struct PonyRigTransform
    {
        public Vector3 position,scale;
        public Quaternion rotation,innerRotation;
        public Matrix4x4 Matrix=>Matrix4x4.TRS(position,rotation,scale)*Matrix4x4.Rotate(innerRotation);
        public static PonyRigTransform FromMatrix(Matrix4x4 m)
        {
            double a=m.m00,b=m.m01,c=m.m10,d=m.m11;
            double theta=.5*Math.Atan2(2*(a*b+c*d),a*a+c*c-b*b-d*d);
            double x=a*Math.Cos(theta)+b*Math.Sin(theta),y=c*Math.Cos(theta)+d*Math.Sin(theta);
            double sx=Math.Sqrt(x*x+y*y),det=a*d-b*c;
            if(!double.IsFinite(sx) || !double.IsFinite(det) || sx<1e-9 || Math.Abs(det)<1e-10)
                throw new InvalidOperationException("Кукла пони: вырожденное преобразование узла.");
            return new PonyRigTransform {position=new Vector3(m.m03,m.m13,0),scale=new Vector3((float)sx,(float)(det/sx),1),
                rotation=Quaternion.Euler(0,0,(float)(Math.Atan2(y,x)*Mathf.Rad2Deg)),innerRotation=Quaternion.Euler(0,0,(float)(-theta*Mathf.Rad2Deg))};
        }
    }
    [Serializable] public sealed class PonyRigNode {public string top,socket;public PonyRigTransform transform;}
    [Serializable] public sealed class PonyRigFrame {public PonyRigNode[] nodes;}
    [Serializable] public sealed class PonyRigClip {public string id,source;public PonyRigFrame[] frames;}
    public sealed class PonyRigMotionAsset:ScriptableObject
    {
        [SerializeField] private string baseSourceHash,sourceCommit;
        [SerializeField] private int schema;
        [SerializeField] private PonyRigClip[] clips;
        public PonyRigClip[] Clips=>clips;
        public string SourceCommit=>sourceCommit;
        public void Configure(string hash,string commit,PonyRigClip[] value){schema=2;baseSourceHash=hash;sourceCommit=commit;clips=value;}
        public void Validate()
        {
            PonyAnimationData.Load();
            if(schema!=2 || baseSourceHash!=PonyRenderAsset.SourceHash() || string.IsNullOrEmpty(sourceCommit) || clips?.Length!=PonyAnimationData.Library.clips.Length)
                throw new InvalidOperationException("Банк движений куклы пони устарел; пересоберите его в Remains → Собрать игровые движения куклы.");
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var clip in clips)
            {
                var original=Array.Find(PonyAnimationData.Library.clips,c=>c.id==clip.id);
                if(!ids.Add(clip.id) || original==null || clip.frames?.Length!=original.frames.Length || string.IsNullOrEmpty(clip.source))
                    throw new InvalidOperationException("Кукла пони: неполная поза "+clip.id);
                foreach(var frame in clip.frames)
                {
                    var sockets=new HashSet<string>(StringComparer.Ordinal);var tops=new HashSet<string>(StringComparer.Ordinal);
                    if(frame?.nodes?.Length!=17)throw new InvalidOperationException("Кукла пони: требуется 17 узлов в "+clip.id);
                    foreach(var node in frame.nodes)
                    {
                        if(string.IsNullOrEmpty(node.top) || string.IsNullOrEmpty(node.socket) || !tops.Add(node.top) || !sockets.Add(node.socket))
                            throw new InvalidOperationException("Кукла пони: неоднозначные узлы в "+clip.id);
                        var matrix=node.transform.Matrix;
                        for(int i=0;i<16;i++)if(!float.IsFinite(matrix[i]))throw new InvalidOperationException("Кукла пони: неконечная матрица "+clip.id+"/"+node.socket);
                        if(Math.Abs(matrix.determinant)<1e-10)throw new InvalidOperationException("Кукла пони: вырожденная матрица "+clip.id+"/"+node.socket);
                    }
                }
            }
        }
    }
}
