// 奥行きを見るTextMesh用シェーダー。
//
// 既定の "GUI/Text Shader" は ZTest Always で、手前に何があっても文字が描かれる。
// 地図の駅名にはそれでよいが、実景に置く文字(ホームの駅名標・停車位置目標)が
// 車体や運転台を突き抜けてしまう。
//
// "Sprites/Default" では代用できない。フォントのアトラスはアルファのみの
// テクスチャで、RGBが0のため文字が真っ黒になる(2026-07-27に実測)。
// GUI/Text Shaderと同じく **色は頂点カラー、濃さはテクスチャのアルファ** から取る。
Shader "RailTycoon/TextDepth"
{
    Properties
    {
        _MainTex ("Font Texture", 2D) = "white" {}
        _Color ("Text Color", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" }
        Lighting Off
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            v2f vert (appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = i.color;
                c.a *= tex2D(_MainTex, i.texcoord).a;
                return c;
            }
            ENDCG
        }
    }
}
