Shader "Hidden/Indie3DEditor/EditUVSceneOverlay"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
        _ViewportPx ("Viewport Px (w, h, pixelsPerPoint)", Vector) = (1, 1, 1, 0)
        _DepthBias ("Depth Bias", Float) = 0.002
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    float4 _ViewportPx;
    float _DepthBias;

    // Pull toward the camera along the view ray: screen position stays,
    // depth wins against the surface the overlay sits on.
    float4 BiasedClip(float3 objectPos)
    {
        float3 viewPos = UnityObjectToViewPos(float4(objectPos, 1));
        bool ortho = UNITY_MATRIX_P[3][3] > 0.5;
        if (ortho)
            viewPos.z *= (1.0 - _DepthBias);
        else
            viewPos *= (1.0 - _DepthBias);
        return mul(UNITY_MATRIX_P, float4(viewPos, 1));
    }

    float2 PxToClip(float2 px, float w)
    {
        return px * 2.0 / max(_ViewportPx.xy, 1.0) * w;
    }
    ENDCG

    SubShader
    {
        Tags { "Queue" = "Overlay" "IgnoreProjector" = "True" "RenderType" = "Transparent" }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        // ------------------------------------------------------------
        // 0: Fill (triangles, COLOR tint). Always on top like the old AA fill.
        // ------------------------------------------------------------
        Pass
        {
            Name "Fill"
            ZTest Always

            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
            };

            v2f Vert(appdata v)
            {
                v2f o;
                o.pos = BiasedClip(v.vertex.xyz);
                o.color = v.color;
                return o;
            }

            fixed4 Frag(v2f i) : SV_Target
            {
                return i.color;
            }
            ENDCG
        }

        // ------------------------------------------------------------
        // 1: Thick lines, expanded in screen space.
        // POSITION = endpoint A, TEXCOORD0.xyz = endpoint B,
        // TEXCOORD1 = (t: 0=A 1=B, side -1/+1, thickness px, 0).
        // ------------------------------------------------------------
        Pass
        {
            Name "Lines"
            ZTest [_ZTest]

            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct appdata
            {
                float4 vertex : POSITION;
                float4 other : TEXCOORD0;
                float4 param : TEXCOORD1;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
            };

            v2f Vert(appdata v)
            {
                v2f o;
                o.color = v.color;

                float4 ca = BiasedClip(v.vertex.xyz);
                float4 cb = BiasedClip(v.other.xyz);
                const float minW = 1e-4;

                if (ca.w < minW && cb.w < minW)
                {
                    o.pos = float4(0, 0, 0, 0);
                    return o;
                }

                if (ca.w < minW)
                    ca = lerp(ca, cb, (minW - ca.w) / (cb.w - ca.w));
                else if (cb.w < minW)
                    cb = lerp(cb, ca, (minW - cb.w) / (ca.w - cb.w));

                float2 sa = ca.xy / ca.w * _ViewportPx.xy * 0.5;
                float2 sb = cb.xy / cb.w * _ViewportPx.xy * 0.5;
                float2 d = sb - sa;
                float len = length(d);
                float2 dir = len > 1e-5 ? d / len : float2(1, 0);
                float2 n = float2(-dir.y, dir.x);

                float halfPx = max(v.param.z * _ViewportPx.z, 1.0) * 0.5;
                float isB = step(0.5, v.param.x);
                float4 c = isB > 0.5 ? cb : ca;
                float2 px = n * v.param.y * halfPx + dir * (isB * 2.0 - 1.0) * halfPx * 0.5;

                c.xy += PxToClip(px, c.w);
                o.pos = c;
                return o;
            }

            fixed4 Frag(v2f i) : SV_Target
            {
                return i.color;
            }
            ENDCG
        }

        // ------------------------------------------------------------
        // 2: Points as screen-space squares.
        // POSITION = center, TEXCOORD1 = (corner x, corner y, size px, 0).
        // ------------------------------------------------------------
        Pass
        {
            Name "Points"
            ZTest [_ZTest]

            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct appdata
            {
                float4 vertex : POSITION;
                float4 param : TEXCOORD1;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
            };

            v2f Vert(appdata v)
            {
                v2f o;
                o.color = v.color;

                float4 c = BiasedClip(v.vertex.xyz);

                if (c.w < 1e-4)
                {
                    o.pos = float4(0, 0, 0, 0);
                    return o;
                }

                c.xy += PxToClip(v.param.xy * v.param.z * _ViewportPx.z * 0.5, c.w);
                o.pos = c;
                return o;
            }

            fixed4 Frag(v2f i) : SV_Target
            {
                return i.color;
            }
            ENDCG
        }
    }

    FallBack Off
}
