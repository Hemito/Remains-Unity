using System;
using System.Collections.Generic;
using Remains.RoomFormat;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Remains.Presentation
{
    // Room lighting with URP 2D lights (stage L1, preview only): global light from the
    // room/land darkness and color tint, a point light with real-time shadows per lamp decor,
    // and shadow casters from the grid geometry (RoomShadowShapes: walls, beams):
    // scaled unit squares, no Physics2D colliders.
    public static class RoomLightingView
    {
        // Renderer2D blend styles: 0 ambient (global), 1 lamps (multiply). WorldTerrain fades style 1 on wall bodies.
        public const int LampBlendStyle=1;

        // urpLamps: false when RoomRadianceField lights the room (lamps are its emitters, not Light2D).
        public static GameObject Build(RoomFile room,RoomDefinitionCatalog definitions,RoomLandCatalog lands,RoomLightingTable table,Vector2 origin,Transform parent=null,bool urpLamps=true)
        {
            if(room==null || definitions==null || lands==null || table==null) throw new ArgumentNullException(room==null?nameof(room):definitions==null?nameof(definitions):lands==null?nameof(lands):nameof(table));
            var root=new GameObject("Освещение комнаты: "+room.Template);
            if(parent!=null) root.transform.SetParent(parent,false);
            root.transform.localPosition=origin;
            var scene=RoomLightScene.Build(room,definitions,lands,table,table.RadiancePower,table.RadianceLampSize);
            var issues=new List<string>(scene.Issues);

            // Global light: ambient level and tint.
            var tint=new Color((float)scene.Tint[0],(float)scene.Tint[1],(float)scene.Tint[2]);
            var global=new GameObject("Общий свет").AddComponent<Light2D>();
            global.transform.SetParent(root.transform,false);
            global.lightType=Light2D.LightType.Global;
            global.intensity=(float)table.Ambient(scene.Darkness);
            global.color=tint;

            // Lamps: URP point lights with shadows, unless the radiance field lights the room instead.
            int lamps=0;
            if(urpLamps)
                foreach(var source in scene.Lamps)
                {
                    var light=new GameObject("Лампа "+source.Type).AddComponent<Light2D>();
                    light.transform.SetParent(root.transform,false);
                    light.transform.localPosition=new Vector3((float)source.X,(float)source.Y,0);
                    light.lightType=Light2D.LightType.Point;
                    light.blendStyleIndex=LampBlendStyle;
                    light.pointLightOuterRadius=(float)source.Lamp.Radius;
                    light.pointLightInnerRadius=(float)source.Lamp.Radius*.15f;
                    light.intensity=(float)source.Lamp.Intensity;
                    light.color=new Color((float)source.Color[0],(float)source.Color[1],(float)source.Color[2]);
                    light.shadowsEnabled=true;
                    light.shadowIntensity=1f;
                    light.shadowSoftness=(float)table.ShadowSoftness;
                    lamps++;
                }

            // Shadow casters: wall rectangles (lit edge comes from the terrain shader), beams.
            var casters=new GameObject("Тени стен");
            casters.transform.SetParent(root.transform,false);
            foreach(var rect in RoomShadowShapes.Build(RoomGeometryBuilder.Build(room,definitions)))
            {
                var obj=new GameObject("Тень");
                obj.transform.SetParent(casters.transform,false);
                obj.transform.localPosition=new Vector3((float)(rect.X+rect.Width/2),(float)(rect.Y+rect.Height/2),0);
                // Without a Renderer the caster's Awake (runs inside AddComponent) builds a unit square in
                // local space; the scale applied afterwards stretches it to the rectangle. Public API only,
                // works in player builds; no collider / shape provider (that one builds only in the editor loop).
                var caster=obj.AddComponent<ShadowCaster2D>();
                caster.castingOption=rect.SelfShadow?ShadowCaster2D.ShadowCastingOptions.CastAndSelfShadow:ShadowCaster2D.ShadowCastingOptions.CastShadow;
                obj.transform.localScale=new Vector3((float)rect.Width,(float)rect.Height,1);
                if(caster.mesh==null || caster.mesh.vertexCount==0)
                    issues.Add($"{room.Template}: тенеотбрасыватель {rect.X:0.##},{rect.Y:0.##} {rect.Width:0.##}×{rect.Height:0.##} без сетки — тени от него не будет.");
            }
            foreach(string issue in issues) Debug.LogWarning(issue);
            Debug.Log($"Свет {room.Template}: общий {global.intensity:0.##}, тон {(string.IsNullOrEmpty(scene.TintName)?"нет":scene.TintName)}, ламп {(urpLamps?lamps.ToString():scene.Lamps.Count+" (поле излучения)")}{(scene.LampsOn?"":" (выключены комнатой)")}, теней {casters.transform.childCount}.");
            return root;
        }
    }
}
