Shader "Remains/WeaponGripGlow"
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
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Cull Off ZWrite Off Lighting Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex SpriteVert
            #pragma fragment Frag
            #include "UnitySprites.cginc"
            fixed4 Frag(v2f input) : SV_Target
            {
                float2 p=input.texcoord*2-1;
                float radius=length(p);
                float cloud=.8+.2*sin(p.x*11+p.y*7+_Time.y*2)*sin(p.y*9-_Time.y*1.3);
                float alpha=pow(saturate(1-radius),1.5)*cloud;
                return fixed4(input.color.rgb,input.color.a*alpha);
            }
            ENDCG
        }
    }
}
