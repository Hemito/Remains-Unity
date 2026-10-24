using System;
using System.Collections.Generic;
using Remains.Domain;
using UnityEngine;

namespace Remains.Content
{
    [Serializable] public sealed class DroppedItemShape
    {
        public string id;
        public Sprite sprite;
        // Opaque pixel-cell bounds relative to the sprite's actual registration, in sprite units.
        public Rect opaqueBounds;
        public float Scale=>DroppedItemTransfer.Width/sprite.bounds.size.x;
        public void Validate()
        {
            if(string.IsNullOrWhiteSpace(id) || sprite==null || !float.IsFinite(sprite.bounds.size.x) || sprite.bounds.size.x<=0 ||
                !float.IsFinite(opaqueBounds.x) || !float.IsFinite(opaqueBounds.y) || !float.IsFinite(opaqueBounds.width) ||
                !float.IsFinite(opaqueBounds.height) || opaqueBounds.width<=0 || opaqueBounds.height<=0)
                throw new InvalidOperationException("Не подготовлена форма брошенного предмета: "+id);
        }
        public Rect WorldBounds(float angle,bool mirrored)
        {
            if(!float.IsFinite(angle)) throw new InvalidOperationException("Некорректный угол брошенного предмета: "+id);
            float radians=angle*Mathf.Deg2Rad,sin=Mathf.Sin(radians),cos=Mathf.Cos(radians),scale=Scale;
            Vector2 low=new(float.PositiveInfinity,float.PositiveInfinity),high=new(float.NegativeInfinity,float.NegativeInfinity);
            for(int i=0;i<4;i++)
            {
                float x=(i%2==0?opaqueBounds.xMin:opaqueBounds.xMax)*scale;
                float y=(i<2?opaqueBounds.yMin:opaqueBounds.yMax)*scale*(mirrored?-1:1);
                var point=new Vector2(x*cos-y*sin,x*sin+y*cos);low=Vector2.Min(low,point);high=Vector2.Max(high,point);
            }
            return Rect.MinMaxRect(low.x,low.y,high.x,high.y);
        }
    }
    public sealed class DroppedItemShapeCatalog : ScriptableObject
    {
        public const string Resource="Content/DroppedItemShapes";
        [SerializeField] private DroppedItemShape[] shapes=Array.Empty<DroppedItemShape>();
        public void Configure(DroppedItemShape[] value) {shapes=value;CreateLookup();}
        public Dictionary<string,DroppedItemShape> CreateLookup()
        {
            var result=new Dictionary<string,DroppedItemShape>(StringComparer.Ordinal);
            foreach(var shape in shapes)
            {
                if(shape==null) throw new InvalidOperationException("Пустая форма брошенного предмета: "+name);
                shape.Validate();if(!result.TryAdd(shape.id,shape)) throw new InvalidOperationException("Повтор формы брошенного предмета: "+shape.id);
            }
            return result;
        }
        private static Dictionary<string,DroppedItemShape> loaded;
        public static void Reset()=>loaded=null;
        public static DroppedItemShape Require(string id)=>TryGet(id,out var shape)?shape:
            throw new InvalidOperationException("Нет подготовленной формы брошенного предмета: definition="+id);
        public static bool TryGet(string id,out DroppedItemShape shape)
        {
            shape=null;if(string.IsNullOrEmpty(id)) return false;
            if(loaded==null)
            {
                var catalog=GameAssets.Load<DroppedItemShapeCatalog>(Resource);
                if(catalog==null) throw new InvalidOperationException("Нет подготовленных форм брошенных предметов: "+Resource);
                loaded=catalog.CreateLookup();
            }
            if(loaded.TryGetValue(id,out shape)) return true;
            if(ExportedContentCatalog.Load().TryItem(id,out var source))
            {
                int grade=id.IndexOf(":grade:",StringComparison.Ordinal);
                string key=source.key+(grade>=0?id.Substring(grade):source.category=="weapon"?":grade:0":"");
                if(loaded.TryGetValue(key,out shape)) return true;
            }
            return false;
        }
    }
}
