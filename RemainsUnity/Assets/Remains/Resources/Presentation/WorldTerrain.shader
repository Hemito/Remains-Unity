Shader "Remains/WorldTerrain"
{
    // Terrain layer lit by URP 2D lights: the tile sprite is only a shape mask; the material
    // texture is laid in world space from the room origin (one continuous surface across
    // cells). Light, tint and shadows come from Light2D / ShadowCaster2D (stage L1).
    Properties
    {
        [PerRendererData] _MainTex ("Маска формы", 2D) = "white" {}
        _MaterialTex ("Текстура материала", 2D) = "white" {}
        _TexelsPerMeter ("Пикселей текстуры на метр", Float) = 50
        _MaterialOrigin ("Начало заливки (левый верх комнаты, м)", Vector) = (0,0,0,0)
        _Color ("Tint", Color) = (1,1,1,1)
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
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _MaterialTex_TexelSize;
                float _TexelsPerMeter;
                float4 _MaterialOrigin;
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
                SurfaceData2D surface;
                InputData2D input;
                InitializeSurfaceData(main.rgb,main.a,half4(1,1,1,1),surface);
                InitializeInputData(i.uv,i.lightingUV,input);
                return CombinedShapeLightShared(surface,input);
            }
            ENDHLSL
        }
    }
}
