using UnityEditor;
using UnityEngine;

namespace Companion.EditorTools
{
    // Builds a skybox in Resources/Sky for each Allsky material listed in TimeOfDaySky.Skies, so the app
    // can load just the one it picks. They share the Allsky textures. Re-run after changing the list.
    public static class CompanionSkyboxBuilder
    {
        const string Source = "Assets/Allsky/";
        const string OutDir = "Assets/Companion/Resources/Sky";
        static readonly string[] Faces = { "_FrontTex", "_BackTex", "_LeftTex", "_RightTex", "_UpTex", "_DownTex" };
        static Shader SixSided => Shader.Find("Skybox/6 Sided");

        [MenuItem("Companion/Build Skyboxes")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Companion/Resources"))
                AssetDatabase.CreateFolder("Assets/Companion", "Resources");
            if (!AssetDatabase.IsValidFolder(OutDir))
                AssetDatabase.CreateFolder("Assets/Companion/Resources", "Sky");

            foreach (var sky in TimeOfDaySky.Skies)
            {
                var src = AssetDatabase.LoadAssetAtPath<Material>(Source + sky.source);
                if (src == null)
                {
                    Debug.LogError("Skybox material not found: " + Source + sky.source);
                    continue;
                }
                // Most Allsky skies use Mobile/Skybox, which can't rotate; the built-in 6 Sided shader
                // takes the same six faces plus rotation.
                var mat = new Material(SixSided) { name = sky.name };
                foreach (var face in Faces) mat.SetTexture(face, src.GetTexture(face));
                if (src.shader == SixSided)
                {
                    mat.SetColor("_Tint", src.GetColor("_Tint"));
                    mat.SetFloat("_Exposure", src.GetFloat("_Exposure"));
                }
                mat.SetFloat("_Rotation", sky.rotation);

                var path = OutDir + "/" + sky.name + ".mat";
                var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (existing != null) EditorUtility.CopySerialized(mat, existing);
                else AssetDatabase.CreateAsset(mat, path);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Companion skyboxes built: " + OutDir);
        }
    }
}
