using System;
using System.Collections.Generic;
using UnityEngine;
using Remains.Content;

namespace Remains.Presentation
{
    // Presentation only. The expanded quad lets the halo extend beyond the sprite mesh.
    public sealed class HeldPropGlow : MonoBehaviour
    {
        private readonly Dictionary<Sprite,(Sprite halo,Material material,Bounds bounds)> frames=new();
        private Sprite current;
        private SpriteRenderer halo;
        private MaterialPropertyBlock properties;
        private Color currentColor;
        public bool Visible=>halo!=null && halo.enabled;
        public Sprite CurrentFrame=>current;
        public int PreparedFrameCount=>frames.Count;
        public SpriteRenderer Renderer=>halo;
        public Color CurrentColor=>currentColor;
        public void Initialize(SpriteRenderer artwork,Color color,float border,Sprite[] preparedFrames=null)
        {
            if(!float.IsFinite(border) || border<=0 || !float.IsFinite(color.r) || !float.IsFinite(color.g) || !float.IsFinite(color.b) || !float.IsFinite(color.a))
                throw new InvalidOperationException("Некорректные настройки свечения предмета: "+artwork.name);
            var sprite=artwork.sprite;
            if(sprite==null || sprite.packed) throw new InvalidOperationException("Свечение предмета требует отдельного непакованного спрайта: "+artwork.name);
            var shader=GameAssets.Load<Shader>("Presentation/HeldPropGlow");
            if(shader==null) throw new InvalidOperationException("Нет шейдера Presentation/HeldPropGlow.");
            foreach(var frame in preparedFrames??new[]{sprite})
            {
                if(frame==null || frame.packed) throw new InvalidOperationException("Не готов отдельный кадр свечения: "+artwork.name);
                if(frames.ContainsKey(frame)) continue;
                var bounds=frame.bounds;
                var haloSprite=Sprite.Create(frame.texture,frame.rect,frame.pivot/frame.rect.size,frame.pixelsPerUnit,0,SpriteMeshType.FullRect);
                var material=new Material(shader) {name="Свечение "+artwork.name+"/"+frame.name,mainTexture=frame.texture};
                var rect=frame.textureRect;
                material.SetVector("_SpriteRect",new Vector4(rect.x/frame.texture.width,rect.y/frame.texture.height,rect.width/frame.texture.width,rect.height/frame.texture.height));
                material.SetVector("_Radius",new Vector4(border/bounds.size.x,border/bounds.size.y,0,0));
                material.SetFloat("_Border",border);material.SetColor("_Color",color);
                frames.Add(frame,(haloSprite,material,new Bounds(bounds.center,bounds.size+new Vector3(border*2,border*2,0))));
            }
            halo=gameObject.AddComponent<SpriteRenderer>();
            properties=new MaterialPropertyBlock();
            halo.sortingLayerID=artwork.sortingLayerID;halo.sortingOrder=artwork.sortingOrder+1;SetFrame(sprite);
            SetColor(color);
            halo.enabled=false;
        }
        // All frame materials/geometry are prepared once. A holder/color change
        // changes this renderer's property block, including after SetFrame.
        public void SetColor(Color color)
        {
            if(!float.IsFinite(color.r) || !float.IsFinite(color.g) || !float.IsFinite(color.b) || !float.IsFinite(color.a) ||
                color.r<0 || color.r>1 || color.g<0 || color.g>1 || color.b<0 || color.b>1 || color.a<0 || color.a>1)
                throw new InvalidOperationException("Некорректный цвет магии удерживаемого предмета: "+name);
            if(halo==null || properties==null) throw new InvalidOperationException("Свечение не подготовлено: "+name);
            if(currentColor==color && properties.HasColor("_Color")) return;
            currentColor=color;properties.SetColor("_Color",color);halo.SetPropertyBlock(properties);
        }
        public void SetFrame(Sprite sprite)
        {
            if(current==sprite) return;
            if(sprite==null || !frames.TryGetValue(sprite,out var prepared)) throw new InvalidOperationException("Кадр свечения не подготовлен: "+sprite?.name);
            halo.sprite=prepared.halo;halo.sharedMaterial=prepared.material;halo.localBounds=prepared.bounds;current=sprite;
        }
        public void Draw(bool held) {if(halo.enabled!=held) halo.enabled=held;}
        private void OnDestroy()
        {
            foreach(var frame in frames.Values)
                if(Application.isPlaying) {Destroy(frame.halo);Destroy(frame.material);}
                else {DestroyImmediate(frame.halo);DestroyImmediate(frame.material);}
            frames.Clear();
        }
    }
}
