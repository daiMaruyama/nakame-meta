using System.IO;
using UnityEditor;
using UnityEngine;

namespace NakameMeta.CityEditor
{
    /// <summary>
    /// Assigns simple URP materials to the (textureless) PLATEAU terrain and road
    /// meshes, which glTFast imports with a flat default-gray material. Select the
    /// imported Terrain or Roads object in the scene and run the matching menu item.
    /// </summary>
    static class CityDresser
    {
        const string MatDir = "Assets/Models/City/Materials";
        const string UrpLit = "Universal Render Pipeline/Lit";

        [MenuItem("Tools/NakameMeta/Assign Ground Material to Selection")]
        static void Ground() =>
            Assign("Ground", new Color(0.21f, 0.23f, 0.19f), 0.10f, 0f);

        [MenuItem("Tools/NakameMeta/Assign Road Material to Selection")]
        static void Road() =>
            Assign("Road", new Color(0.12f, 0.12f, 0.13f), 0.35f, 0f);

        static void Assign(string name, Color baseColor, float smoothness, float metallic)
        {
            var sel = Selection.activeGameObject;
            if (sel == null)
            {
                EditorUtility.DisplayDialog("NakameMeta", "Select the imported Terrain/Roads object in the Hierarchy first.", "OK");
                return;
            }

            var shader = Shader.Find(UrpLit);
            if (shader == null) { EditorUtility.DisplayDialog("NakameMeta", "URP Lit shader not found.", "OK"); return; }

            string path = $"{MatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                if (!Directory.Exists(MatDir)) { Directory.CreateDirectory(MatDir); AssetDatabase.Refresh(); }
                mat = new Material(shader) { name = name };
                mat.SetColor("_BaseColor", baseColor);
                mat.SetFloat("_Smoothness", smoothness);
                mat.SetFloat("_Metallic", metallic);
                AssetDatabase.CreateAsset(mat, path);
            }

            var renderers = sel.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length == 0) { EditorUtility.DisplayDialog("NakameMeta", "No MeshRenderer under " + sel.name, "OK"); return; }

            Undo.RecordObjects(renderers, "Assign City Material");
            foreach (var r in renderers)
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
            }
            Debug.Log($"[NakameMeta] Assigned {name} material to {renderers.Length} renderer(s) on {sel.name}.");
        }
    }
}
