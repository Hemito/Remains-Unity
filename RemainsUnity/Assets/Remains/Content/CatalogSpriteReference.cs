using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Content
{
    // Source geometry is authored once: changing render resolution must not change gameplay geometry.
    [Serializable] public sealed class CatalogSpriteReference
    {
        [SerializeField,InspectorName("Ключ графики каталога")] private string key;
        [SerializeField,InspectorName("Прямой спрайт вне каталога")] private Sprite direct;
        [SerializeField] private Rect rect;
        [SerializeField] private Vector2 pivot;
        [SerializeField] private float pixelsPerUnit;
        [SerializeField] private Bounds bounds;
        [SerializeField,InspectorName("Причина сохранения растра")] private string rasterReason;
        [SerializeField] private bool reframe;
        [SerializeField] private Vector2 sourceCanvas;
        [NonSerialized] private Sprite resolved;
        private static readonly HashSet<string> reported=new(StringComparer.Ordinal);
        public string Key=>key;
        public bool IsConfigured=>!string.IsNullOrEmpty(key) || direct!=null;
        public Rect Rect=>rect;
        public Vector2 Pivot=>pivot;
        public float PixelsPerUnit=>pixelsPerUnit;
        public Bounds Bounds=>bounds;
        public string RasterReason=>rasterReason;
        public static CatalogSpriteReference FromSprite(Sprite sprite,string key=null,string rasterReason=null,bool reframe=false)
        {
            if(sprite==null) throw new ArgumentNullException(nameof(sprite));
            var result=new CatalogSpriteReference {key=key,direct=string.IsNullOrEmpty(key)?sprite:null,
                rect=sprite.rect,pivot=sprite.pivot,pixelsPerUnit=sprite.pixelsPerUnit,bounds=sprite.bounds,rasterReason=rasterReason,reframe=reframe,
                sourceCanvas=new Vector2(sprite.texture.width,sprite.texture.height)};
            result.Validate(sprite.name);return result;
        }
        public void Validate(string context)
        {
            bool keyed=!string.IsNullOrEmpty(key);
            if(keyed && (!key.StartsWith("Imported/Art/",StringComparison.Ordinal) || direct!=null) || !keyed && direct==null ||
                !float.IsFinite(pixelsPerUnit) || pixelsPerUnit<=0 || rect.width<=0 || rect.height<=0 ||
                !float.IsFinite(rect.x) || !float.IsFinite(rect.y) || !float.IsFinite(rect.width) || !float.IsFinite(rect.height) ||
                !float.IsFinite(pivot.x) || !float.IsFinite(pivot.y) || !float.IsFinite(bounds.center.x) || !float.IsFinite(bounds.center.y) ||
                !float.IsFinite(bounds.size.x) || !float.IsFinite(bounds.size.y) || bounds.size.x<=0 || bounds.size.y<=0)
                throw new InvalidOperationException($"Некорректная ссылка на графику: {context}, key={key}, direct={direct?.name}.");
        }
        public Sprite Resolve()
        {
            if(resolved!=null) return resolved;
            Validate(key??direct?.name);
            if(!string.IsNullOrEmpty(key))
            {
                if(Application.isPlaying && !string.IsNullOrEmpty(rasterReason) && reported.Add(key))
                    Debug.LogWarning($"Графика каталога {key} остаётся растровой: {rasterReason}.");
                resolved=GameAssets.Load<Sprite>(key);
                if(resolved==null) throw new InvalidOperationException("Не найдена графика каталога: "+key);
                if(reframe)
                {
                    // Centered traces used a cropped Sprite asset over the catalog texture. Keep that geometry,
                    // but use the actual resolution of the shared keyed image (padding is only right/bottom).
                    var canvas=resolved.rect;float density=resolved.pixelsPerUnit;
                    float ratio=density/pixelsPerUnit;
                    var requested=new Rect(canvas.x+rect.x*ratio,canvas.yMax-(sourceCanvas.y-rect.y)*ratio,rect.width*ratio,rect.height*ratio);
                    var clipped=Rect.MinMaxRect(Mathf.Max(canvas.xMin,requested.xMin),Mathf.Max(canvas.yMin,requested.yMin),
                        Mathf.Min(canvas.xMax,requested.xMax),Mathf.Min(canvas.yMax,requested.yMax));
                    if(clipped.width<=0 || clipped.height<=0) throw new InvalidOperationException("Кадр потребителя вне изображения: "+key);
                    var actualPivot=pivot*ratio+requested.position-clipped.position;
                    resolved=Sprite.Create(resolved.texture,clipped,
                        new Vector2(actualPivot.x/clipped.width,actualPivot.y/clipped.height),density,0,SpriteMeshType.FullRect);
                    resolved.name=key+" (геометрия потребителя)";
                }
            }
            else resolved=direct;
            return resolved;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetReports()=>reported.Clear();
    }
}
