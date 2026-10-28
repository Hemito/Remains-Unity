using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Remains.Content;
using UnityEngine;

namespace Remains.Editor
{
    // One quad per local symbol piece, including source colour; textures may change without duplicating geometry.
    public sealed class PonySymbolMeshCompiler
    {
        private readonly Dictionary<string,Mesh> meshes=new(StringComparer.Ordinal);
        private readonly Dictionary<string,(string socket,HashSet<Mesh> meshes)> symbols=new(StringComparer.Ordinal);
        private readonly Func<Mesh,Mesh> save;
        public int MeshCount=>meshes.Count;
        public float MaximumError {get;private set;}
        public PonySymbolMeshCompiler(Func<Mesh,Mesh> saveMesh)=>save=saveMesh;
        public PonyRenderSymbol[] Symbols=>symbols.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>new PonyRenderSymbol {id=p.Key,socket=p.Value.socket,meshes=p.Value.meshes.OrderBy(m=>m.name,StringComparer.Ordinal).ToArray()}).ToArray();
        public Mesh Piece(PonySymbolLayout.Placement placement,string piece,Vector3[] actorPoints,Color color)
        {
            string key=placement.Id+"/"+piece+"/"+ColorUtility.ToHtmlStringRGBA(color)+"/"+color.r.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+color.g.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+color.b.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+color.a.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
            if(!meshes.TryGetValue(key,out var mesh))
            {
                var inverse=placement.Matrix.inverse;var points=actorPoints.Select(inverse.MultiplyPoint3x4).ToArray();
                using var hash=SHA256.Create();string name="symbol:"+BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-","").ToLowerInvariant().Substring(0,24);
                mesh=new Mesh {name=name,vertices=points,uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one},triangles=new[]{0,2,1,2,3,1},colors=new[]{color,color,color,color}};
                mesh.RecalculateBounds();mesh=save(mesh);meshes.Add(key,mesh);
            }
            var vertices=mesh.vertices;var matrix=placement.Transform.Matrix;
            for(int v=0;v<4;v++)MaximumError=Math.Max(MaximumError,(matrix.MultiplyPoint3x4(vertices[v])-actorPoints[v]).magnitude);
            if(MaximumError>.00001f)throw new InvalidOperationException($"Символ не восстанавливает исходную графику: symbol={placement.Id}, piece={piece}, error={MaximumError:G6} м.");
            if(!symbols.TryGetValue(placement.Id,out var symbol))symbols.Add(placement.Id,symbol=(placement.Socket,new HashSet<Mesh>()));
            symbol.meshes.Add(mesh);return mesh;
        }
    }
}
