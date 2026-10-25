Shader "Hidden/Remains/RvgFinal"
{
    // RVG rasterizer (RvgRasterizer): box-averages the premultiplied canvas (Supersample² texels per output pixel) and
    // writes straight alpha for sprite textures.
    Properties { _MainTex ("Холст", 2D) = "black" {} _Supersample ("Суперсэмплинг", Float) = 1 }
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
            TEXTURE2D(_MainTex);
            float _Supersample;
            Varyings Vert(Attributes v) { Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS); o.uv=v.uv; return o; }
            half4 Frag(Varyings i) : SV_Target
            {
                int n=(int)_Supersample; int2 origin=int2(i.positionCS.xy)*n;
                float4 c=0;
                for(int y=0;y<n;y++) for(int x=0;x<n;x++) c+=LOAD_TEXTURE2D(_MainTex,origin+int2(x,y));
                c/=n*n;
                return c.a>1e-5?half4(saturate(c.rgb/c.a),c.a):half4(0,0,0,0);
            }
            ENDHLSL
        }
    }
}
