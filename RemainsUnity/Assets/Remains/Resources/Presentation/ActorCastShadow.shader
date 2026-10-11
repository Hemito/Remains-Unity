Shader "Remains/ActorCastShadow"
{
    Properties { _MainTex("Маска",2D)="white"{} _Opacity("Непрозрачность",Float)=1 _BlurUv("Полутень UV",Vector)=(0,0,0,0) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Cull Off ZWrite Off ZTest Always
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        struct Attributes {float3 positionOS:POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;};
        struct Varyings {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;float alpha:TEXCOORD1;};
        TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);
        float _Opacity;float4 _BlurUv;
        Varyings Vert(Attributes v) {Varyings o;o.positionCS=TransformObjectToHClip(v.positionOS);o.uv=v.uv;o.alpha=v.color.a;return o;}
        float4 Mask(Varyings i):SV_Target {return float4(0,0,0,SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv).a*i.alpha*_Opacity);}
        float4 Shadow(Varyings i):SV_Target
        {
            float alpha=0;
            for(int y=-1;y<=1;y++) for(int x=-1;x<=1;x++)
            {
                float weight=(x==0?2:1)*(y==0?2:1);
                alpha+=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv+float2(x,y)*_BlurUv.xy).a*weight;
            }
            return float4(0,0,0,saturate(alpha/16*_Opacity));
        }
        ENDHLSL
        Pass
        {
            Name "SilhouetteMask"
            Tags {"LightMode"="RemainsShadowMask"}
            BlendOp Max Blend One One
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Mask
            ENDHLSL
        }
        Pass
        {
            Name "BackgroundShadow"
            Tags {"LightMode"="SRPDefaultUnlit"}
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Shadow
            ENDHLSL
        }
    }
}
