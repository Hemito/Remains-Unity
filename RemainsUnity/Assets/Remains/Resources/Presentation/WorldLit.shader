Shader "Remains/WorldLit"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor ("Renderer", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        _Emission ("Self illumination", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Cull Off ZWrite Off Lighting Off
        Blend One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "UnitySprites.cginc"
            sampler2D _WorldLightMap;
            float4 _WorldLightBounds;
            float _WorldLightEnabled, _Emission;
            struct lit_v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 lightUV : TEXCOORD1;
                fixed4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            lit_v2f Vert(appdata_t input)
            {
                lit_v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float4 local=UnityFlipSprite(input.vertex,_Flip);
                output.vertex=UnityObjectToClipPos(local);
                float2 world=mul(unity_ObjectToWorld,local).xy;
                output.lightUV=(world-_WorldLightBounds.xy)*_WorldLightBounds.zw;
                output.uv=input.texcoord;
                output.color=input.color*_Color*_RendererColor;
                return output;
            }
            fixed4 Frag(lit_v2f input) : SV_Target
            {
                fixed4 color=SampleSpriteTexture(input.uv)*input.color;
                fixed3 light=lerp(fixed3(1,1,1),tex2D(_WorldLightMap,input.lightUV).rgb,_WorldLightEnabled);
                color.rgb*=lerp(light,fixed3(1,1,1),_Emission)*color.a;
                return color;
            }
            ENDCG
        }
    }
}
