using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    // Editor diagnostic. The raster decides whether a triangle actually carries artwork;
    // folds in unused transparent margins do not count as visible damage.
    public static class NativePonyDeformationMetrics
    {
        public readonly struct Result
        {
            public readonly int VisibleTriangles,FlippedTriangles,CollapsedTriangles;
            public readonly float MinAreaRatio,MaxEdgeStretch;
            public Result(int visible,int flipped,int collapsed,float minArea,float stretch)
            {VisibleTriangles=visible;FlippedTriangles=flipped;CollapsedTriangles=collapsed;MinAreaRatio=minArea;MaxEdgeStretch=stretch;}
        }
        public sealed class Probe
        {
            private readonly Vector3[] rest;
            private readonly int[] indices;
            private readonly bool[] visible;
            public Probe(Sprite sprite,float alphaThreshold=.15f)
            {
                if(sprite==null)throw new ArgumentNullException(nameof(sprite));
                string path=AssetDatabase.GetAssetPath(sprite);
                if(!File.Exists(path) || !path.EndsWith(".png",StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Проверка деформации: нужен исходный PNG спрайта "+sprite.name);
                rest=sprite.GetVertexAttribute<Vector3>(VertexAttribute.Position).ToArray();
                var uv=sprite.GetVertexAttribute<Vector2>(VertexAttribute.TexCoord0).ToArray();
                indices=sprite.GetIndices().Select(i=>(int)i).ToArray();visible=new bool[indices.Length/3];
                var pixels=new Texture2D(2,2,TextureFormat.RGBA32,false);
                try
                {
                    if(!pixels.LoadImage(File.ReadAllBytes(path)))throw new InvalidOperationException("Проверка деформации: не читается PNG "+path);
                    var alpha=pixels.GetPixels32();
                    for(int triangle=0;triangle<visible.Length;triangle++)
                    {
                        var a=uv[indices[triangle*3]];var b=uv[indices[triangle*3+1]];var c=uv[indices[triangle*3+2]];
                        int covered=0,total=0;
                        // Ten interior points, including the centre. Avoid exact shared vertices/edges.
                        for(int u=0;u<4;u++)for(int v=0;v<4-u;v++)
                        {
                            float wa=(u+.3333333f)/4f,wb=(v+.3333333f)/4f;var point=a*wa+b*wb+c*(1-wa-wb);
                            int x=Mathf.Clamp(Mathf.RoundToInt(point.x*(pixels.width-1)),0,pixels.width-1);
                            int y=Mathf.Clamp(Mathf.RoundToInt(point.y*(pixels.height-1)),0,pixels.height-1);
                            if(alpha[y*pixels.width+x].a/255f>=alphaThreshold)covered++;total++;
                        }
                        visible[triangle]=covered>=Mathf.CeilToInt(total*.2f);
                    }
                }
                finally {Object.DestroyImmediate(pixels);}
            }
            public Result Measure(IEnumerable<Vector3> deformedVertices)
            {
                var deformed=deformedVertices.ToArray();
                if(deformed.Length!=rest.Length)throw new InvalidOperationException("Проверка деформации: число вершин изменилось.");
                int count=0,flipped=0,collapsed=0;float minArea=float.PositiveInfinity,maxStretch=0;
                for(int triangle=0;triangle<visible.Length;triangle++)if(visible[triangle])
                {
                    int ia=indices[triangle*3],ib=indices[triangle*3+1],ic=indices[triangle*3+2];
                    var a=rest[ia];var b=rest[ib];var c=rest[ic];var da=deformed[ia];var db=deformed[ib];var dc=deformed[ic];
                    float before=Area(a,b,c);
                    if(Mathf.Abs(before)<1e-10f)throw new InvalidOperationException("Проверка деформации: вырожденный исходный треугольник "+triangle);
                    float ratio=Area(da,db,dc)/before;
                    if(!float.IsFinite(ratio))throw new InvalidOperationException("Проверка деформации: неконечная площадь треугольника "+triangle);
                    minArea=Mathf.Min(minArea,ratio);count++;
                    if(ratio<0)flipped++;else if(ratio<.15f)collapsed++;
                    maxStretch=Mathf.Max(maxStretch,Stretch(a,b,da,db),Stretch(b,c,db,dc),Stretch(c,a,dc,da));
                }
                if(count==0)throw new InvalidOperationException("Проверка деформации: не найдены видимые треугольники.");
                return new Result(count,flipped,collapsed,minArea,maxStretch);
            }
            private static float Area(Vector3 a,Vector3 b,Vector3 c)=>(b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x);
            private static float Stretch(Vector3 a,Vector3 b,Vector3 da,Vector3 db)=>Vector3.Distance(da,db)/Mathf.Max(1e-8f,Vector3.Distance(a,b));
        }
    }
}
