Shader "Remains/HeldPropGlow"
{
    Properties
    {
        _MainTex ("Изображение", 2D) = "white" {}
        _Color ("Цвет магии", Color) = (0.1,1,0.15,0.7)
        _SpriteRect ("Область спрайта", Vector) = (0,0,1,1)
        _Radius ("Радиус", Vector) = (.05,.05,0,0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Cull Off ZWrite Off Lighting Off
        Blend One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _SpriteRect,_Radius,_Color,_MainTex_TexelSize;
            struct inputData {float4 vertex:POSITION;float2 uv:TEXCOORD0;};
            struct outputData {float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;};
            outputData vert(inputData v) {outputData o;o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;return o;}
            float alphaAt(float2 uv)
            {
                float2 edge=min(uv,1-uv);
                float2 feather=_MainTex_TexelSize.xy/_SpriteRect.zw;
                float2 coverage=smoothstep(-feather,feather,edge);
                float inside=coverage.x*coverage.y;
                return tex2D(_MainTex,_SpriteRect.xy+saturate(uv)*_SpriteRect.zw).a*inside;
            }
            float4 frag(outputData i):SV_Target
            {
                float glow=0,weight=0;
                for(int y=-4;y<=4;y++) for(int x=-4;x<=4;x++)
                {
                    float w=exp(-float(x*x+y*y)/8);
                    glow+=alphaAt(i.uv+float2(x,y)*_Radius.xy*.25)*w;weight+=w;
                }
                float a=saturate(glow/weight)*_Color.a;
                // Weak inner tint plus a stronger halo, independent of scene darkness.
                a*=lerp(1,.22,alphaAt(i.uv));
                return float4(_Color.rgb*a,a);
            }
            ENDCG
        }
    }
}
