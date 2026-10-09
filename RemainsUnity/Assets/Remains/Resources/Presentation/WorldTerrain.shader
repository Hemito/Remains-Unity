Shader "Remains/WorldTerrain"
{
    // Terrain layer: the tile sprite is only a shape mask; the material texture is laid in
    // world space from the room origin, so a wall reads as one continuous surface (as the
    // original's bitmap fill under per-cell masks). Lighting is the shared world light map.
    Properties
    {
        [PerRendererData] _MainTex ("Маска формы", 2D) = "white" {}
        _MaterialTex ("Текстура материала", 2D) = "white" {}
        _TexelsPerMeter ("Пикселей текстуры на метр", Float) = 50
        _MaterialOrigin ("Начало заливки (левый верх комнаты, м)", Vector) = (0,0,0,0)
        _Color ("Tint", Color) = (1,1,1,1)
        [Toggle] _BackgroundLighting ("Освещение фоновой декорации", Float) = 0
        [HideInInspector] _RendererColor ("Renderer", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
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
            #include "UnitySprites.cginc"
            #include "WorldLighting.cginc"
            sampler2D _MaterialTex;
            float4 _MaterialTex_TexelSize;
            float _TexelsPerMeter, _BackgroundLighting;
            float4 _MaterialOrigin;
            struct terrain_v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 world : TEXCOORD1;
                fixed4 color : COLOR;
            };
            terrain_v2f Vert(appdata_t input)
            {
                terrain_v2f output;
                float4 local=UnityFlipSprite(input.vertex,_Flip);
                output.vertex=UnityObjectToClipPos(local);
                output.world=mul(unity_ObjectToWorld,local).xy;
                output.uv=input.texcoord;
                output.color=input.color*_Color*_RendererColor;
                return output;
            }
            fixed4 Frag(terrain_v2f input) : SV_Target
            {
                fixed mask=tex2D(_MainTex,input.uv).a;
                // Bitmap rows run down from the origin; Unity v runs up: v = (y - top) * density / height.
                float2 texel=float2(input.world.x-_MaterialOrigin.x,input.world.y-_MaterialOrigin.y)*_TexelsPerMeter;
                fixed4 color=tex2D(_MaterialTex,texel*_MaterialTex_TexelSize.xy)*input.color;
                color.a*=mask;
                color.rgb*=RemainsWorldLight(RemainsWorldLightUV(input.world),_BackgroundLighting,0)*color.a;
                return color;
            }
            ENDCG
        }
    }
}
