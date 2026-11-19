Shader "Hidden/Indie3DEditor/EditMeshProxyGhost"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.55, 0.2, 0.35)
        _DepthBias ("Depth Bias", Float) = 0.001
    }

    SubShader
    {
        Tags { "Queue" = "Overlay" "IgnoreProjector" = "True" "RenderType" = "Transparent" }

        // Only where the region sits behind other geometry; no UV needed.
        Pass
        {
            Name "Occluded"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Greater
            Cull Back

            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _DepthBias;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float shade : TEXCOORD0;
            };

            v2f Vert(appdata v)
            {
                v2f o;

                // Pull toward the camera so the visible surface never passes Greater
                // against its own depth.
                float3 viewPos = UnityObjectToViewPos(v.vertex.xyz);
                bool ortho = UNITY_MATRIX_P[3][3] > 0.5;
                if (ortho)
                    viewPos.z *= (1.0 - _DepthBias);
                else
                    viewPos *= (1.0 - _DepthBias);
                o.pos = mul(UNITY_MATRIX_P, float4(viewPos, 1));

                float3 worldNormal = normalize(UnityObjectToWorldNormal(v.normal));
                float3 viewDir = normalize(WorldSpaceViewDir(v.vertex));
                o.shade = 0.45 + 0.55 * saturate(dot(worldNormal, viewDir));
                return o;
            }

            fixed4 Frag(v2f i) : SV_Target
            {
                return fixed4(_Color.rgb * i.shade, _Color.a);
            }
            ENDCG
        }
    }

    FallBack Off
}
