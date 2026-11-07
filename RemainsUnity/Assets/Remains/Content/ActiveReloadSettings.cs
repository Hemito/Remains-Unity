using System;
using Remains.Domain;
using UnityEngine;

namespace Remains.Content
{
    [Serializable]
    public sealed class ActiveReloadSettings
    {
        public float windowStart,windowWidth;
        public static ActiveReloadSettings Load()
        {
            var asset=GameAssets.Load<TextAsset>("Content/active-reload");
            if(asset==null) throw new InvalidOperationException("Нет настроек активной перезарядки Content/active-reload.");
            var result=JsonUtility.FromJson<ActiveReloadSettings>(asset.text);
            if(result==null) throw new InvalidOperationException("Не прочитаны настройки активной перезарядки.");
            ActiveReloadClock.ValidateWindow(result.windowStart,result.windowWidth);
            return result;
        }
    }
}
