Shader "Hidden/Remains/LightScene"
{
    // Draws a sprite into the radiance field's scene texture (RoomRadianceField): lamps emit their
    // radiance over their silhouette (alpha > _AlphaCut), objects absorb by alpha × absorption.
    // Max-blended over the wall raster, so walls stay opaque.
    Properties
    {
        _MainTex ("Спрайт", 2D) = "white" {}
        _Emission ("Излучение (rgb)", Color) = (0,0,0,0)
        _Absorption ("Поглощение", Float) = 0
        _AlphaCut ("Порог силуэта лампы", Float) = 0.4
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        BlendOp Max
        Blend One One
        Pass
        {
            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _Emission;
            float _Absorption, _AlphaCut;
            Varyings Vert(Attributes v) { Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS); o.uv=v.uv; return o; }
            float4 Frag(Varyings i) : SV_Target
            {
                float a=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv).a;
                bool lamp=any(_Emission.rgb>0);
                if(lamp) return a>_AlphaCut?float4(_Emission.rgb,1):0;
                return float4(0,0,0,a*_Absorption);
            }
            ENDHLSL
        }
    }
}
