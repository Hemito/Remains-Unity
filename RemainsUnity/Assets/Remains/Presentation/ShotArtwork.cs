using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Presentation
{
    [CreateAssetMenu(menuName="Remains/Графика выстрела")]
    public sealed class ShotArtwork : ScriptableObject
    {
        [InspectorName("Идентификаторы оружия")] public string[] weapons;
        [InspectorName("След")]
        public Sprite trace;
        [InspectorName("Кадры попадания")]
        public Sprite[] impact;
        [InspectorName("Мгновенный луч")]
        public bool beam;
        [InspectorName("Скорость визуального следа, м/с")] public float speed;
        [InspectorName("Длина следа, м")] public float length;
        [InspectorName("Затухание луча, с")] public float lifetime;
        [InspectorName("Частота кадров попадания")] public float impactFramesPerSecond=30;
        private static Dictionary<string,ShotArtwork> loaded;
        private static readonly HashSet<string> warned=new();
        public static void Prepare()
        {
            var catalog=new Dictionary<string,ShotArtwork>(StringComparer.Ordinal);
            foreach(var artwork in Resources.LoadAll<ShotArtwork>("Presentation/Shots"))
            {
                artwork.Validate();
                foreach(string weapon in artwork.weapons)
                    if(!catalog.TryAdd(weapon,artwork))
                        throw new InvalidOperationException($"Графика выстрела {artwork.name}: повтор оружия {weapon}, уже задан в {catalog[weapon].name}.");
                SpriteArtworkMaterial.Prepare(artwork.trace);
                foreach(var frame in artwork.impact) SpriteArtworkMaterial.Prepare(frame);
            }
            if(catalog.Count==0) throw new InvalidOperationException("Нет ресурсов графики выстрелов в Presentation/Shots.");
            loaded=catalog;warned.Clear();
        }
        public static bool TryGet(string weapon,out ShotArtwork artwork)
        {
            if(loaded==null) throw new InvalidOperationException("Графика выстрелов ещё не подготовлена.");
            if(loaded.TryGetValue(weapon,out artwork)) return true;
            if(warned.Add(weapon)) Debug.LogWarning($"Нет графики выстрела для оружия {weapon}; эффект пропущен.");
            return false;
        }
        public void Validate()
        {
            void Require(bool value,string reason) {if(!value) throw new InvalidOperationException($"Графика выстрела {name}: {reason}.");}
            Require(weapons!=null && weapons.Length>0,"не задано оружие");
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(string id in weapons) Require(!string.IsNullOrWhiteSpace(id) && ids.Add(id),$"пустой или повторный ID оружия '{id}'");
            Require(trace!=null && trace.bounds.size.x>0,"нет следа ненулевой ширины");
            Require(impact!=null,"не задан массив кадров попадания");
            foreach(var frame in impact) Require(frame!=null,"пустой кадр попадания");
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
