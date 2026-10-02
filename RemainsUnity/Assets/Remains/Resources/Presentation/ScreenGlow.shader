Shader "Remains/ScreenGlow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor ("Renderer", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Cull Off ZWrite Off Lighting Off
        Blend One OneMinusSrcColor
        Pass
        {
            CGPROGRAM
            #pragma vertex SpriteVert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "UnitySprites.cginc"
            fixed4 Frag(v2f input) : SV_Target
            {
                fixed4 color=SampleSpriteTexture(input.texcoord)*input.color;
                color.rgb*=color.a;
                return color;
            }
            ENDCG
        }
    }
}
