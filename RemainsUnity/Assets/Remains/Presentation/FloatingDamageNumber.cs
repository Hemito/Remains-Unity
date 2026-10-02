using System.Globalization;
using System.Collections.Generic;
using Remains.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    public sealed class FloatingDamageNumber : MonoBehaviour
    {
        public const float Duration=1.2f;
        [SerializeField,InspectorName("Надпись урона")] private Text label;
        private Vector3 origin;
        private Vector2 size;
        private Color tint;
        public Rect Bounds=>new Rect((Vector2)transform.position-size*.5f,size);
        public void Validate()
        {
            var canvas=GetComponent<Canvas>();
            if(label==null || label.font==null || !label.transform.IsChildOf(transform) || canvas==null || canvas.renderMode!=RenderMode.WorldSpace)
                throw new System.InvalidOperationException($"Число урона {name}: нужны шрифт, надпись и мировой Canvas.");
            if(label.horizontalOverflow!=HorizontalWrapMode.Overflow)
                throw new System.InvalidOperationException($"Число урона {name}: отключите перенос строки для корректного измерения числа.");
            var scale=transform.lossyScale;
            if(!float.IsFinite(scale.x) || !float.IsFinite(scale.y) || scale.x<=0 || scale.y<=0)
                throw new System.InvalidOperationException($"Число урона {name}: масштаб должен быть положительным и конечным.");
        }
        public void Initialize(DamageResult result)
        {
            Validate();origin=new Vector3(result.x,result.y+1.7f,0);tint=label.color;
            label.text=result.amount.ToString("0.##",CultureInfo.InvariantCulture);
            var rect=GetComponent<RectTransform>();
            rect.sizeDelta=new Vector2(label.preferredWidth+12,label.preferredHeight+8);
            size=Vector2.Scale(rect.sizeDelta,new Vector2(transform.lossyScale.x,transform.lossyScale.y));
            Draw(0);
        }
        public void PlaceAbove(IReadOnlyList<Rect> occupied)
        {
            // All labels drift upward at the same speed. Resolve overlap once on arrival.
            for(int pass=0;pass<occupied.Count;pass++)
            {
                bool moved=false;
                foreach(var other in occupied)
                {
                    var padded=new Rect(other.xMin-.04f,other.yMin-.04f,other.width+.08f,other.height+.08f);
                    var candidate=new Rect((Vector2)origin-size*.5f,size);
                    if(!candidate.Overlaps(padded)) continue;
                    origin.y=padded.yMax+size.y*.5f;moved=true;
                }
                if(!moved) break;
            }
            Draw(0);
        }
        public void Draw(float elapsed)
        {
            transform.position=origin+Vector3.up*(elapsed*.55f);
            var color=tint;color.a*=Mathf.Clamp01((Duration-elapsed)/.4f);label.color=color;
        }
    }
}
