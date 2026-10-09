Shader "Remains/WorldSprite"
{
    // World sprites (ladders, stair sprites; later actors and items) lit like the terrain:
    // ambient (URP blend style 0) plus lamp light read at the pixel's world position from the
    // radiance field, or URP "Lamps" lights without it (RemainsLight2D.hlsl, one implementation).
    Properties
    {
        [PerRendererData] _MainTex ("Спрайт", 2D) = "white" {}
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
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
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
                half4 main=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv)*i.color;
                if(main.a<=0) discard;
                half4 lamp=RemainsLampsAvailable()?RemainsSampleLamp(i.world):0;
                return RemainsCombineLight(main.rgb,main.a,i.lightingUV,lamp);
            }
            ENDHLSL
        }
    }
}
