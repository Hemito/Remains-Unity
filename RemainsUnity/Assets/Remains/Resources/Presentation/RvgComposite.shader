Shader "Hidden/Remains/RvgComposite"
{
    // RVG rasterizer (RvgRasterizer): a group texture (premultiplied) onto its parent with a Flash blend mode.
    // Fixed blend modes use the GPU blend state; overlay, hardlight and difference read a copy of the backdrop
    // (W3C separable compositing) and replace the covered pixels. Parent masks apply through the stencil.
    Properties
    {
        _Group ("Группа", 2D) = "black" {}
        _Backdrop ("Фон", 2D) = "black" {}
        _Ref ("Ссылка трафарета", Float) = 0
        _ReadMask ("Маска чтения", Float) = 0
        _Comp ("Сравнение", Float) = 8
        _SrcBlend ("Смешивание источника", Float) = 1
        _DstBlend ("Смешивание приёмника", Float) = 10
        _BlendOp ("Операция смешивания", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        Blend [_SrcBlend] [_DstBlend]
        BlendOp [_BlendOp]
        Stencil { Ref [_Ref] ReadMask [_ReadMask] WriteMask 0 Comp [_Comp] Pass Keep Fail Keep ZFail Keep }
        Pass
        {
            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _MODE_FIXED _MODE_OVERLAY _MODE_HARDLIGHT _MODE_DIFFERENCE _MODE_INVERT
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            TEXTURE2D(_Group); TEXTURE2D(_Backdrop); SamplerState rvg_point_clamp_sampler;
            Varyings Vert(Attributes v) { Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS); o.uv=v.uv; return o; }
            half4 Frag(Varyings i) : SV_Target
            {
                float4 src=SAMPLE_TEXTURE2D_LOD(_Group,rvg_point_clamp_sampler,i.uv,0);
            #if defined(_MODE_FIXED)
                return src;
            #elif defined(_MODE_INVERT)
                return float4(src.aaa,src.a);    // with blend OneMinusDstColor/OneMinusSrcAlpha: 1 − backdrop where covered
            #else
                float4 dst=SAMPLE_TEXTURE2D_LOD(_Backdrop,rvg_point_clamp_sampler,i.uv,0);
                float3 cs=src.rgb/max(src.a,1e-5), cb=dst.rgb/max(dst.a,1e-5);
                float3 mixed;
            #if defined(_MODE_DIFFERENCE)
                mixed=abs(cb-cs);
            #else
                // Overlay is hard light with the layers swapped.
            #if defined(_MODE_OVERLAY)
                float3 top=cb, bottom=cs;
            #else
                float3 top=cs, bottom=cb;
            #endif
                mixed=lerp(bottom*2*top,1-(1-bottom)*(2-2*top),step(0.5,top));
            #endif
                // W3C: Cs' = (1 − αb)·Cs + αb·B(Cb, Cs); result = Cs'·αs + Cb·αb·(1 − αs) (premultiplied).
                float3 blended=(1-dst.a)*cs+dst.a*mixed;
                float a=src.a+dst.a*(1-src.a);
                return float4(blended*src.a+dst.rgb*(1-src.a),a);
            #endif
            }
            ENDHLSL
        }
    }
}
