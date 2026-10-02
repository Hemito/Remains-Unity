using System;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    public sealed class HealthBarWarning
    {
        private readonly Sprite normal;
        private readonly Sprite[] frames;
        private bool active;
        private float started;
        public HealthBarWarning(Sprite normal,Sprite[] frames)
        {
            if(normal==null || frames==null || frames.Length==0 || Array.Exists(frames,frame=>frame==null))
                throw new InvalidOperationException("Нет сохранённых кадров предупреждения здоровья.");
            this.normal=normal;this.frames=(Sprite[])frames.Clone();
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

