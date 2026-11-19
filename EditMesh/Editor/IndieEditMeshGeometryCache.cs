namespace Indie3DEditor.EditMesh
{
    /// <summary>
    /// Version stamp for caches derived from <see cref="IndieEditableMeshData"/> geometry
    /// (coincident-vertex buckets, front-facing masks). Every mesh refresh bumps it.
    /// </summary>
    public static class IndieEditMeshGeometryCache
    {
        public static int Version { get; private set; } = 1;

        public static void Invalidate()
        {
            unchecked
            {
                Version++;
            }
        }
    }
}
