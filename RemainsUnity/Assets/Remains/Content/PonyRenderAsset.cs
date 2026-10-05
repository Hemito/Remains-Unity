using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
namespace Remains.Content
{
    [Serializable] public sealed class PonyRenderPart {public Mesh mesh;public Material material;public string slot;public bool visible;public int hairStyle,eyeStyle,drawOrder;}
    [Serializable] public sealed class PonyRenderFrame {public PonyRenderPart[] parts;public Bounds bounds;}
    [Serializable] public sealed class PonyRenderClip {public string id;public PonyRenderFrame[] frames;}
    public sealed class PonyRenderAsset : ScriptableObject
    {
        public const int CompilerVersion=2;
        [SerializeField,HideInInspector] private int compilerVersion;
        [SerializeField,HideInInspector] private string sourceHash;
        [SerializeField,HideInInspector] private PonyRenderClip[] clips;
        public PonyRenderClip[] Clips=>clips;
        public static string SourceHash()
        {
            var source=Resources.Load<TextAsset>("Original/Pony/pony");
            if(source==null) throw new InvalidOperationException("Нет исходной анимации пони.");
            using var hash=SHA256.Create();return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(source.text)));
        }
        public void Configure(PonyRenderClip[] value) {clips=value;sourceHash=SourceHash();compilerVersion=CompilerVersion;}
        public void Validate()
        {
            if(compilerVersion!=CompilerVersion || sourceHash!=SourceHash() || clips==null || clips.Length==0)
                throw new InvalidOperationException("Графика пони отсутствует или устарела. Выполните Remains → Пересобрать графику пони.");
            foreach(var clip in clips)
                foreach(var frame in clip.frames)
                    foreach(var part in frame.parts)
                        if(part.mesh==null || part.material==null || part.material.mainTexture==null)
                            throw new InvalidOperationException($"Графика пони повреждена: {clip.id}, область {part.slot}.");
        }
    }
}
