Shader "Hidden/Remains/EraseMask"
{
    // Writes a sprite's alpha into the room erase mask (RoomDecorView); max-blended, no lighting.
    Properties { _MainTex ("Маска стирания", 2D) = "white" {} }
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
            Varyings Vert(Attributes v) { Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS); o.uv=v.uv; return o; }
            half4 Frag(Varyings i) : SV_Target { half a=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv).a; return half4(a,a,a,a); }
            ENDHLSL
        }
    }
}
