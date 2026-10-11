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
        _SpriteUvRect ("Область спрайта", Vector) = (0,0,1,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        BlendOp Max
        Blend One One
            HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float alpha : TEXCOORD1; };
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _Emission;
            float4 _SpriteUvRect;
            float _Absorption, _AlphaCut, _Glow, _GlowThreshold;
            float _OccluderOpacity;
            Varyings Vert(Attributes v) { Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS); o.uv=_SpriteUvRect.xy+v.uv*_SpriteUvRect.zw; o.alpha=v.color.a; return o; }
            float4 OccluderFrag(Varyings i) : SV_Target
            {
                float alpha=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv).a*i.alpha*_OccluderOpacity;
                return float4(0,0,0,alpha>=0.4?1:0);
            }
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
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
        // Transient emission adds RGB, preserving the static scene's exact absorption alpha.
        Pass
        {
            BlendOp Add
            Blend One One
            ColorMask RGB
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
        // Dynamic silhouettes only absorb; they never enter the lamp bounce history.
        Pass
        {
            ColorMask A
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment OccluderFrag
            ENDHLSL
        }
        // Constant door absorption, already snapped to the light grid by the CPU.
        Pass
        {
            ColorMask A
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment DoorFrag
            float4 DoorFrag(Varyings i) : SV_Target { return float4(0,0,0,_Absorption); }
            ENDHLSL
        }
    }
}
