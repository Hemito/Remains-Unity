using System;
using UnityEngine;

namespace Remains.Content
{
    public sealed class PonyNestedEyeAsset:ScriptableObject
    {
        public const string Resource="Original/Pony/Nested/eye-closed";
        [SerializeField] private int schema;
        [SerializeField] private string sourceHash,baseSourceHash;
        [SerializeField] private Mesh mesh;
        [SerializeField] private Material material;
        [SerializeField] private Vector3[] canonicalPoints;
        public Mesh Mesh=>mesh;
        public Material Material=>material;
        public Vector3[] Points=>canonicalPoints;
        public void Configure(string hash,string baseHash,Mesh value,Material artwork,Vector3[] points)
        {schema=1;sourceHash=hash;baseSourceHash=baseHash;mesh=value;material=artwork;canonicalPoints=(Vector3[])points.Clone();}
        public void Validate()
        {
            if(schema!=1 || string.IsNullOrWhiteSpace(sourceHash) || baseSourceHash!=PonyRenderAsset.SourceHash() || mesh==null || !mesh.isReadable || material==null || material.mainTexture==null || canonicalPoints?.Length!=4)
                throw new InvalidOperationException("Не подготовлен вложенный символ закрытого глаза пони; выполните Remains → Пони → Подготовить закрытый глаз.");
            var vertices=mesh.vertices;
            if(vertices.Length!=4)throw new InvalidOperationException("Вложенный глаз пони: требуется quad символа218.");
            for(int p=0;p<4;p++)if(!vertices[p].Equals(canonicalPoints[p]) || !float.IsFinite(vertices[p].x) || !float.IsFinite(vertices[p].y))
                throw new InvalidOperationException("Вложенный глаз пони: повреждена каноническая геометрия218.");
        }
    }
}
