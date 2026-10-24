using System;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using Remains.Content;

namespace Remains.Editor
{
    public static class LaserImpactAuthoring
    {
        public static void Run()
        {
            const string folder="Assets/Remains/Resources/Presentation/Effects";
            const string path=folder+"/laser-impact.prefab";
            if(AssetDatabase.LoadMainAssetAtPath(path)!=null) throw new InvalidOperationException("Префаб вспышки уже существует; миграция отменена.");
            System.IO.Directory.CreateDirectory(folder);AssetDatabase.Refresh();
            var frames=new Sprite[3];
            for(int i=0;i<frames.Length;i++)
            {
                var texture=GameAssets.Load<Texture2D>("Original/Shots/laser-impact-"+(i+1));
                if(texture==null) throw new InvalidOperationException("Нет исходного кадра вспышки: "+(i+1));
                frames[i]=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),100);
                frames[i].name="Кадр вспышки "+(i+1);
                AssetDatabase.CreateAsset(frames[i],folder+"/laser-impact-"+(i+1)+".asset");
            }
            var shader=GameAssets.Load<Shader>("Presentation/ScreenGlow");
            if(shader==null) throw new InvalidOperationException("Нет шейдера вспышки лазера.");
            var material=new Material(shader);AssetDatabase.CreateAsset(material,folder+"/laser-impact.mat");
            var root=new GameObject("Попадание лазера");
            try
            {
                var renderer=root.AddComponent<SpriteRenderer>();renderer.sprite=frames[0];renderer.sharedMaterial=material;
                renderer.sortingOrder=ActorVisualLayers.EffectOrder;
                var view=root.AddComponent<LaserImpactView>();view.Artwork=renderer;view.Frames=frames;view.FramesPerSecond=30;view.Validate();
                PrefabUtility.SaveAsPrefabAsset(root,path);AssetDatabase.SaveAssets();
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
