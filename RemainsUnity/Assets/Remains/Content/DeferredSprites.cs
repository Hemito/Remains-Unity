using System;
using UnityEngine;

namespace Remains.Content
{
    // Frames named by their Resources paths and loaded asynchronously on request (Preload), so that a catalog of many
    // animated weapons does not load every frame texture with itself. Get returns null until its frame has arrived;
    // the caller draws its resting sprite meanwhile.
    [Serializable] public sealed class DeferredSprites
    {
        [SerializeField,InspectorName("Кадры (пути Resources)")] private string[] resources=Array.Empty<string>();
        [SerializeField,InspectorName("Пикселей на метр кадров")] private float pixelsPerUnit;
        [NonSerialized] private Sprite[] loaded;
        [NonSerialized] private GameAssets.Request[] requests;
        [NonSerialized] private bool warned;
        public int Count=>resources?.Length??0;
        public float PixelsPerUnit=>pixelsPerUnit;
        public string Resource(int index)=>resources[index];
        public void Configure(string[] values,float density)
        {
            resources=(string[])(values??throw new ArgumentNullException(nameof(values))).Clone();pixelsPerUnit=density;
            loaded=null;requests=null;warned=false;
        }
        public void Validate(string owner)
        {
            if(Count==0 || !float.IsFinite(pixelsPerUnit) || pixelsPerUnit<=0) throw new InvalidOperationException($"{owner}: нет отложенных кадров или их масштаба.");
            for(int i=0;i<resources.Length;i++) if(string.IsNullOrWhiteSpace(resources[i])) throw new InvalidOperationException($"{owner}: пустой путь кадра {i}.");
        }
        public void Preload()
        {
            if(requests!=null || Count==0) return;
            loaded=new Sprite[resources.Length];requests=new GameAssets.Request[resources.Length];
            for(int i=0;i<resources.Length;i++) requests[i]=GameAssets.LoadAsync<Sprite>(resources[i]);
        }
        public Sprite Get(int index)
        {
            if(requests==null) return null;
            if(loaded[index]!=null || !requests[index].IsDone) return loaded[index];
            loaded[index]=requests[index].Asset as Sprite;
            if(loaded[index]==null && !warned) {warned=true;Debug.LogWarning($"Нет кадра {resources[index]} (кадр {index}); рисуется кадр покоя.");}
            return loaded[index];
        }
        // Editor checks: the frame itself, synchronously.
        public Sprite LoadNow(int index)=>GameAssets.Load<Sprite>(resources[index]);
    }
}
