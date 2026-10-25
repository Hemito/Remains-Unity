using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using Remains.Content;

namespace Remains.Presentation
{
    [CreateAssetMenu(menuName="Remains/Графика выстрела")]
    public sealed class ShotArtwork : ScriptableObject
    {
        [InspectorName("Идентификаторы оружия")] public string[] weapons;
        [SerializeField,FormerlySerializedAs("trace"),InspectorName("След вне каталога")] private Sprite legacyTrace;
        [SerializeField,FormerlySerializedAs("impact"),InspectorName("Кадры попадания вне каталога")] private Sprite[] legacyImpact;
        [SerializeField,InspectorName("След по ключу")] private CatalogSpriteReference traceReference;
        [SerializeField,InspectorName("Кадры попадания по ключам")] private CatalogSpriteReference[] impactReferences;
        [SerializeField,HideInInspector] private bool keyedImpacts;
        [NonSerialized] private Sprite[] resolvedImpact;
        [NonSerialized] private bool materialsReady;
        public Sprite trace
        {
            get=>traceReference!=null && traceReference.IsConfigured?traceReference.Resolve():legacyTrace;
            set {legacyTrace=value;traceReference=null;materialsReady=false;}
        }
        public Sprite[] impact
        {
            get
            {
                if(!keyedImpacts) return legacyImpact;
                if(resolvedImpact==null)
                {
                    resolvedImpact=new Sprite[impactReferences.Length];
                    for(int i=0;i<resolvedImpact.Length;i++) resolvedImpact[i]=impactReferences[i].Resolve();
                }
                return resolvedImpact;
            }
            set {legacyImpact=value;impactReferences=null;keyedImpacts=false;resolvedImpact=null;materialsReady=false;}
        }
        public CatalogSpriteReference TraceReference=>traceReference!=null && traceReference.IsConfigured?traceReference:
            legacyTrace==null?null:CatalogSpriteReference.FromSprite(legacyTrace);
        public CatalogSpriteReference[] ImpactReferences
        {
            get
            {
                if(keyedImpacts) return impactReferences;
                if(legacyImpact==null) return null;
                var result=new CatalogSpriteReference[legacyImpact.Length];
                for(int i=0;i<result.Length;i++) result[i]=legacyImpact[i]==null?null:CatalogSpriteReference.FromSprite(legacyImpact[i]);
                return result;
            }
        }
        public void ConfigureReferences(CatalogSpriteReference trace,CatalogSpriteReference[] impacts)
        {
            if(trace==null) throw new ArgumentNullException(nameof(trace));
            if(impacts==null) throw new ArgumentNullException(nameof(impacts));
            traceReference=trace;impactReferences=(CatalogSpriteReference[])impacts.Clone();keyedImpacts=true;legacyTrace=null;legacyImpact=null;
            resolvedImpact=null;materialsReady=false;Validate();
        }
        [InspectorName("Мгновенный луч")] public bool beam;
        [InspectorName("Скорость визуального следа, м/с")] public float speed;
        [InspectorName("Длина следа, м")] public float length;
        [InspectorName("Затухание луча, с")] public float lifetime;
        [InspectorName("Частота кадров попадания")] public float impactFramesPerSecond=30;
        private static Dictionary<string,ShotArtwork> loaded;
        private static readonly HashSet<string> warned=new();
        public static void Prepare()
        {
            var catalog=new Dictionary<string,ShotArtwork>(StringComparer.Ordinal);
            foreach(var artwork in GameAssets.LoadAll<ShotArtwork>("Presentation/Shots"))
            {
                artwork.Validate();
                foreach(string weapon in artwork.weapons)
                    if(!catalog.TryAdd(weapon,artwork))
                        throw new InvalidOperationException($"Графика выстрела {artwork.name}: повтор оружия {weapon}, уже задан в {catalog[weapon].name}.");
            }
            if(catalog.Count==0) throw new InvalidOperationException("Нет ресурсов графики выстрелов в Presentation/Shots.");
            loaded=catalog;warned.Clear();
        }
        // Called by the explicit launch event, not by frame drawing. Only the selected effect is resolved.
        public static bool TryGet(string weapon,out ShotArtwork artwork)
        {
            if(loaded==null) throw new InvalidOperationException("Графика выстрелов ещё не подготовлена.");
            if(loaded.TryGetValue(weapon,out artwork)) {artwork.PrepareMaterials();return true;}
            if(warned.Add(weapon)) Debug.LogWarning($"Нет графики выстрела для оружия {weapon}; эффект пропущен.");
            return false;
        }
        public void PrepareMaterials()
        {
            if(materialsReady) return;
            SpriteArtworkMaterial.Prepare(trace);
            foreach(var frame in impact) SpriteArtworkMaterial.Prepare(frame);
            materialsReady=true;
        }
        public void Validate()
        {
            void Require(bool value,string reason) {if(!value) throw new InvalidOperationException($"Графика выстрела {name}: {reason}.");}
            Require(weapons!=null && weapons.Length>0,"не задано оружие");
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(string id in weapons) Require(!string.IsNullOrWhiteSpace(id) && ids.Add(id),$"пустой или повторный ID оружия '{id}'");
            var traceData=TraceReference;Require(traceData!=null,"нет следа");traceData.Validate("след "+name);
            Require(traceData.Bounds.size.x>0,"нет следа ненулевой ширины");
            var impacts=ImpactReferences;Require(impacts!=null,"не задан массив кадров попадания");
            for(int i=0;i<impacts.Length;i++)
            {Require(impacts[i]!=null,"пустой кадр попадания "+i);impacts[i].Validate($"попадание {name}, кадр {i}");}
            if(beam) Require(float.IsFinite(lifetime) && lifetime>0,"затухание должно быть положительным");
            else
            {
                Require(float.IsFinite(speed) && speed>0 && float.IsFinite(length) && length>0,"скорость и длина следа должны быть положительными");
                Require(float.IsFinite(impactFramesPerSecond) && impactFramesPerSecond>0,"частота кадров должна быть положительной");
            }
        }
        private void OnValidate()
        {try {Validate();} catch(InvalidOperationException error) {Debug.LogWarning(error.Message,this);}}
    }
}
