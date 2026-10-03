using System;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class ShotArtworkChecks
    {
        public static void Run()
        {
            ShotArtwork.Prepare();
            foreach(string weapon in new[]{"pistol","npc-shot","energy"})
            {
                Require(ShotArtwork.TryGet(weapon,out var artwork),"Нет графики оружия "+weapon);
                Require(AssetDatabase.Contains(artwork) && AssetDatabase.Contains(artwork.trace),"Графика выстрела создаётся вместо чтения сохранённого ресурса: "+weapon);
                foreach(var frame in artwork.impact) Require(AssetDatabase.Contains(frame),"Кадр попадания не сохранён: "+weapon);
            }
            ShotArtwork.TryGet("pistol",out var source);
            var invalid=UnityEngine.Object.Instantiate(source);
            try
            {
                invalid.weapons=new[]{"pistol","pistol"};Reject(invalid,"Повтор ID оружия принят");
                invalid.weapons=new[]{"pistol"};invalid.impact=new Sprite[]{null};Reject(invalid,"Пустой кадр принят");
                invalid.impact=source.impact;invalid.impactFramesPerSecond=float.NaN;Reject(invalid,"Некорректная частота кадров принята");
            }
            finally {UnityEngine.Object.DestroyImmediate(invalid);}
        }
        private static void Reject(ShotArtwork artwork,string reason)
        {try {artwork.Validate();} catch(InvalidOperationException) {return;} throw new InvalidOperationException(reason);}
        private static void Require(bool value,string reason)
        {if(!value) throw new InvalidOperationException(reason);}
    }
}
