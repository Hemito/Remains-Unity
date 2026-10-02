using System;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    public sealed class HealthBarWarning
    {
        private readonly Sprite normal;
        private readonly Sprite[] frames=new Sprite[17];
        private bool active;
        private float started;
        public HealthBarWarning(Sprite normal)
        {
            this.normal=normal;
            for(int i=0;i<frames.Length;i++)
            {
                string path="Original/Hud/HealthWarning/"+(i+2);
                var texture=Resources.Load<Texture2D>(path);
                if(texture==null) throw new InvalidOperationException("Нет кадра предупреждения здоровья: "+path);
                frames[i]=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f));
            }
        }
        public void Refresh(Image image,float fraction,bool visible,float time)
        {
            bool warning=visible && fraction>0 && fraction<.2f;
            if(warning && !active) started=time;
            active=warning;
            image.sprite=warning?frames[Mathf.FloorToInt(Mathf.Max(0,time-started)*30)%frames.Length]:normal;
        }
    }
}
