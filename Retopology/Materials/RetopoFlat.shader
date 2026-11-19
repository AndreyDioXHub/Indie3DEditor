Shader "Hidden/Indie3DEditor/RetopoFlat"
{
    Properties
    {
        _FaceColor ("Face", Color) = (0.45, 0.72, 1, 0.45)
        _EdgeColor ("Edge", Color) = (0.82, 0.9, 1, 1)
        _EdgeThickness ("Edge Thickness Px", Float) = 1.5
        _DepthBias ("Depth Bias", Float) = 0.002
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            ZTest LEqual
            Cull Back

            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            fixed4 _FaceColor;
            fixed4 _EdgeColor;
            float _EdgeThickness;
            float _DepthBias;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 bary : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 bary : TEXCOORD0;
            };

            v2f Vert(appdata v)
            {
                v2f o;
                float3 viewPos = UnityObjectToViewPos(v.vertex.xyz);
                bool ortho = UNITY_MATRIX_P[3][3] > 0.5;

                if (ortho)
                    viewPos.z *= (1.0 - _DepthBias);
                else
                    viewPos *= (1.0 - _DepthBias);

                o.pos = mul(UNITY_MATRIX_P, float4(viewPos, 1));
                o.bary = v.bary;
                return o;
            }

            fixed4 Frag(v2f i) : SV_Target
            {
                float3 bary = saturate(i.bary);
                float3 width = max(fwidth(bary), 1e-5);
                float edgePx = min(bary.x / width.x, min(bary.y / width.y, bary.z / width.z));
                float edge = 1.0 - smoothstep(_EdgeThickness - 0.75, _EdgeThickness + 0.75, edgePx);
                return lerp(_FaceColor, _EdgeColor, saturate(edge));
            }
            ENDCG
        }
    }

    FallBack Off
}
