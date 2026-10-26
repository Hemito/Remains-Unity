using System;
using System.Collections.Generic;
using Remains.RoomFormat;
using UnityEngine;

namespace Remains.Presentation
{
    // Light rules shared by the room radiance field and the level light: how strongly sprites emit and absorb.
    public static class LightRules
    {
        // Partial absorption stays below the wall threshold (0.99): an object stays an object (lit through its body,
        // no wall bounce) even when it stops all light across its thickness.
        public const double MaxObjectTexelAbsorption=0.985;

        // The table gives what the whole object stops; per texel so that crossing its thickness (smaller side, in
        // texels) stops that much: 1 - (1 - A)^(1 / thickness).
        public static double ObjectTexelAbsorption(double absorption,double thicknessTexels)
            =>absorption<=0?0:Math.Min(MaxObjectTexelAbsorption,1-Math.Pow(1-Math.Min(absorption,0.9999),1/Math.Max(1,thicknessTexels)));

        // A door of opacity 1 is a wall; a partial one (original Box.door_opac) absorbs per texel across its thickness.
        public static float DoorTexelAbsorption(double opacity,double thicknessTexels)
        {
            if(!(opacity>0 && opacity<=1)) throw new ArgumentOutOfRangeException(nameof(opacity),$"Непрозрачность заслонки {opacity} вне (0,1].");
            return opacity>=1?1:(float)Math.Min(MaxObjectTexelAbsorption,1-Math.Pow(1-opacity,1/Math.Max(1,thicknessTexels)));
        }

        // Flatland: fluence 1 m away ≈ radiance × apparent width; width = diameter of the disk with the silhouette's
        // area, so every lamp sends the table's power whatever its shape.
        public static double EmitterWidth(double silhouetteArea,double texel)=>Math.Max(texel,2*Math.Sqrt(silhouetteArea/Math.PI));
        public static Color LampRadiance(RoomLightingTable table,RoomLamp lamp,double[] tint,double share,double silhouetteArea,double texel)
        {
            double l=share*table.RadiancePower*lamp.Intensity/EmitterWidth(silhouetteArea,texel);
            return new Color((float)(l*lamp.Color[0]*tint[0]),(float)(l*lamp.Color[1]*tint[1]),(float)(l*lamp.Color[2]*tint[2]),1);
        }

        // Windows emit the far background's radiance (table "windows" by the land's fon). An unknown background is
        // reported by the caller; its windows stay dark.
        public static bool TryWindowEmission(RoomLightingTable table,RoomLandCatalog lands,RoomFile room,double[] tint,out Color emission,out string fon)
        {
            var issues=new List<string>();
            fon=lands.Option(room,"fon",issues);
            if(string.IsNullOrEmpty(fon)) fon="fonDefault";
            foreach(string issue in issues) Debug.LogWarning(issue);
            if(!table.WindowRadiance.TryGetValue(fon,out var sky)) {emission=Color.clear;return false;}
            emission=new Color((float)(sky[0]*tint[0]),(float)(sky[1]*tint[1]),(float)(sky[2]*tint[2]),1);
            return true;
        }
    }
}
