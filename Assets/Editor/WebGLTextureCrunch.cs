using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class WebGLTextureCrunch
{
    [MenuItem("Tools/WebGL/Enable Crunch on All Textures")]
    public static void EnableCrunchOnAll()
    {
        string[] roots =
        {
            "Assets/Sprite",
            "Assets/Model",
            "Assets/VFX",
            "Assets/Shaders"
        };

        var processed = new List<string>();
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;

            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { root });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                var settings = importer.GetPlatformTextureSettings("WebGL");
                settings.overridden = true;
                settings.textureCompression = TextureImporterCompression.Compressed;
                settings.crunchedCompression = true;
                importer.SetPlatformTextureSettings(settings);
                EditorUtility.SetDirty(importer);
                processed.Add(path);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"WebGL Crunch enabled on {processed.Count} textures.\n" + string.Join("\n", processed));
    }
}
