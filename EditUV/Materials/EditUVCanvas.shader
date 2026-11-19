Shader "Hidden/Indie3DEditor/EditUVCanvas"
{
    Properties
    {
        _MainTex ("Preview", 2D) = "white" {}
        _BgColor ("Bg", Color) = (0.12, 0.12, 0.12, 1)
        _CheckerA ("Checker A", Color) = (0.85, 0.85, 0.85, 1)
        _CheckerB ("Checker B", Color) = (0.2, 0.2, 0.2, 1)
        _CheckerDensity ("Density", Float) = 8
        _UseTexture ("Use Texture", Float) = 0
        _OutsideDim ("Outside Dim", Color) = (0, 0, 0, 0.3)
        _GridColor ("Grid", Color) = (0.28, 0.28, 0.28, 1)
        _UnitBorderColor ("Unit Border", Color) = (0.08, 0.2, 0.55, 1)
        _AxisUColor ("Axis U", Color) = (0.9, 0.25, 0.25, 1)
        _AxisVColor ("Axis V", Color) = (0.25, 0.85, 0.35, 1)
        _Pan ("Pan", Vector) = (0.5, 0.5, 0, 0)
        _Zoom ("Zoom", Float) = 1.2
        _CanvasSize ("Canvas Size", Vector) = (512, 512, 0, 0)
        _MouseUv ("Mouse UV", Vector) = (-999, -999, 0, 0)
        _PointSize ("Point Size", Float) = 5
        _HoverRadiusPx ("Hover Radius", Float) = 8
        _HoverBoost ("Hover Boost", Color) = (1, 1, 0.4, 0.35)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        // ------------------------------------------------------------
        // 0: Background (fullscreen pixel quad 0..canvas)
        // ------------------------------------------------------------
        Pass
        {
            Name "Background"
            Cull Off
            ZWrite Off
            ZTest Always
            Blend One Zero

            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _BgColor;
            float4 _CheckerA;
            float4 _CheckerB;
            float _CheckerDensity;
            float _UseTexture;
            float4 _OutsideDim;
            float4 _GridColor;
            float4 _UnitBorderColor;
            float4 _AxisUColor;
            float4 _AxisVColor;
            float4 _Pan;
            float _Zoom;
            float4 _CanvasSize;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 gui : TEXCOORD0;
            };

            float PixelsPerUv()
            {
                float size = min(_CanvasSize.x, _CanvasSize.y);
                return size / max(_Zoom, 0.01);
            }

            float2 GuiToUv(float2 gui)
            {
                float pp = PixelsPerUv();
                float2 center = _CanvasSize.xy * 0.5;
                return float2(
                    _Pan.x + (gui.x - center.x) / pp,
                    _Pan.y - (gui.y - center.y) / pp
                );
            }

            v2f Vert(appdata v)
            {
                v2f o;
                float2 gui = v.vertex.xy;
                o.gui = gui;
                o.pos = UnityObjectToClipPos(float4(gui, 0, 1));
                return o;
            }

            float LineMask(float distPx, float thickness)
            {
                return saturate(1.0 - abs(distPx) / max(thickness, 0.5));
            }

            fixed4 Frag(v2f i) : SV_Target
            {
                float2 uv = GuiToUv(i.gui);
                float pp = PixelsPerUv();

                float4 col = _BgColor;

                if (_UseTexture > 0.5)
                {
                    float4 tex = tex2D(_MainTex, uv);
                    col = float4(tex.rgb, 1);
                }
                else
                {
                    float dens = max(_CheckerDensity, 0.0001);
                    // Integer parity — fmod on negative UV cells is unreliable in HLSL.
                    int2 cell = int2(floor(uv * dens));
                    bool dark = ((cell.x + cell.y) & 1) != 0;
                    col = dark ? _CheckerB : _CheckerA;
                }

                // Dim outside unit square.
                if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1)
                    col.rgb = lerp(col.rgb, _OutsideDim.rgb, _OutsideDim.a);

                // Grid lines at integers outside unit (and faint).
                float2 gridUv = uv;
                float2 gridDist = abs(frac(gridUv + 0.5) - 0.5) * pp;
                float grid = max(
                    LineMask(gridDist.x, 1.0),
                    LineMask(gridDist.y, 1.0)
                );
                // Skip integer lines that are unit border / axes handled below.
                bool nearUnitEdge =
                    abs(uv.x) < 0.002 || abs(uv.x - 1) < 0.002 ||
                    abs(uv.y) < 0.002 || abs(uv.y - 1) < 0.002;
                if (!nearUnitEdge && (uv.x < -0.01 || uv.x > 1.01 || uv.y < -0.01 || uv.y > 1.01))
                    col.rgb = lerp(col.rgb, _GridColor.rgb, grid * _GridColor.a);

                // Subdivision 0.25 inside 0..1
                if (uv.x >= 0 && uv.x <= 1 && uv.y >= 0 && uv.y <= 1)
                {
                    float2 sub = abs(frac(uv * 4.0 + 0.5) - 0.5) * (pp * 0.25);
                    // Only at 0.25/0.5/0.75, not at 0/1 (unit border).
                    float2 t = frac(uv * 4.0);
                    bool isInterior =
                        (abs(t.x) > 0.01 && abs(t.x - 1) > 0.01) ||
                        (abs(t.y) > 0.01 && abs(t.y - 1) > 0.01);
                    float subMask = max(LineMask(sub.x, 0.8), LineMask(sub.y, 0.8));
                    if (isInterior)
                        col.rgb = lerp(col.rgb, _GridColor.rgb, subMask * 0.55);
                }

                // Unit border 0..1
                float unitDist = min(
                    min(abs(uv.x) * pp, abs(uv.x - 1) * pp),
                    min(abs(uv.y) * pp, abs(uv.y - 1) * pp)
                );
                // Only near the square rim (not deep outside).
                float onUnit =
                    (uv.x >= -0.02 && uv.x <= 1.02 && abs(uv.y) * pp < 2.5) ||
                    (uv.x >= -0.02 && uv.x <= 1.02 && abs(uv.y - 1) * pp < 2.5) ||
                    (uv.y >= -0.02 && uv.y <= 1.02 && abs(uv.x) * pp < 2.5) ||
                    (uv.y >= -0.02 && uv.y <= 1.02 && abs(uv.x - 1) * pp < 2.5)
                    ? 1.0 : 0.0;
                float unitMask = LineMask(unitDist, 2.0) * onUnit;
                col.rgb = lerp(col.rgb, _UnitBorderColor.rgb, unitMask);

                // Axes U (y=0) and V (x=0)
                float axisU = LineMask(abs(uv.y) * pp, 1.2);
                float axisV = LineMask(abs(uv.x) * pp, 1.2);
                if (uv.x > -0.1 && uv.x < 1.15)
                    col.rgb = lerp(col.rgb, _AxisUColor.rgb, axisU * 0.9);
                if (uv.y > -0.1 && uv.y < 1.15)
                    col.rgb = lerp(col.rgb, _AxisVColor.rgb, axisV * 0.9);

                return col;
            }
            ENDCG
        }

        // ------------------------------------------------------------
        // 1: Faces (UV in POSITION.xy, color in COLOR)
        // ------------------------------------------------------------
        Pass
        {
            Name "Faces"
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            float4 _Pan;
            float _Zoom;
            float4 _CanvasSize;

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

            float2 UvToGui(float2 uv)
            {
                float size = min(_CanvasSize.x, _CanvasSize.y);
                float pp = size / max(_Zoom, 0.01);
                float2 center = _CanvasSize.xy * 0.5;
                return float2(
                    center.x + (uv.x - _Pan.x) * pp,
                    center.y - (uv.y - _Pan.y) * pp
                );
            }

            v2f Vert(appdata v)
            {
                v2f o;
                float2 gui = UvToGui(v.vertex.xy);
                o.pos = UnityObjectToClipPos(float4(gui, 0, 1));
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
        // 2: Lines (UV endpoints in POSITION.xy, color in COLOR)
        // ------------------------------------------------------------
        Pass
        {
            Name "Lines"
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            float4 _Pan;
            float _Zoom;
            float4 _CanvasSize;

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

            float2 UvToGui(float2 uv)
            {
                float size = min(_CanvasSize.x, _CanvasSize.y);
                float pp = size / max(_Zoom, 0.01);
                float2 center = _CanvasSize.xy * 0.5;
                return float2(
                    center.x + (uv.x - _Pan.x) * pp,
                    center.y - (uv.y - _Pan.y) * pp
                );
            }

            v2f Vert(appdata v)
            {
                v2f o;
                float2 gui = UvToGui(v.vertex.xy);
                o.pos = UnityObjectToClipPos(float4(gui, 0, 1));
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
        // 3: Points as quads
        // POSITION.xy = UV center, TEXCOORD0.xy = corner (-1..1), COLOR = tint
        // ------------------------------------------------------------
        Pass
        {
            Name "Points"
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            float4 _Pan;
            float _Zoom;
            float4 _CanvasSize;
            float4 _MouseUv;
            float _PointSize;
            float _HoverRadiusPx;
            float4 _HoverBoost;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 corner : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
                float2 local : TEXCOORD0;
            };

            float2 UvToGui(float2 uv)
            {
                float size = min(_CanvasSize.x, _CanvasSize.y);
                float pp = size / max(_Zoom, 0.01);
                float2 center = _CanvasSize.xy * 0.5;
                return float2(
                    center.x + (uv.x - _Pan.x) * pp,
                    center.y - (uv.y - _Pan.y) * pp
                );
            }

            v2f Vert(appdata v)
            {
                v2f o;
                float2 gui = UvToGui(v.vertex.xy);
                float halfPx = _PointSize * 0.5;
                gui += v.corner * halfPx;

                float size = min(_CanvasSize.x, _CanvasSize.y);
                float pp = size / max(_Zoom, 0.01);
                float hoverDist = length(UvToGui(_MouseUv.xy) - UvToGui(v.vertex.xy));
                float hover = hoverDist < _HoverRadiusPx ? 1.0 : 0.0;

                o.pos = UnityObjectToClipPos(float4(gui, 0, 1));
                o.color = lerp(v.color, saturate(v.color + _HoverBoost), hover * _HoverBoost.a);
                o.local = v.corner;
                return o;
            }

            fixed4 Frag(v2f i) : SV_Target
            {
                // Soft square (filled).
                float2 a = abs(i.local);
                if (a.x > 1.01 || a.y > 1.01)
                    discard;
                return i.color;
            }
            ENDCG
        }
    }

    FallBack Off
}
