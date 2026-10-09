Shader "Remains/WorldTerrain"
{
    // Terrain layer lit by URP 2D lights: the tile sprite is only a shape mask; the material
    // texture is laid in world space from the room origin (one continuous surface across
    // cells). Light, tint and shadows come from Light2D / ShadowCaster2D (stage L1).
    // Walls (_EdgeWidth > 0): lamp light (blend style 1) fades from the wall's open sides into its
    // body over _EdgeWidth, starting right at the edge; ambient (blend style 0) stays uniform. The
    // edge map comes from RoomShadowShapes.Edges (casters leave that strip unshadowed).
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

            // Edge map texel: r = WallOpen bits, g = wall flag, b = wall top / cell.
            half EdgeLight(float2 world)
            {
                if(_EdgeWidth<=0) return 1;
                float2 c=(world-_EdgeOrigin.xy)/_EdgeCell;
                float2 index=floor(c);
                if(any(index<0) || any(index>=_EdgeTex_TexelSize.zw)) return 1;
                half4 e=SAMPLE_TEXTURE2D_LOD(_EdgeTex,sampler_EdgeTex,(index+0.5)*_EdgeTex_TexelSize.xy,0);
                if(e.g<0.5) return 1;
                uint open=(uint)round(e.r*255);
                float h=e.b*_EdgeCell, w=_EdgeCell;
                float2 p=(c-index)*_EdgeCell;
                float d=1e4;
                if(open&1u)   d=min(d,h-p.y);
                if(open&2u)   d=min(d,w-p.x);
                if(open&4u)   d=min(d,p.y);
                if(open&8u)   d=min(d,p.x);
                if(open&16u)  d=min(d,length(p-float2(0,h)));
                if(open&32u)  d=min(d,length(p-float2(w,h)));
                if(open&64u)  d=min(d,length(p-float2(w,0)));
                if(open&128u) d=min(d,length(p-float2(0,0)));
                half t=saturate(d/_EdgeWidth);
                return (1-t)*(1-t);   // brightest at the very edge, fading over the whole width
            }

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

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"

            half4 Frag(Varyings i) : SV_Target
            {
                half mask=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv).a;
                // Bitmap rows run down from the origin; Unity v runs up: v = (y - top) * density / height.
                float2 texel=(i.world-_MaterialOrigin.xy)*_TexelsPerMeter;
                half4 main=SAMPLE_TEXTURE2D(_MaterialTex,sampler_MaterialTex,texel*_MaterialTex_TexelSize.xy)*i.color;
                main.a*=mask;
                if(main.a<=0) discard;
                // Same sum as URP CombinedShapeLightShared (no mask channels here), lamp style scaled by the edge.
                half4 modulate=0, additive=0;
                #if USE_SHAPE_LIGHT_TYPE_0
                half4 l0=SAMPLE_TEXTURE2D(_ShapeLightTexture0,sampler_ShapeLightTexture0,i.lightingUV);
                modulate+=l0*_ShapeLightBlendFactors0.x; additive+=l0*_ShapeLightBlendFactors0.y;
                #endif
                #if USE_SHAPE_LIGHT_TYPE_1
                half4 l1=SAMPLE_TEXTURE2D(_ShapeLightTexture1,sampler_ShapeLightTexture1,i.lightingUV)*EdgeLight(i.world);
                modulate+=l1*_ShapeLightBlendFactors1.x; additive+=l1*_ShapeLightBlendFactors1.y;
                #endif
                #if !USE_SHAPE_LIGHT_TYPE_0 && !USE_SHAPE_LIGHT_TYPE_1
                modulate=1;
                #endif
                half4 color=_HDREmulationScale*(half4(main.rgb,1)*modulate+additive);
                return half4(max(0,color.rgb),main.a);
            }
            ENDHLSL
        }
    }
}
