Shader "Hidden/Remains/RvgFilter"
{
    // RVG rasterizer (RvgRasterizer): Flash bitmap filters on a group texture (premultiplied).
    // Pass 0/1: box blur along X/Y; 2: glow and drop shadow from the blurred source; 3: color matrix.
    Properties { _MainTex ("Группа", 2D) = "black" {} _Blurred ("Размытая группа", 2D) = "black" {} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always Blend Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; };
        struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
        TEXTURE2D(_MainTex); TEXTURE2D(_Blurred); SamplerState rvg_linear_clamp_sampler;
        float4 _TexelSize, _GlowColor, _Offset, _Matrix0, _Matrix1, _Matrix2, _Matrix3, _MatrixOffset;
        float _Radius, _Strength, _Inner, _Knockout, _Composite;
        Varyings Vert(Attributes v) { Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS); o.uv=v.uv; return o; }
        float4 Tap(TEXTURE2D(t), float2 uv) { return SAMPLE_TEXTURE2D_LOD(t,rvg_linear_clamp_sampler,uv,0); }
        float4 Box(float2 uv, float2 axis)
        {
            int r=(int)_Radius; float4 sum=0;
            [loop] for(int k=-r;k<=r;k++) sum+=Tap(_MainTex,uv+axis*k*_TexelSize.xy);
            return sum/(2*r+1);
        }
        ENDHLSL
        Pass { HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target { return Box(i.uv,float2(1,0)); }
        ENDHLSL }
        Pass { HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target { return Box(i.uv,float2(0,1)); }
        ENDHLSL }
        Pass { HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target
            {
                float4 src=Tap(_MainTex,i.uv);
                float blurA=Tap(_Blurred,i.uv-_Offset.xy*_TexelSize.xy).a;
                float a;
                if(_Inner>0.5) a=saturate((1-blurA)*_Strength)*src.a;    // inside the shape, from its edges
                else a=saturate(blurA*_Strength);
                a*=_GlowColor.a;
                float4 glow=float4(_GlowColor.rgb*a,a);
                if(_Inner>0.5)
                {
                    if(_Knockout>0.5) return glow;
                    return _Composite>0.5?glow+src*(1-glow.a):glow;   // inner glow over the source
                }
                if(_Knockout>0.5) return glow*(1-src.a);
                return _Composite>0.5?src+glow*(1-src.a):glow;         // the source over the outer glow
            }
        ENDHLSL }
        Pass { HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target
            {
                float4 c=Tap(_MainTex,i.uv);
                float4 s=float4(c.rgb/max(c.a,1e-5),c.a);
                float4 o=saturate(float4(dot(_Matrix0,s),dot(_Matrix1,s),dot(_Matrix2,s),dot(_Matrix3,s))+_MatrixOffset);
                return float4(o.rgb*o.a,o.a);
            }
        ENDHLSL }
    }
}
