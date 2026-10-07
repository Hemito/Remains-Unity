Shader "Remains/BackdropMultiply"
{
    Properties
    {
        _MainTex ("Исходный рисунок", 2D) = "white" {}
        _Color ("Оттенок", Color) = (1,1,1,1)
        [HideInInspector] _BackgroundLighting ("Освещение назначения", Float) = 1
        [HideInInspector] _Emission ("Самосвечение", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Cull Off ZWrite Off Lighting Off
        // RGB-only preserves destination coverage. Transparent source is white,
        // so it does not darken the existing (already lit) background a second time.
        Blend DstColor Zero
        ColorMask RGB
        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            fixed4 _Color;
            struct Input {float4 vertex:POSITION;float2 uv:TEXCOORD0;fixed4 color:COLOR;};
            struct Output {float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;fixed4 color:COLOR;};
            Output Vert(Input input)
            {
                Output output;output.vertex=UnityObjectToClipPos(input.vertex);
                output.uv=input.uv;output.color=input.color*_Color;return output;
            }
            fixed4 Frag(Output input):SV_Target
            {
                fixed4 source=tex2D(_MainTex,input.uv)*input.color;
                return fixed4(lerp(fixed3(1,1,1),source.rgb,source.a),1);
            }
            ENDCG
        }
    }
}
