Shader "Remains/WorldTerrain"
{
    // Terrain layer lit by URP 2D lights: the tile sprite is only a shape mask; the material
    // texture is laid in world space from the room origin (one continuous surface across
    // cells). Light, tint and shadows come from Light2D / ShadowCaster2D (stage L1).
    // Walls (_EdgeWidth > 0): lamp light (blend style 1) is read in the room just outside the wall's
    // open sides/inner corners (within one cell) and fades into the body over _EdgeWidth,
    // starting right at the edge; ambient (blend style 0) stays uniform. Edge map: RoomShadowShapes.Edges.
    Properties
    {
        [PerRendererData] _MainTex ("Маска формы", 2D) = "white" {}
        _MaterialTex ("Текстура материала", 2D) = "white" {}
        _TexelsPerMeter ("Пикселей текстуры на метр", Float) = 50
        _MaterialOrigin ("Начало заливки (левый верх комнаты, м)", Vector) = (0,0,0,0)
        _Color ("Tint", Color) = (1,1,1,1)
        [NoScaleOffset] _EdgeTex ("Кромки стен (клетка: открытые стороны/углы, высота)", 2D) = "black" {}
        _EdgeWidth ("Ширина освещённой кромки (м), 0 — без кромки", Float) = 0
        _EdgeCell ("Размер клетки (м)", Float) = 0.8
        _EdgeOrigin ("Левый низ комнаты (м)", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off ZWrite Off ZTest Off

        Pass
        {
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/ShapeLightShared.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                half2 lightingUV : TEXCOORD1;
                float2 world : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/LightingUtility.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_MaterialTex); SAMPLER(sampler_MaterialTex);
            TEXTURE2D(_EdgeTex); SAMPLER(sampler_EdgeTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _MaterialTex_TexelSize;
                float _TexelsPerMeter;
                float4 _MaterialOrigin;
                float4 _EdgeTex_TexelSize;
                float _EdgeWidth;
                float _EdgeCell;
                float4 _EdgeOrigin;
            CBUFFER_END

            #if USE_SHAPE_LIGHT_TYPE_0
            SHAPE_LIGHT(0)
            #endif
            #if USE_SHAPE_LIGHT_TYPE_1
            SHAPE_LIGHT(1)
            #endif
            #if USE_SHAPE_LIGHT_TYPE_2
            SHAPE_LIGHT(2)
            #endif
            #if USE_SHAPE_LIGHT_TYPE_3
            SHAPE_LIGHT(3)
            #endif
            #include "RemainsLight2D.hlsl"

            half EdgeFade(float d)
            {
                half t=saturate(d/_EdgeWidth);
                return 1-t*t*(3-2*t);   // from the very edge, smooth to zero at the full width
            }

            // Lamp light on a pixel. Edge map texel: r = WallOpen bits, g = wall flag, b = wall top / cell.
            // On a wall every open side / inner corner of the cell contributes the lamp light read in the
            // room just outside it, faded by the distance to it; the brightest wins (no seams on convex
            // corners). Non-wall pixels read their own light.
            half4 LampLight(float2 world)
            {
                if(!RemainsLampsAvailable()) return 0;
                if(_EdgeWidth<=0) return RemainsSampleLamp(world);
                float2 c=(world-_EdgeOrigin.xy)/_EdgeCell;
                float2 index=floor(c);
                if(any(index<0) || any(index>=_EdgeTex_TexelSize.zw)) return RemainsSampleLamp(world);
                half4 e=SAMPLE_TEXTURE2D_LOD(_EdgeTex,sampler_EdgeTex,(index+0.5)*_EdgeTex_TexelSize.xy,0);
                if(e.g<0.5) return RemainsSampleLamp(world);
                uint open=(uint)round(e.r*255);
                // Outside step: clear of the wall in the light grid too (radiance texels are opaque inside walls).
                float h=e.b*_EdgeCell, w=_EdgeCell, step=max(_EdgeCell*0.06,_RemainsRadianceEnabled>0.5?_RemainsRadianceTexel*1.0:0);
                float2 cellOrigin=_EdgeOrigin.xy+index*_EdgeCell;
                float2 p=c*_EdgeCell-index*_EdgeCell;
                half4 light=0;
                #define REMAINS_EDGE(bit,dist,point,dir) if((open&bit)!=0 && dist<_EdgeWidth) light=max(light,RemainsSampleLamp(cellOrigin+point+dir*step)*EdgeFade(dist));
                REMAINS_EDGE(1u,h-p.y,float2(p.x,h),float2(0,1))
                REMAINS_EDGE(2u,w-p.x,float2(w,p.y),float2(1,0))
                REMAINS_EDGE(4u,p.y,float2(p.x,0),float2(0,-1))
                REMAINS_EDGE(8u,p.x,float2(0,p.y),float2(-1,0))
                REMAINS_EDGE(16u,length(p-float2(0,h)),float2(0,h),float2(-0.7071,0.7071))
                REMAINS_EDGE(32u,length(p-float2(w,h)),float2(w,h),float2(0.7071,0.7071))
                REMAINS_EDGE(64u,length(p-float2(w,0)),float2(w,0),float2(0.7071,-0.7071))
                REMAINS_EDGE(128u,length(p-float2(0,0)),float2(0,0),float2(-0.7071,-0.7071))
                #undef REMAINS_EDGE
                return light;
            }

            Varyings Vert(Attributes v)
            {
                Varyings o=(Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS=TransformObjectToHClip(v.positionOS);
                o.world=TransformObjectToWorld(v.positionOS).xy;
                o.uv=v.uv;
                o.lightingUV=half2(ComputeScreenPos(o.positionCS/o.positionCS.w).xy);
                o.color=v.color*_Color;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half mask=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv).a;
                // Bitmap rows run down from the origin; Unity v runs up: v = (y - top) * density / height.
                float2 texel=(i.world-_MaterialOrigin.xy)*_TexelsPerMeter;
                half4 main=SAMPLE_TEXTURE2D(_MaterialTex,sampler_MaterialTex,texel*_MaterialTex_TexelSize.xy)*i.color;
                main.a*=mask;
                if(main.a<=0) discard;
                return RemainsCombineLight(main.rgb,main.a,i.lightingUV,LampLight(i.world));
            }
            ENDHLSL
        }
    }
}
