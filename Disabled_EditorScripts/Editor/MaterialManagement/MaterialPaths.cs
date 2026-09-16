using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ithappy.Creative_Characters_FREE.CharacterCustomizationTool.Editor.MaterialManagement
{
    public static class MaterialPaths
    {
        private static readonly string[] MainColorNames = { "Material", "Color" };
        private static readonly string GlassName = "Glass";
        private static readonly string EmissionName = "Emission";

        public static string[] MainColorPaths => GetMaterialPaths(MainColorNames);
        public static string GlassPath => GetMaterialPath(GlassName);
        public static string EmissionPath => GetMaterialPath(EmissionName);
        
        private static string GetMaterialPath(string materialName)
        {
            string hardPath = $"{AssetsPath.Folder.Materials}{materialName}.mat";

#if UNITY_EDITOR
            if (AssetDatabase.LoadAssetAtPath<Material>(hardPath) != null)
                return hardPath;
            
            string foundPath = SearchInIthappy(materialName);
            if (!string.IsNullOrEmpty(foundPath))
                return foundPath;
#endif

            return hardPath;
        }

        private static string[] GetMaterialPaths(string[] materialNames)
        {
            return materialNames.Select(GetMaterialPath).ToArray();
        }
        
        private static string SearchInIthappy(string materialName)
        {
#if UNITY_EDITOR
            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { "Assets/ithappy" });

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                if (System.IO.Path.GetFileNameWithoutExtension(path) == materialName && IsInsideMaterialsFolder(path))
                    return path;
            }
#endif
            return null;
        }

        private static bool IsInsideMaterialsFolder(string assetPath)
        {
            string[] folders = assetPath.Replace('\\', '/').Split('/');

            for (int i = 0; i < folders.Length - 1; i++)
            {
                if (folders[i].Equals("Materials", System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}