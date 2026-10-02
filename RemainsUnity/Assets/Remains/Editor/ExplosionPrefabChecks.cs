using System;
using Remains.Content;
using Remains.Presentation;
using UnityEngine;
namespace Remains.Editor
{
    public static class ExplosionPrefabChecks
    {
        public static void Run()
        {
            var prefab=Resources.Load<ExplosionVisual>("Presentation/Explosion");
            if(prefab==null) throw new InvalidOperationException("Нет префаба взрыва.");
            prefab.Validate();
            var source=prefab.GetComponent<SpriteRenderer>();var original=source.sprite;
            var data=Resources.Load<ExplosionAnimationAsset>("Presentation/explosion-animation").CreateArtwork();
            var instance=UnityEngine.Object.Instantiate(prefab);
            try
            {
                instance.Initialize(null,"hub",new Vector2(2,3));
                var renderer=instance.GetComponent<SpriteRenderer>();
                if(instance.GetComponentsInChildren<SpriteRenderer>(true).Length!=1) throw new InvalidOperationException("Взрыв создал лишний renderer.");
                for(int i=0;i<data.frames.Length;i++)
                {
                    if(!instance.Draw((i+.25f)/data.framesPerSecond) || renderer.sprite!=data.frames[i] || renderer.sharedMaterial!=source.sharedMaterial || renderer.sortingOrder!=source.sortingOrder)
                        throw new InvalidOperationException($"Взрыв: неверный кадр/материал/порядок {i}.");
                }
                if(instance.Draw(data.Duration+.01f) || renderer.enabled || source.sprite!=original) throw new InvalidOperationException("Взрыв не завершился или изменил префаб.");
            }
            finally {UnityEngine.Object.DestroyImmediate(instance.gameObject);}
        }
    }
}
