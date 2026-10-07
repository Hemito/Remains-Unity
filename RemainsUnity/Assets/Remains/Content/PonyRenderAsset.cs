using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
namespace Remains.Content
{
    [Serializable] public sealed class PonyRenderPart
    {
        public Mesh mesh;public Material material;public string slot;public bool visible;
        public int hairStyle,eyeStyle,drawOrder,sourceIndex;
        public string sourcePath,sourceShape;
        public Vector3[] points;public Color baseColor;
        public int[] orderPath;
    }
    [Serializable] public sealed class PonyRenderFrame {public PonyRenderPart[] parts;public Bounds bounds;}
    [Serializable] public sealed class PonyRenderClip {public string id;public PonyRenderFrame[] frames;}
    [Serializable] public sealed class PonyRenderHeadStyle {public string id;public PonyRenderClip[] clips;}
    public sealed class PonyRenderAsset : ScriptableObject
    {
        public const int CompilerVersion=4;
        [SerializeField,HideInInspector] private int compilerVersion;
        [SerializeField,HideInInspector] private string sourceHash;
        [SerializeField,HideInInspector] private PonyRenderClip[] clips;
        [SerializeField,HideInInspector] private string headSourceHash;
        [SerializeField,HideInInspector] private PonyRenderHeadStyle[] headStyles;
        public PonyRenderClip[] Clips=>clips;
        public PonyRenderHeadStyle[] HeadStyles=>headStyles;
        public static string SourceHash()
        {
            var source=Resources.Load<TextAsset>("Original/Pony/pony");
            if(source==null) throw new InvalidOperationException("Нет исходной анимации пони.");
            using var hash=SHA256.Create();return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(source.text)));
        }
        public void Configure(PonyRenderClip[] value)=>Configure(value,Array.Empty<PonyRenderHeadStyle>());
        public void Configure(PonyRenderClip[] value,PonyRenderHeadStyle[] heads)
        {
            PonyHeadCatalog.Load();clips=value;headStyles=heads;sourceHash=SourceHash();
            headSourceHash=PonyHeadCatalog.SourceHash;compilerVersion=CompilerVersion;
        }
        public void Validate()
        {
            PonyHeadCatalog.Load();
            if(compilerVersion!=CompilerVersion || sourceHash!=SourceHash() || headSourceHash!=PonyHeadCatalog.SourceHash || clips==null || clips.Length==0)
                throw new InvalidOperationException("Графика пони отсутствует или устарела. Выполните Remains → Пересобрать графику пони.");
            var baseline=new Dictionary<string,PonyRenderClip>(StringComparer.Ordinal);
            foreach(var clip in clips)
            {
                if(clip==null || string.IsNullOrWhiteSpace(clip.id) || clip.frames==null || clip.frames.Length==0 || baseline.ContainsKey(clip.id))
                    throw new InvalidOperationException("Повреждена базовая поза пони: "+clip?.id+".");
                baseline.Add(clip.id,clip);
                foreach(var frame in clip.frames)
                {
                    if(frame?.parts==null || frame.parts.Length==0) throw new InvalidOperationException("Пустой базовый кадр пони: "+clip.id+".");
                    foreach(var part in frame.parts)
                        ValidatePart(part,"pony/"+clip.id);
                }
            }
            if(headStyles==null || headStyles.Length!=PonyHeadCatalog.Styles.Count-1)
                throw new InvalidOperationException($"Неполный набор форм головы: prepared={headStyles?.Length}, expected={PonyHeadCatalog.Styles.Count-1}. Пересоберите графику пони.");
            var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var style in headStyles)
            {
                if(style==null || style.id==Domain.CharacterAppearance.OriginalHeadStyleId || !PonyHeadCatalog.TryGet(style.id,out _) || !seen.Add(style.id) ||
                    style.clips==null || style.clips.Length!=clips.Length)
                    throw new InvalidOperationException("Повреждён набор графики головы: head="+style?.id+".");
                var clipIds=new HashSet<string>(StringComparer.Ordinal);
                foreach(var clip in style.clips)
                {
                    if(clip==null || string.IsNullOrWhiteSpace(clip.id) || !baseline.TryGetValue(clip.id,out var original) || !clipIds.Add(clip.id) ||
                        clip.frames==null || clip.frames.Length!=original.frames.Length)
                        throw new InvalidOperationException($"Неполные позы формы головы: head={style.id}, clip={clip?.id}.");
                    for(int f=0;f<clip.frames.Length;f++)
                    {
                        var frame=clip.frames[f];var source=original.frames[f];
                        if(frame?.parts==null || frame.parts.Length!=1)
                            throw new InvalidOperationException($"Форма головы требует одну замену кадра: head={style.id}, clip={clip.id}, frame={f}.");
                        var replacement=frame.parts[0];PonyRenderPart target=null;
                        foreach(var part in source.parts) if(part.sourceShape==PonyHeadRenderResolver.SourceShape)
                        {
                            if(target!=null) throw new InvalidOperationException($"Повтор базовой головы: clip={clip.id}, frame={f}, shape={PonyHeadRenderResolver.SourceShape}.");
                            target=part;
                        }
                        if(target==null || replacement==null || replacement.sourceShape!=target.sourceShape || replacement.sourceIndex!=target.sourceIndex ||
                            replacement.sourcePath!=target.sourcePath || replacement.slot!=target.slot || replacement.visible!=target.visible ||
                            replacement.hairStyle!=target.hairStyle || replacement.eyeStyle!=target.eyeStyle || replacement.drawOrder!=target.drawOrder || !replacement.baseColor.Equals(target.baseColor))
                            throw new InvalidOperationException($"Замена головы нарушает базовую привязку/слой: head={style.id}, clip={clip.id}, frame={f}, sourceIndex={replacement?.sourceIndex}.");
                        ValidatePart(replacement,$"head={style.id}/clip={clip.id}/frame={f}");
                        ValidateBounds(frame.bounds,replacement.mesh.bounds,$"head={style.id}/clip={clip.id}/frame={f}");
                    }
                }
            }
        }
        // Startup validation keeps the world Mesh and portrait quad one source.
        // This reads native arrays only when a compiled bank is loaded/registered.
        public static void ValidatePart(PonyRenderPart part,string context)
        {
            if(part==null || part.mesh==null || !part.mesh.isReadable || part.material==null || part.material.mainTexture==null ||
                part.material.shader==null || !part.material.HasProperty("_MainTex") || !part.material.HasProperty("_Color") || !part.material.HasProperty("_Emission") ||
                part.points==null || part.points.Length!=4 || string.IsNullOrEmpty(part.sourcePath))
                throw new InvalidOperationException($"Повреждена подготовленная графическая часть: context={context}, path={part?.sourcePath}.");
            var vertices=part.mesh.vertices;var uv=part.mesh.uv;var colors=part.mesh.colors;var triangles=part.mesh.triangles;
            if(vertices.Length!=4 || uv.Length!=4 || colors.Length!=4 || triangles.Length!=6 ||
                triangles[0]!=0 || triangles[1]!=2 || triangles[2]!=1 || triangles[3]!=2 || triangles[4]!=3 || triangles[5]!=1 ||
                !uv[0].Equals(Vector2.zero) || !uv[1].Equals(Vector2.right) || !uv[2].Equals(Vector2.up) || !uv[3].Equals(Vector2.one))
                throw new InvalidOperationException($"Не совпадает формат quad/UV: context={context}, path={part.sourcePath}.");
            var minimum=part.points[0];var maximum=minimum;
            for(int i=0;i<4;i++)
            {
                var point=part.points[i];
                if(!float.IsFinite(point.x) || !float.IsFinite(point.y) || point.z!=0 || !vertices[i].Equals(point) || !colors[i].Equals(part.baseColor))
                    throw new InvalidOperationException($"Мир/портрет расходятся по вершине или цвету: context={context}, path={part.sourcePath}, point={i}.");
                minimum=Vector3.Min(minimum,point);maximum=Vector3.Max(maximum,point);
            }
            if(!float.IsFinite(part.baseColor.r) || !float.IsFinite(part.baseColor.g) || !float.IsFinite(part.baseColor.b) || !float.IsFinite(part.baseColor.a))
                throw new InvalidOperationException($"Неконечный цвет части: context={context}, path={part.sourcePath}.");
            var expected=new Bounds();expected.SetMinMax(minimum,maximum);ValidateBounds(part.mesh.bounds,expected,context+"/"+part.sourcePath);
        }
        public static void ValidateBounds(Bounds actual,Bounds expected,string context)
        {
            var minimum=actual.min;var maximum=actual.max;
            if(!float.IsFinite(minimum.x) || !float.IsFinite(minimum.y) || !float.IsFinite(minimum.z) ||
                !float.IsFinite(maximum.x) || !float.IsFinite(maximum.y) || !float.IsFinite(maximum.z) ||
                actual.size.x<0 || actual.size.y<0 || actual.size.z<0 ||
                Mathf.Abs(minimum.x-expected.min.x)>0.000001f || Mathf.Abs(minimum.y-expected.min.y)>0.000001f || Mathf.Abs(minimum.z-expected.min.z)>0.000001f ||
                Mathf.Abs(maximum.x-expected.max.x)>0.000001f || Mathf.Abs(maximum.y-expected.max.y)>0.000001f || Mathf.Abs(maximum.z-expected.max.z)>0.000001f)
                throw new InvalidOperationException($"Границы не соответствуют фактической графике: context={context}, actual={actual}, expected={expected}.");
        }
    }
}
