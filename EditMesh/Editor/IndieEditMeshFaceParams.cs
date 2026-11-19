namespace Indie3DEditor.EditMesh
{
    public enum IndieEditMeshFaceSettingsKind
    {
        Extrude,
        Outline,
        Bevel,
        Inset,
        Bridge,
        Ring,
        Loop,
        Row
    }

    public enum IndieEditMeshFaceExtrudeType
    {
        Group = 0,
        LocalNormal = 1,
        ByPolygon = 2
    }

    public static class IndieEditMeshFaceParams
    {
        public static IndieEditMeshFaceExtrudeType ExtrudeType = IndieEditMeshFaceExtrudeType.Group;
        public static float ExtrudeHeight = 0.1f;

        public static float OutlineAmount = 0.05f;

        public static IndieEditMeshFaceExtrudeType BevelType = IndieEditMeshFaceExtrudeType.Group;
        public static float BevelHeight = 0.1f;
        public static float BevelOutline = -0.05f;

        public static IndieEditMeshFaceExtrudeType InsetType = IndieEditMeshFaceExtrudeType.Group;
        public static float InsetAmount = 0.05f;

        public static int BridgeSegments = 1;
        public static int BridgeTwist;
        public static float BridgeTaper;
        public static float BridgeBias;

        public static float RingAngle = 0.1f;

        /// <summary>
        /// How strictly radials/zigzag are dropped by |Δradius| across the belt.
        /// Higher → only near-equal-radius ring lines remain.
        /// </summary>
        public static float LoopAngle = 60f;

        /// <summary>
        /// Max turn (°) between consecutive quads in a Row. Larger bends stop growth.
        /// </summary>
        public static float RowAngle = 60f;
    }
}
