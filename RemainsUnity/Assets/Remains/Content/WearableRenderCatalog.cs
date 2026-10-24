using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace Remains.Content
{
    // Startup registration precedes Freeze. Queries never access Resources.
    public static class WearableRenderCatalog
    {
        public const string Resource="Content/wearable-render";
        private static readonly Dictionary<string,WearableRenderAsset> profiles=new(StringComparer.Ordinal);
        private static readonly Dictionary<string,string> sources=new(StringComparer.Ordinal);
        private static bool prepared;
        public static bool IsFrozen {get;private set;}
        public static int MaximumAdditionalParts {get;private set;}
        public static string Fingerprint {get;private set;}
        public static string ContentFingerprint {get {Freeze();return Fingerprint;}}
        public static void Prepare()
        {
            if(prepared) return;
            PonyAnimationData.Load();
            var asset=GameAssets.Load<WearableRenderCatalogAsset>(Resource);
            if(asset==null || asset.Profiles==null) throw new InvalidOperationException("Нет подготовленного каталога рисунков снаряжения: "+Resource+".");
            // Validate the complete native set before publishing any entry.
            var ids=new HashSet<string>(StringComparer.Ordinal);string hash=PonyRenderAsset.SourceHash();
            foreach(var value in asset.Profiles)
            {
                if(value==null) throw new InvalidOperationException("Пустой рисунок снаряжения: "+Resource+".");
                value.Validate(PonyAnimationData.Library,hash,Resource);
                if(!ids.Add(value.VisualId) || profiles.ContainsKey(value.VisualId)) throw new InvalidOperationException("Повтор рисунка снаряжения: "+value.VisualId+".");
            }
            foreach(var value in asset.Profiles) {profiles.Add(value.VisualId,value);sources.Add(value.VisualId,Resource);}
            prepared=true;
        }
        public static void Register(WearableRenderAsset asset,bool replace=false,string source=null)
        {
            if(IsFrozen) throw new InvalidOperationException("Рисунки снаряжения уже используются; зарегистрируйте мод до создания персонажей/портретов.");
            if(asset==null || string.IsNullOrWhiteSpace(source)) throw new ArgumentException("Для регистрации рисунка нужны asset и явный source/mod ID.");
            PonyAnimationData.Load();asset.Validate(PonyAnimationData.Library,PonyRenderAsset.SourceHash(),source);
            if(!replace && profiles.ContainsKey(asset.VisualId)) throw new InvalidOperationException($"Повтор рисунка снаряжения: visual={asset.VisualId}, source={source}.");
            profiles[asset.VisualId]=asset;sources[asset.VisualId]=source;
        }
        public static void Freeze()
        {
            if(IsFrozen) return;
            if(!prepared) throw new InvalidOperationException("Рисунки снаряжения не подготовлены. Вызовите WearableRenderCatalog.Prepare до запросов.");
            var maximumBySlot=new Dictionary<string,int>(StringComparer.Ordinal);
            foreach(var definition in CosmeticCatalog.Current.Definitions)
            {
                if(!profiles.TryGetValue(definition.Visual,out var asset))
                    throw new InvalidOperationException($"Нет зарегистрированного рисунка снаряжения: definition={definition.Id}, slot={definition.Slot}, visual={definition.Visual}. Завершите регистрацию ресурсов модов до Freeze.");
                if(asset.Anatomy!=definition.Anatomy) throw new InvalidOperationException($"Несовместимая графика снаряжения: definition={definition.Id}, visual={definition.Visual}, anatomy={definition.Anatomy}/{asset.Anatomy}.");
                int maximum=0;
                foreach(var clip in asset.Clips) foreach(var frame in clip.frames)
                    for(int hair=1;hair<=PonyAnimationData.Library.hairStyles;hair++) for(int eye=1;eye<=PonyAnimationData.Library.eyeStyles;eye++)
                        maximum=Math.Max(maximum,frame.parts.Count(part=>part.visible && PonyAppearance.Matches(part.hairStyle,part.eyeStyle,part.slot,hair,eye,true)));
                maximumBySlot.TryGetValue(definition.Slot,out int old);maximumBySlot[definition.Slot]=Math.Max(old,maximum);
            }
            MaximumAdditionalParts=maximumBySlot.Values.Sum();
            Fingerprint=Remains.Persistence.RecoverySnapshot.Fingerprint(string.Join("\n",profiles.OrderBy(pair=>pair.Key,StringComparer.Ordinal).Select(pair=>pair.Key+":"+sources[pair.Key]+":"+pair.Value.ContentFingerprint)));
            IsFrozen=true;
        }
        public static bool TryGet(string visual,out WearableRenderAsset asset)
        {if(!IsFrozen) throw new InvalidOperationException("Каталог рисунков снаряжения не заморожен перед запросом.");asset=null;return visual!=null && profiles.TryGetValue(visual,out asset);}
        public static IReadOnlyList<WearableRenderAsset> Assets
        {get {if(!IsFrozen) throw new InvalidOperationException("Каталог рисунков снаряжения не заморожен.");return Array.AsReadOnly(profiles.OrderBy(pair=>pair.Key,StringComparer.Ordinal).Select(pair=>pair.Value).ToArray());}}
    }
}
