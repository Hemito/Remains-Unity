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
        private Text label;
        private Vector3 origin;
        private Vector2 size;
        public Rect Bounds=>new Rect((Vector2)transform.position-size*.5f,size);
        public void Initialize(DamageResult result,Font font)
        {
            origin=new Vector3(result.x,result.y+1.7f,0);
            var canvas=gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.sortingOrder=31000;
            var rect=GetComponent<RectTransform>();rect.sizeDelta=new Vector2(180,60);rect.localScale=Vector3.one*.01f;
            var child=new GameObject("Число",typeof(RectTransform));child.transform.SetParent(transform,false);
            var textRect=child.GetComponent<RectTransform>();textRect.anchorMin=Vector2.zero;textRect.anchorMax=Vector2.one;textRect.offsetMin=textRect.offsetMax=Vector2.zero;
            label=child.AddComponent<Text>();label.font=font;label.fontSize=28;label.alignment=TextAnchor.MiddleCenter;label.raycastTarget=false;
            label.text=result.amount.ToString("0.##",CultureInfo.InvariantCulture);
            rect.sizeDelta=new Vector2(label.preferredWidth+12,label.preferredHeight+8);
            size=rect.sizeDelta*.01f;
            var outline=child.AddComponent<Outline>();outline.effectColor=new Color(0,0,0,.85f);outline.effectDistance=new Vector2(1,-1);
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
            label.color=new Color(1,.65f,.05f,Mathf.Clamp01((Duration-elapsed)/.4f));
        }
    }
}
