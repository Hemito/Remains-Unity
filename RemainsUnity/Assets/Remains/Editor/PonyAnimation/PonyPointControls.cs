using System;
using System.Linq;
using UnityEngine;

namespace Remains.Editor
{
    // Explicit authoring handles in actor space, not alleged original anatomical joints.
    public static class PonyPointControls
    {
        public sealed class Part
        {
            public string Socket,Label;
            public string[] Labels;
            public Vector2[] Rest;
        }
        public static readonly Part[] Parts={
            new() {Socket="hindUpperNear",Label="Круп ближний",Labels=new[]{"Верхняя опора","Колено","Край у туловища"},
                Rest=new[]{new Vector2(-.22f,.73f),new Vector2(-.24f,.40f),new Vector2(-.01f,.64f)}},
            new() {Socket="hindLowerNear",Label="Голень ближняя",Labels=new[]{"Колено","Копыто","Боковая опора"},
                Rest=new[]{new Vector2(-.24f,.40f),new Vector2(-.30f,0),new Vector2(-.40f,.22f)}}
        };
        public static readonly string[] PresetIds={"rest","lift","back"};
        public static readonly string[] PresetLabels={"Исходная стойка","Поднять заднюю ногу","Отвести заднюю ногу"};
        public static Vector2[][] Preset(string id)
        {
            var result=Parts.Select(p=>(Vector2[])p.Rest.Clone()).ToArray();
            if(id=="rest")return result;
            Vector2 knee,hoof;
            if(id=="lift"){knee=new Vector2(-.09f,.45f);hoof=new Vector2(-.10f,.20f);}
            else if(id=="back"){knee=new Vector2(-.35f,.42f);hoof=new Vector2(-.49f,.03f);}
            else throw new InvalidOperationException("Неизвестная проба трёх опор: "+id);
            result[0][1]=knee;result[1][0]=knee;result[1][1]=hoof;
            // Preserve the authored width while relocating a segment's two endpoints.
            Vector2 oldAxis=Parts[1].Rest[1]-Parts[1].Rest[0],axis=hoof-knee,offset=Parts[1].Rest[2]-Parts[1].Rest[0];
            float along=Vector2.Dot(offset,oldAxis)/oldAxis.sqrMagnitude;
            float across=(oldAxis.x*offset.y-oldAxis.y*offset.x)/oldAxis.magnitude;
            result[1][2]=knee+axis*along+new Vector2(-axis.y,axis.x).normalized*across;
            return result;
        }
        public static Matrix4x4[] Matrices(PonyAnimationArtwork art,Vector2[][] points)
        {
            if(points?.Length!=Parts.Length)throw new InvalidOperationException("Повреждены опоры пробы пони.");
            var matrices=art.Frame.Nodes.Select(n=>n.transform.Matrix).ToArray();
            for(int p=0;p<Parts.Length;p++)
            {
                int node=Array.FindIndex(art.Frame.Nodes,n=>n.socket==Parts[p].Socket);
                if(node<0)throw new InvalidOperationException("В рисунке нет гнезда "+Parts[p].Socket);
                var delta=PonyAffinePoints.Solve(Parts[p].Rest,points[p]);
                if(!PonyAffinePoints.AcceptEdit(delta,out var reason))throw new InvalidOperationException(Parts[p].Label+": "+reason);
                matrices[node]=delta*matrices[node];
            }
            return matrices;
        }
    }
}
