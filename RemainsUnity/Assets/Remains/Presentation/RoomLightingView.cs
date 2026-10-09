using System;
using System.Collections.Generic;
using System.Globalization;
using Remains.RoomFormat;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Remains.Presentation
{
    // Room lighting with URP 2D lights (stage L1, preview only): global light from the
    // room/land darkness and color tint, a point light with real-time shadows per lamp decor,
    // and shadow casters generated from the grid geometry (solid cells and beams): scaled unit
    // squares, no Physics2D colliders.
    public static class RoomLightingView
    {
        public static GameObject Build(RoomFile room,RoomDefinitionCatalog definitions,RoomLandCatalog lands,RoomLightingTable table,Vector2 origin,Transform parent=null)
        {
            if(room==null || definitions==null || lands==null || table==null) throw new ArgumentNullException(room==null?nameof(room):definitions==null?nameof(definitions):lands==null?nameof(lands):nameof(table));
            var root=new GameObject("Освещение комнаты: "+room.Template);
            if(parent!=null) root.transform.SetParent(parent,false);
            root.transform.localPosition=origin;
            var issues=new List<string>();

            // Global light: ambient level and tint.
            // Original keys differ: the room says "dark", the land says "darkness".
            double darkness=Number(room.Options.TryGetValue("dark",out string roomDark)?roomDark:lands.Option(room,"darkness",issues),0);
            string colorName=lands.Option(room,"color",issues);
            var tint=Color.white;
            if(!string.IsNullOrEmpty(colorName))
            {
                if(table.Tints.TryGetValue(colorName,out var rgb)) tint=new Color((float)rgb[0],(float)rgb[1],(float)rgb[2]);
                else issues.Add($"{room.Template}: цветовой тон \"{colorName}\" не описан в таблице света — без тона.");
            }
            var global=new GameObject("Общий свет").AddComponent<Light2D>();
            global.transform.SetParent(root.transform,false);
            global.lightType=Light2D.LightType.Global;
            global.intensity=(float)table.Ambient(darkness);
            global.color=tint;

            // Lamps: decor placements with a lamp entry. Lamps are on unless the room switches them off (lon < 0).
            bool lampsOn=Number(lands.Option(room,"lon",issues),0)>=0;
            float cell=(float)room.Cell;
            int lamps=0;
            if(lampsOn)
                foreach(var decor in room.Decor)
                {
                    if(!table.Lamps.TryGetValue(decor.Type,out var lamp)) continue;
                    var definition=definitions.Require(decor.Type,room.Template+" декор");
                    // Placement cell is the decor's top-left; footprint extends right and down.
                    float cx=(decor.X+definition.DecorWidth/2f)*cell;
                    float cy=(decor.Y+1-definition.DecorHeight/2f)*cell+(float)lamp.OffsetY;
                    var light=new GameObject("Лампа "+decor.Type).AddComponent<Light2D>();
                    light.transform.SetParent(root.transform,false);
                    light.transform.localPosition=new Vector3(cx,cy,0);
                    light.lightType=Light2D.LightType.Point;
                    light.pointLightOuterRadius=(float)lamp.Radius;
                    light.pointLightInnerRadius=(float)lamp.Radius*.15f;
                    light.intensity=(float)lamp.Intensity;
                    light.color=new Color((float)lamp.Color[0],(float)lamp.Color[1],(float)lamp.Color[2])*tint;
                    light.shadowsEnabled=true;
                    light.shadowIntensity=1f;
                    light.shadowSoftness=.3f;
                    lamps++;
                }

            // Shadow casters from derived geometry: one box per solid/beam rectangle.
            var geometry=RoomGeometryBuilder.Build(room,definitions);
            var casters=new GameObject("Тени стен");
            casters.transform.SetParent(root.transform,false);
            foreach(var surface in geometry.Surfaces)
            {
                if(surface.Slope!=0) continue;   // stairs are drawn as sprites; their thin treads cast no useful shadow
                var obj=new GameObject("Тень");
                obj.transform.SetParent(casters.transform,false);
                obj.transform.localPosition=new Vector3((float)(surface.X+surface.Width/2),(float)(surface.Y+surface.Height/2),0);
                // Without a Renderer the caster's Awake (runs inside AddComponent) builds a unit square in
                // local space; the scale applied afterwards stretches it to the rectangle. Public API only,
                // works in player builds; no collider / shape provider (that one builds only in the editor loop).
                var caster=obj.AddComponent<ShadowCaster2D>();
                caster.castingOption=ShadowCaster2D.ShadowCastingOptions.CastShadow;
                obj.transform.localScale=new Vector3((float)surface.Width,(float)surface.Height,1);
                if(caster.mesh==null || caster.mesh.vertexCount==0)
                    issues.Add($"{room.Template}: тенеотбрасыватель {surface.X:0.##},{surface.Y:0.##} {surface.Width:0.##}×{surface.Height:0.##} без сетки — тени от него не будет.");
            }
            foreach(string issue in issues) Debug.LogWarning(issue);
            Debug.Log($"Свет {room.Template}: общий {global.intensity:0.##}, тон {(string.IsNullOrEmpty(colorName)?"нет":colorName)}, ламп {lamps}{(lampsOn?"":" (выключены комнатой)")}, теней {casters.transform.childCount}.");
            return root;
        }

        private static double Number(string value,double fallback)
            =>string.IsNullOrEmpty(value)?fallback:double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out double parsed)?parsed:throw new FormatException("Не число: "+value);
    }
}
