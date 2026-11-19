namespace Indie3DEditor.EditMesh
{
    public enum IndieEditMeshVertexSettingsKind
    {
        Extrude,
        Weld,
        Chamfer
    }

    public static class IndieEditMeshVertexParams
    {
        public static float WeldThreshold = 0.001f;
        public static float ExtrudeHeight = 0.1f;
        public static float ExtrudeBaseWidth = 0f;
        public static float ChamferAmount = 0.1f;
        public static bool ChamferOpen;
    }
}
