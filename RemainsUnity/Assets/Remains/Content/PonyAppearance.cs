using System;
using UnityEngine;

namespace Remains.Content
{
    public static class PonyAppearance
    {
        private static PonyPaletteAsset palette;
        public static void Prepare()=>palette=PonyPaletteAsset.Load();
        public static Color Tint(string slot,int hair)=>slot switch
        {
            "hair"=>(palette!=null?palette:throw new InvalidOperationException("Палитра пони не подготовлена перед отрисовкой.")).Hair(hair),
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
