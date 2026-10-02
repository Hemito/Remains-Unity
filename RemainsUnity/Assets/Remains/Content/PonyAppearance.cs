using System;
using UnityEngine;

namespace Remains.Content
{
    public static class PonyAppearance
    {
        public static Color Tint(string slot,int hair)=>slot switch
        {
            "hair"=>hair%2==0?new Color(133/255f,70/255f,9/255f):new Color(.15f,.5f,.85f),
            "fur"=>new Color(163/255f,163/255f,163/255f),
            "eye"=>new Color(22/255f,243/255f,67/255f),
            "magic"=>Color.green,
            "none" or "hairSecondary"=>Color.white,
            _=>throw new InvalidOperationException($"Неизвестная цветовая область пони: {slot}.")
        };
        // The prototype wears one device on the near foreleg. pip2 is the spare view.
        public static bool IsVisible(PonyPart part)=>part.slot!="hairSecondary" && !part.path.StartsWith("pip2/");
    }
}
