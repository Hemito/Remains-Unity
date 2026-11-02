using System;
using UnityEngine;

namespace Remains.Content
{
    public enum PonyFacialExpression { Neutral, Alert, Closed, Dead }
    [Serializable] public sealed class PonyFacialPiece
    {
        public string shape,slot;
        public Mesh mesh;
        public Material material;
    }
    // Original eye220 states, registered in the same head-local space as the existing closed eye.
    public sealed class PonyFacialAsset:ScriptableObject
    {
        public const string Resource="Original/Pony/Nested/expressions";
        [SerializeField] private string sourceHash,baseSourceHash;
        [SerializeField] private PonyFacialPiece[] alert,dead;
        public PonyFacialPiece[] Alert=>alert;
        public PonyFacialPiece[] Dead=>dead;
        public void Configure(string hash,string baseline,PonyFacialPiece[] wide,PonyFacialPiece[] shut)
        {sourceHash=hash;baseSourceHash=baseline;alert=wide;dead=shut;}
        public void Validate()
        {
            if(string.IsNullOrWhiteSpace(sourceHash) || baseSourceHash!=PonyRenderAsset.SourceHash() || alert?.Length!=3 || dead?.Length!=1)
                throw new InvalidOperationException("Мимика пони: банк отсутствует или устарел; выполните Remains → Пони → Подготовить мимику.");
            foreach(var pieces in new[]{alert,dead})foreach(var p in pieces)
                if(p?.mesh==null || p.material==null || p.material.mainTexture==null || p.mesh.vertexCount!=4 || p.slot is not ("none" or "eye"))
                    throw new InvalidOperationException("Мимика пони: неполный рисунок "+p?.shape+".");
        }
    }
}
