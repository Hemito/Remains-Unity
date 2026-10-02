using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    // Canvas representation of the original standing pose, never a second gameplay actor.
    public sealed class InventoryPortrait
    {
        private readonly GameObject root;
        private readonly List<(PortraitPart graphic,PonyPart source)> parts=new();
        private int? hair;
        public InventoryPortrait(Transform parent,Vector2 position,Vector2 size)
        {
            root=new GameObject("Персонаж в инвентаре",typeof(RectTransform));root.transform.SetParent(parent,false);
            HudElements.Rect(root.GetComponent<RectTransform>(),position,size);
            PonyAnimationData.Load();
            var idle=Array.Find(PonyAnimationData.Library.clips,clip=>clip.id=="idle");
            if(idle==null) throw new InvalidOperationException("Нет исходной позы idle для портрета.");
            var entries=new List<(PonyPart part,Vector3[] points)>();
            Vector2 minimum=new(float.PositiveInfinity,float.PositiveInfinity),maximum=new(float.NegativeInfinity,float.NegativeInfinity);
            foreach(var part in idle.frames[0].parts)
            {
                if(!PonyAppearance.IsVisible(part)) continue;
                var points=PonyAnimationData.Quad(part);
                foreach(var point in points) {minimum=Vector2.Min(minimum,point);maximum=Vector2.Max(maximum,point);}
                entries.Add((part,points));
            }
            if(entries.Count==0) throw new InvalidOperationException("Портрет не содержит видимых частей.");
            foreach(var entry in entries)
            {
                var texture=Resources.Load<Texture2D>("Original/Pony/"+entry.part.texture);
                if(texture==null) throw new InvalidOperationException($"Нет текстуры портрета: {entry.part.texture}, часть={entry.part.path}.");
                var child=new GameObject(entry.part.path,typeof(RectTransform));child.transform.SetParent(root.transform,false);
                var rect=child.GetComponent<RectTransform>();rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
                var graphic=child.AddComponent<PortraitPart>();graphic.Configure(texture,entry.points,minimum,maximum);
                parts.Add((graphic,entry.part));
            }
        }
        public void Refresh(ActorState actor)
        {
            root.SetActive(actor!=null);
            if(actor==null || hair==actor.hair) return;
            hair=actor.hair;
            foreach(var entry in parts)
            {
                var c=entry.source.color;
                entry.graphic.color=new Color(c[0],c[1],c[2],c[3])*PonyAppearance.Tint(entry.source.slot,actor.hair);
            }
        }
    }
    public sealed class PortraitPart : MaskableGraphic
    {
        private Texture texture;
        private Vector3[] points;
        private Vector2 minimum,maximum;
        public override Texture mainTexture=>texture;
        public void Configure(Texture texture,Vector3[] points,Vector2 minimum,Vector2 maximum)
        {this.texture=texture;this.points=points;this.minimum=minimum;this.maximum=maximum;raycastTarget=false;SetAllDirty();}
        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            vertices.Clear();if(points==null) return;
            var rect=rectTransform.rect;var extent=maximum-minimum;
            float scale=Mathf.Min((rect.width-8)/extent.x,(rect.height-8)/extent.y);
            var center=(minimum+maximum)*.5f;
            for(int i=0;i<4;i++) vertices.AddVert(rect.center+((Vector2)points[i]-center)*scale,color,new Vector2(i%2,i/2));
            vertices.AddTriangle(0,2,1);vertices.AddTriangle(2,3,1);
        }
    }
}
