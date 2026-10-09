Shader "Remains/WorldLit"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor ("Renderer", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        _Emission ("Self illumination", Range(0,1)) = 0
        [Toggle] _BackgroundLighting ("Освещение фоновой декорации", Float) = 0
        [HideInInspector] _ArtworkVisibility ("Сохранённая маска оформления", 2D) = "white" {}
        [HideInInspector] _ArtworkVisibilityEnabled ("Маска оформления включена", Float) = 0
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
            #include "WorldLighting.cginc"
            float _BackgroundLighting;
            float _Emission;
            sampler2D _ArtworkVisibility;
            float _ArtworkVisibilityEnabled;
            struct lit_appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float2 maskUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct lit_v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 lightUV : TEXCOORD1;
                float2 maskUV : TEXCOORD2;
                fixed4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            lit_v2f Vert(lit_appdata input)
            {
                lit_v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float4 local=UnityFlipSprite(input.vertex,_Flip);
                output.vertex=UnityObjectToClipPos(local);
                float2 world=mul(unity_ObjectToWorld,local).xy;
                output.lightUV=RemainsWorldLightUV(world);
                output.uv=input.texcoord;
                output.maskUV=input.maskUV;
                output.color=input.color*_Color*_RendererColor;
                return output;
            }
            fixed4 Frag(lit_v2f input) : SV_Target
            {
                fixed4 color=SampleSpriteTexture(input.uv)*input.color;
                color.a*=lerp(1,tex2D(_ArtworkVisibility,input.maskUV).r,_ArtworkVisibilityEnabled);
                color.rgb*=RemainsWorldLight(input.lightUV,_BackgroundLighting,_Emission)*color.a;
                return color;
            }
            ENDCG
        }
    }
}
