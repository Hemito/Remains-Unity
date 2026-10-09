Shader "Hidden/Remains/LightScene"
{
    // Draws a sprite into the radiance field's scene texture (RoomRadianceField): lamps emit their
    // radiance over their silhouette (alpha > _AlphaCut), objects absorb by alpha × absorption, screens
    // glow by their bright pixels (luminance above _GlowThreshold) × _Glow, 4×4 samples per light texel.
    // Max-blended over the wall raster, so walls stay opaque.
    Properties
    {
        _MainTex ("Спрайт", 2D) = "white" {}
        _Emission ("Излучение (rgb)", Color) = (0,0,0,0)
        _Absorption ("Поглощение", Float) = 0
        _AlphaCut ("Порог силуэта лампы", Float) = 0.4
        _Glow ("Свечение ярких пикселей", Float) = 0
        _GlowThreshold ("Порог яркости свечения", Float) = 0.5
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
            float _Absorption, _AlphaCut, _Glow, _GlowThreshold;
            Varyings Vert(Attributes v) { Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS); o.uv=v.uv; return o; }
            float4 Frag(Varyings i) : SV_Target
            {
                float a=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv).a;
                bool lamp=any(_Emission.rgb>0);
                if(lamp) return a>_AlphaCut?float4(_Emission.rgb,1):0;
                float3 glow=0;
                if(_Glow>0)
                {
                    // Bright pixels are a few sprite pixels inside one light texel: supersample at full detail.
                    float2 dx=ddx(i.uv), dy=ddy(i.uv);
                    for(int y=0;y<4;y++)
                        for(int x=0;x<4;x++)
                        {
                            float4 c=SAMPLE_TEXTURE2D_LOD(_MainTex,sampler_MainTex,i.uv+dx*((x+0.5)/4-0.5)+dy*((y+0.5)/4-0.5),0);
                            float luminance=dot(c.rgb,float3(0.2126,0.7152,0.0722));
                            glow+=c.rgb*c.a*smoothstep(_GlowThreshold,_GlowThreshold+0.15,luminance);
                        }
                    glow*=_Glow/16;
                }
                return float4(glow,a*_Absorption);
            }
            ENDHLSL
        }
    }
}
