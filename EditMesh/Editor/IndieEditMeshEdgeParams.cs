namespace Indie3DEditor.EditMesh
{
    public enum IndieEditMeshEdgeSettingsKind
    {
        Extrude,
        Weld,
        Chamfer,
        Bridge,
        Connect,
        Ring
    }

    public static class IndieEditMeshEdgeParams
    {
        public static float ExtrudeHeight = 0.1f;
        public static float ExtrudeBaseWidth;
        public static float WeldThreshold = 0.001f;

        /// <summary>Number typed in the Chamfer Amount field.</summary>
        public static float ChamferAmountInput = 0.1f;

        /// <summary>Divides <see cref="ChamferAmountInput"/> before the chamfer runs. 1 keeps the typed value.</summary>
        public static float ChamferAmountDivisor = 1f;

        /// <summary>Chamfer size passed to the operation: input divided by the divisor.</summary>
        public static float ChamferAmount =>
            ChamferAmountDivisor > 0f ? ChamferAmountInput / ChamferAmountDivisor : ChamferAmountInput;

        public static int ChamferSegments = 1;
        // 1 = flat (Max), 0 = full circular fillet.
        public static float ChamferTension = 0f;
        // false = convex (outward), true = concave (inward gutter).
        public static bool ChamferInvert;
        public static bool ChamferOpen;
        public static int BridgeSegments = 1;
        public static int BridgeTwist;
        public static float BridgeTaper;
        public static float BridgeBias;
        public static int ConnectSegments = 1;
        /// <summary>-1 = collapse to center, 0 = even, +1 = max spread.</summary>
        public static float ConnectPinch;
        /// <summary>-1 = one end, 0 = mid, +1 = other end.</summary>
        public static float ConnectSlide;
        /// <summary>
        /// Max angle (degrees) between mother edge and next ring candidate.
        /// </summary>
        public static float RingAngle = 10f;
    }
}
