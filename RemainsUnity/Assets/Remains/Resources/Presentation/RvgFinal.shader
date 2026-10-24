Shader "Hidden/Remains/RvgFinal"
{
    // RVG rasterizer (RvgRasterizer): resolves the premultiplied canvas into straight alpha for sprite textures.
    Properties { _MainTex ("Холст", 2D) = "black" {} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always Blend Off
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
            half4 Frag(Varyings i) : SV_Target
            {
                float4 c=SAMPLE_TEXTURE2D_LOD(_MainTex,sampler_MainTex,i.uv,0);
                return c.a>1e-5?half4(saturate(c.rgb/c.a),c.a):half4(0,0,0,0);
            }
            ENDHLSL
        }
    }
}
