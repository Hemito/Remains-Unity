using System;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Content
{
    [Serializable] public sealed class SpikeArtworkFrame
    {
        public string resource,sha256;
        public int frame;
        public float zoom;
        public Vector2 sourceOrigin;
        public Vector2Int sourceSize;
        public Sprite sprite;
        public void Validate(string context)
        {
            if(string.IsNullOrEmpty(resource) || string.IsNullOrEmpty(sha256) || sha256.Length!=64 || frame<1 ||
                !(zoom>0) || sourceSize.x<1 || sourceSize.y<1 || !float.IsFinite(sourceOrigin.x) || !float.IsFinite(sourceOrigin.y) || sprite==null ||
                Mathf.Abs(sprite.pixelsPerUnit-50*zoom)>.001f)
                throw new InvalidOperationException($"Графика шипов {context}: нет кадра, происхождения или точного масштаба.");
        }
    }
    [Serializable] public sealed class SpikeArtworkDefinition
    {
        public string id,rawXml;
        public SpikeArtworkFrame[] rear=Array.Empty<SpikeArtworkFrame>(),front=Array.Empty<SpikeArtworkFrame>();
        public void Validate()
        {
            if(string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(rawXml) || rear==null || front==null || rear.Length==0 || front.Length==0)
                throw new InvalidOperationException("Графика шипов: нет определения или двух слоёв: "+id);
            foreach(var frame in rear) {if(frame==null) throw new InvalidOperationException("Пустой задний кадр шипов: "+id);frame.Validate(id+"/задний");}
            foreach(var frame in front) {if(frame==null) throw new InvalidOperationException("Пустой передний кадр шипов: "+id);frame.Validate(id+"/передний");}
        }
    }
    // The prepared artwork is separate from the hurting box. No rendering bounds determine damage geometry.
    public sealed class SpikeArtworkCatalog : ScriptableObject
    {
        public const string Resource="Presentation/Spikes/SpikeArtwork";
        [SerializeField] private string manifestSha256;
        [SerializeField] private SpikeVisual prefab;
        [SerializeField] private SpikeArtworkDefinition[] definitions=Array.Empty<SpikeArtworkDefinition>();
        public string ManifestSha256=>manifestSha256;
        public SpikeVisual Prefab=>prefab;
        public System.Collections.Generic.IReadOnlyList<SpikeArtworkDefinition> Definitions=>definitions;
        public void Configure(string hash,SpikeVisual visual,SpikeArtworkDefinition[] values)
        {manifestSha256=hash;prefab=visual;definitions=values;Validate();}
        public void Validate()
        {
            if(string.IsNullOrEmpty(manifestSha256) || manifestSha256.Length!=64 || prefab==null || definitions==null || definitions.Length==0)
                throw new InvalidOperationException("Каталог графики шипов: отсутствуют происхождение, префаб или определения.");
            var ids=new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach(var entry in definitions)
            {if(entry==null || !ids.Add(entry.id)) throw new InvalidOperationException("Пустая или повторная графика шипов: "+entry?.id);entry.Validate();}
            prefab.Validate();
        }
        public SpikeArtworkDefinition Require(string id)
        {
            foreach(var entry in definitions) if(entry.id==id) return entry;
            throw new InvalidOperationException("Нет двухслойной графики шипов: "+id);
        }
    }
}
