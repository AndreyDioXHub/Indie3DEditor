using UnityEditor.PackageManager;

namespace Indie3DEditor.EditMesh
{
    internal static class Indie3DEditorPackagePaths
    {
        public static string Material(string relativePath)
        {
            PackageInfo info = PackageInfo.FindForAssembly(typeof(Indie3DEditorPackagePaths).Assembly);
            string root = info != null && !string.IsNullOrEmpty(info.assetPath)
                ? info.assetPath
                : "Assets/Indie 3D Editor";

            return root.TrimEnd('/') + "/" + relativePath.TrimStart('/');
        }
    }
}
