Shader "Hidden/Remains/RvgPaint"
{
    // RVG rasterizer (RvgRasterizer): covers the stencil-selected area with a Flash fill (solid, gradients, bitmap),
    // applies the Flash color transform and writes premultiplied color. Values are raw sRGB bytes (linear textures and
    // target), so blending happens in gamma space as in Flash.
    Properties
    {
        _Ref ("Ссылка трафарета", Float) = 1
        _ReadMask ("Маска чтения", Float) = 1
        _WriteMask ("Маска записи", Float) = 1
        _Comp ("Сравнение", Float) = 3
        _Pass ("Операция", Float) = 2
        _SrcBlend ("Смешивание источника", Float) = 1
        _DstBlend ("Смешивание приёмника", Float) = 10
        _BlendOp ("Операция смешивания", Float) = 0
        _Ramp ("Градиент", 2D) = "white" {}
        _Bitmap ("Растр", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        Blend [_SrcBlend] [_DstBlend]
        BlendOp [_BlendOp]
        Stencil { Ref [_Ref] ReadMask [_ReadMask] WriteMask [_WriteMask] Comp [_Comp] Pass [_Pass] Fail Keep ZFail Keep }
        Pass
        {
            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _FILL_SOLID _FILL_LINEAR _FILL_RADIAL _FILL_FOCAL _FILL_BITMAP
            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 pixel : TEXCOORD0; };
            TEXTURE2D(_Ramp); TEXTURE2D(_Bitmap);
            SamplerState rvg_linear_clamp_sampler; SamplerState rvg_point_clamp_sampler;
            float4 _Color, _Mul, _Add, _Inv0, _Inv1, _BitmapSize;
            float _Spread, _Focal, _Repeat, _Smooth;
            Varyings Vert(Attributes v) { Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS); o.pixel=v.positionOS.xy; return o; }
            float2 Inverse(float2 p) { return float2(dot(_Inv0.xy,p)+_Inv0.z, dot(_Inv1.xy,p)+_Inv1.z); }
            float Spread(float t)
            {
                if(_Spread<0.5) return saturate(t);                       // pad
                if(_Spread<1.5) return 1-abs(frac(t*0.5)*2-1);             // reflect
                return frac(t);                                           // repeat
            }
            float4 Ramp(float t) { t=Spread(t); return SAMPLE_TEXTURE2D_LOD(_Ramp,rvg_linear_clamp_sampler,float2((t*255+0.5)/256,0.5),0); }
            float4 Fill(float2 p)
            {
            #if defined(_FILL_SOLID)
                return _Color;
            #elif defined(_FILL_BITMAP)
                float2 q=Inverse(p)/_BitmapSize.xy;            // bitmap pixels (Y down) → 0..1
                if(_Repeat>0.5) q=frac(q);
                float2 uv=float2(q.x,1-q.y);
                // The bitmap is stored premultiplied (as in the SWF) so smoothing never bleeds the color of transparent texels.
                float4 b=_Smooth>0.5?SAMPLE_TEXTURE2D_LOD(_Bitmap,rvg_linear_clamp_sampler,uv,0):SAMPLE_TEXTURE2D_LOD(_Bitmap,rvg_point_clamp_sampler,uv,0);
                return b.a>0?float4(b.rgb/b.a,b.a):0;
            #else
                float2 g=Inverse(p)/16384.0;                   // the Flash gradient square → −1..1
            #if defined(_FILL_LINEAR)
                return Ramp((g.x+1)*0.5);
            #elif defined(_FILL_RADIAL)
                return Ramp(length(g));
            #else
                // Focal: t where g = F + (Q − F)·t for Q on the unit circle and F = (focal, 0).
                float2 f=float2(_Focal,0); float2 d=g-f;
                float dd=dot(d,d); if(dd<1e-12) return Ramp(0);
                float fd=dot(f,d); float disc=fd*fd-dd*(dot(f,f)-1);
                float s=(-fd+sqrt(max(disc,0)))/dd;
                return Ramp(s>1e-6?1/s:1e6);
            #endif
            #endif
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float4 c=Fill(i.pixel);
                c=saturate(c*_Mul+_Add);                        // Flash color transform on straight color
                return half4(c.rgb*c.a,c.a);
            }
            ENDHLSL
        }
    }
}
