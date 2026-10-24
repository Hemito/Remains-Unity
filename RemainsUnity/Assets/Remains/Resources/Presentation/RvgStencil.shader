Shader "Hidden/Remains/RvgStencil"
{
    // RVG rasterizer (RvgRasterizer): writes only the stencil (fill parity, stroke coverage, mask bits); no color.
    Properties
    {
        _Ref ("Ссылка трафарета", Float) = 0
        _ReadMask ("Маска чтения", Float) = 255
        _WriteMask ("Маска записи", Float) = 255
        _Comp ("Сравнение", Float) = 8
        _Pass ("Операция", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always ColorMask 0
        Stencil { Ref [_Ref] ReadMask [_ReadMask] WriteMask [_WriteMask] Comp [_Comp] Pass [_Pass] Fail Keep ZFail Keep }
        Pass
        {
            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            struct Attributes { float3 positionOS : POSITION; };
            float4 Vert(Attributes v) : SV_POSITION { return TransformObjectToHClip(v.positionOS); }
            half4 Frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
