using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Presentation
{
    public sealed class ShotArtwork
    {
        public Sprite trace;
        public Sprite[] impact;
        public bool beam;
        public float speed,length,lifetime;
        private static Dictionary<string,ShotArtwork> loaded;
        private static readonly HashSet<string> warned=new();
        public static void Prepare()
        {
            var bullet=new ShotArtwork {trace=Load("tracer",new Rect(2,8,92,3)),
                impact=new[]{Load("impact-1",new Rect(45,0,47,22),true),Load("impact-2",new Rect(64,5,25,10),true)},speed=90,length=3};
            var laser=new ShotArtwork {trace=Load("laser",new Rect(0,1,100,2)),impact=Array.Empty<Sprite>(),beam=true,lifetime=4f/30};
            loaded=new() {{"pistol",bullet},{"npc-shot",bullet},{"energy",laser}};
        }
        public static bool TryGet(string weapon,out ShotArtwork artwork)
        {
            if(loaded==null) throw new InvalidOperationException("Графика выстрелов ещё не подготовлена.");
            if(loaded.TryGetValue(weapon,out artwork)) return true;
            if(warned.Add(weapon)) Debug.LogWarning($"Нет графики выстрела для оружия {weapon}; эффект пропущен.");
            return false;
        }
        private static Sprite Load(string name,Rect region,bool contactAtTip=false)
        {
            string path="Original/Shots/"+name;var texture=Resources.Load<Texture2D>(path);
            if(texture==null) throw new InvalidOperationException("Нет графики выстрела: "+path);
            var sprite=Sprite.Create(texture,region,new Vector2(contactAtTip?1:.5f,.5f),50);
            SpriteArtworkMaterial.Prepare(sprite);return sprite;
        }
    }
}
