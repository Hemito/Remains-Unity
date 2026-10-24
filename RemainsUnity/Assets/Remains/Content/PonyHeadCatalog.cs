using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Remains.Domain;
using UnityEngine;

namespace Remains.Content
{
    [Serializable] public sealed class PonyHeadStyleDefinition
    {
        public string id,label,texture,source;
        public float[] bounds;
        public int sourceNumber;
    }
    [Serializable] public sealed class PonyHeadStyleList
    {public int version;public PonyHeadStyleDefinition[] styles;}

    // Authored decorative heads only. The original animation/anatomy remains the
    // gameplay source; catalogue queries never load textures or inspect geometry.
    public static class PonyHeadCatalog
    {
        public const string Resource="Content/pony-heads";
        private static IReadOnlyList<PonyHeadStyleDefinition> styles;
        private static Dictionary<string,PonyHeadStyleDefinition> byId;
        public static bool IsLoaded=>byId!=null;
        public static IReadOnlyList<PonyHeadStyleDefinition> Styles=>styles??throw new InvalidOperationException("Каталог форм головы не подготовлен: "+Resource+".");
        public static string SourceJsonText {get;private set;}
        public static string SourceHash {get;private set;}
        public static void Load()
        {
            if(IsLoaded) return;
            var asset=GameAssets.Load<TextAsset>(Resource);
            if(asset==null) throw new InvalidOperationException("Нет каталога форм головы: "+Resource+".");
            var source=JsonUtility.FromJson<PonyHeadStyleList>(asset.text);
            Validate(source,Resource);
            var prepared=new Dictionary<string,PonyHeadStyleDefinition>(StringComparer.Ordinal);
            foreach(var style in source.styles) prepared.Add(style.id,style);
            using var hash=SHA256.Create();
            string fingerprint=Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(asset.text)));
            styles=Array.AsReadOnly(source.styles);SourceJsonText=asset.text;SourceHash=fingerprint;byId=prepared;
        }
        public static void Validate(PonyHeadStyleList source,string context)
        {
            if(source?.version!=1 || source.styles==null || source.styles.Length==0)
                throw new InvalidOperationException($"Некорректный каталог форм головы: source={context}, version={source?.version}.");
            var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var style in source.styles)
            {
                if(style==null || string.IsNullOrWhiteSpace(style.id) || style.id.Trim()!=style.id ||
                    string.IsNullOrWhiteSpace(style.label) || string.IsNullOrWhiteSpace(style.source) || style.sourceNumber<0 || !seen.Add(style.id))
                    throw new InvalidOperationException($"Неизвестная или повторная форма головы: source={context}, id={style?.id}, number={style?.sourceNumber}.");
                bool original=style.id==CharacterAppearance.OriginalHeadStyleId;
                if(original && !string.IsNullOrEmpty(style.texture) || !original && string.IsNullOrWhiteSpace(style.texture))
                    throw new InvalidOperationException($"Неверный графический источник головы: source={context}, head={style.id}, texture={style.texture??"<не указана>"}.");
                if(original && style.bounds==null) continue;
                if(style.bounds?.Length!=4 || Array.Exists(style.bounds,value=>!float.IsFinite(value)) ||
                    style.bounds[0]>=style.bounds[2] || style.bounds[1]>=style.bounds[3])
                    throw new InvalidOperationException($"Неверные локальные границы головы: source={context}, head={style.id}.");
            }
            if(!seen.Contains(CharacterAppearance.OriginalHeadStyleId))
                throw new InvalidOperationException($"Не объявлена исходная форма головы: source={context}, head={CharacterAppearance.OriginalHeadStyleId}.");
        }
        public static bool TryGet(string id,out PonyHeadStyleDefinition style)
        {
            if(!IsLoaded) throw new InvalidOperationException("Каталог форм головы не подготовлен: "+Resource+".");
            style=null;return id!=null && byId.TryGetValue(id,out style);
        }
        public static PonyHeadStyleDefinition Get(string id)=>TryGet(id,out var style)?style:
            throw new InvalidOperationException($"Не найдена форма головы: head={id??"<не указана>"}, source={Resource}.");
        public static void Reset() {byId=null;styles=null;SourceJsonText=null;SourceHash=null;}
    }
}
