using UnityEngine;
using UnityEngine.UI;
using Remains.Content;
namespace Remains.Presentation
{
    public sealed class PortraitPart : MaskableGraphic
    {
        [SerializeField] private Texture texture;
        [SerializeField] private Vector3[] points;
        [SerializeField] private Vector2 minimum,maximum;
        [SerializeField] private string slot;
        [SerializeField] private Color baseColor;
        public void SetAppearance(string value,Color tint) {slot=value;baseColor=tint;ApplyAppearance(0);}
        public void ApplyAppearance(int hair)=>color=baseColor*PonyAppearance.Tint(slot,hair);
        public override Texture mainTexture=>texture;
        public void Validate()
        {
            if(texture==null || points==null || points.Length!=4 || !float.IsFinite(minimum.x) || !float.IsFinite(minimum.y) ||
                !float.IsFinite(maximum.x) || !float.IsFinite(maximum.y) || maximum.x<=minimum.x || maximum.y<=minimum.y || string.IsNullOrEmpty(slot))
                throw new System.InvalidOperationException($"Часть портрета {name}: отсутствуют текстура, вершины, границы или цветовая область.");
            foreach(var point in points) if(!float.IsFinite(point.x) || !float.IsFinite(point.y) || !float.IsFinite(point.z))
                throw new System.InvalidOperationException($"Часть портрета {name}: некорректная вершина.");
        }
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

