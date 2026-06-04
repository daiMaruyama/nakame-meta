using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using NakameMeta.City;

namespace NakameMeta.CityEditor
{
    /// <summary>
    /// Splits the merged PLATEAU GLB into one GameObject per building using the
    /// triangle ranges in the *.buildings.json sidecar, classifies each building
    /// by geometry (height + footprint), assigns a per-category facade material,
    /// and optionally adds colliders. Everything is generated under a single
    /// "NakameguroCity" root so a rebuild can wipe and recreate it (reversible).
    /// </summary>
    public sealed class CityBuilder : EditorWindow
    {
        const string RootName = "NakameguroCity";
        const string ShaderName = "NakameMeta/ProceduralFacade";
        const string MaterialDir = "Assets/Models/City/Materials";

        // --- inputs ---
        string _glbPath = "Assets/Models/City/NakameguroStation.glb";
        string _jsonPath = "Assets/Models/City/NakameguroStation.buildings.json";

        // --- classification thresholds (metres / m^2) ---
        float _lowMaxHeight = 10f;      // < this  -> LowResidential
        float _midMaxHeight = 25f;      // < this  -> Mid/Zakkyo, else Office
        float _bigFootprint = 500f;     // >= this -> Office/Commercial regardless of height
        float _zakkyoMaxFootprint = 120f; // mid height + small footprint -> Zakkyo

        // --- collider options ---
        enum ColliderMode { None, All, Filtered }
        ColliderMode _colliderMode = ColliderMode.Filtered;
        bool _filterByHeight = true;
        float _colliderMaxHeight = 12f;
        bool _filterByDistance = false;
        Vector2 _colliderCenter = Vector2.zero; // world XZ
        float _colliderRadius = 150f;

        bool _markStatic = true;

        [MenuItem("Tools/NakameMeta/City Builder")]
        static void Open() => GetWindow<CityBuilder>("City Builder");

        void OnGUI()
        {
            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
            _glbPath = EditorGUILayout.TextField("GLB Path", _glbPath);
            _jsonPath = EditorGUILayout.TextField("buildings.json Path", _jsonPath);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Classification (geometry only)", EditorStyles.boldLabel);
            _lowMaxHeight = EditorGUILayout.FloatField("Low < height (m)", _lowMaxHeight);
            _midMaxHeight = EditorGUILayout.FloatField("Office >= height (m)", _midMaxHeight);
            _bigFootprint = EditorGUILayout.FloatField("Office >= footprint (m^2)", _bigFootprint);
            _zakkyoMaxFootprint = EditorGUILayout.FloatField("Zakkyo <= footprint (m^2)", _zakkyoMaxFootprint);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Colliders", EditorStyles.boldLabel);
            _colliderMode = (ColliderMode)EditorGUILayout.EnumPopup("Mode", _colliderMode);
            if (_colliderMode == ColliderMode.Filtered)
            {
                EditorGUI.indentLevel++;
                _filterByHeight = EditorGUILayout.Toggle("Limit by height", _filterByHeight);
                if (_filterByHeight)
                    _colliderMaxHeight = EditorGUILayout.FloatField("  Collider if height <= (m)", _colliderMaxHeight);
                _filterByDistance = EditorGUILayout.Toggle("Limit by distance", _filterByDistance);
                if (_filterByDistance)
                {
                    _colliderCenter = EditorGUILayout.Vector2Field("  Center (world XZ)", _colliderCenter);
                    _colliderRadius = EditorGUILayout.FloatField("  Radius (m)", _colliderRadius);
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
            _markStatic = EditorGUILayout.Toggle("Mark Batching Static", _markStatic);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Build City", GUILayout.Height(32))) Build();
                if (GUILayout.Button("Clear City", GUILayout.Height(32))) Clear();
            }
            EditorGUILayout.HelpBox(
                "Build regenerates the '" + RootName + "' root from scratch (existing one is removed first). " +
                "Original GLB is untouched. Usage-based re-tagging can later look up buildings by BuildingInfo.uid.",
                MessageType.Info);
        }

        // ------------------------------------------------------------------ build

        void Build()
        {
            try
            {
                if (!File.Exists(_glbPath)) { EditorUtility.DisplayDialog("City Builder", "GLB not found:\n" + _glbPath, "OK"); return; }
                if (!File.Exists(_jsonPath)) { EditorUtility.DisplayDialog("City Builder", "buildings.json not found:\n" + _jsonPath, "OK"); return; }

                var shader = Shader.Find(ShaderName);
                if (shader == null) { EditorUtility.DisplayDialog("City Builder", "Shader not found: " + ShaderName + "\nLet Unity finish compiling, then retry.", "OK"); return; }

                EditorUtility.DisplayProgressBar("City Builder", "Reading GLB geometry...", 0f);
                var geo = GlbReader.Read(_glbPath);

                EditorUtility.DisplayProgressBar("City Builder", "Reading building ranges...", 0.05f);
                var buildings = BuildingsJson.ReadRanges(_jsonPath, out int declaredTriangles);

                int meshTriangles = geo.indices.Length / 3;
                if (declaredTriangles != 0 && declaredTriangles != meshTriangles)
                {
                    EditorUtility.ClearProgressBar();
                    EditorUtility.DisplayDialog("City Builder",
                        $"Triangle count mismatch: GLB has {meshTriangles}, sidecar declares {declaredTriangles}. Aborting to avoid mis-split.",
                        "OK");
                    return;
                }

                var materials = GetOrCreateMaterials(shader);

                ClearInternal(); // remove previous root

                var root = new GameObject(RootName);
                Undo.RegisterCreatedObjectUndo(root, "Build City");

                var counts = new int[4];
                int built = 0, total = buildings.Count;
                for (int bi = 0; bi < total; bi++)
                {
                    var b = buildings[bi];
                    if ((bi & 31) == 0)
                        EditorUtility.DisplayProgressBar("City Builder", $"Building {bi}/{total}...", 0.1f + 0.85f * bi / total);

                    if (!TryBuildMesh(geo, b.segments, out Mesh mesh, out Vector3 pivot,
                                      out float height, out float footprint))
                        continue;

                    var cat = Classify(height, footprint);
                    counts[(int)cat]++;

                    var go = new GameObject($"B_{cat}_{ShortUid(b.uid)}");
                    go.transform.SetParent(root.transform, false);
                    go.transform.localPosition = pivot;

                    var mf = go.AddComponent<MeshFilter>();
                    mf.sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = materials[(int)cat];

                    if (ShouldHaveCollider(height, pivot))
                    {
                        var mc = go.AddComponent<MeshCollider>();
                        mc.sharedMesh = mesh;
                    }

                    var info = go.AddComponent<BuildingInfo>();
                    info.uid = b.uid;
                    info.category = cat;
                    info.heightMeters = height;
                    info.footprintArea = footprint;

                    if (_markStatic)
                        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);

                    built++;
                }

                EditorUtility.ClearProgressBar();
                EditorSceneManager.MarkSceneDirty(root.scene);
                Selection.activeGameObject = root;

                Debug.Log($"[NakameMeta] Built {built}/{total} buildings. " +
                          $"Low={counts[0]} Mid={counts[1]} Office={counts[2]} Zakkyo={counts[3]}.");
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogException(e);
                EditorUtility.DisplayDialog("City Builder", "Build failed:\n" + e.Message, "OK");
            }
        }

        void Clear()
        {
            ClearInternal();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        static void ClearInternal()
        {
            // Remove any existing root (search all roots of the active scene).
            var scene = EditorSceneManager.GetActiveScene();
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.name == RootName)
                    Undo.DestroyObjectImmediate(go);
            }
        }

        // ------------------------------------------------------------- classify

        BuildingCategory Classify(float height, float footprint)
        {
            if (height >= _midMaxHeight || footprint >= _bigFootprint)
                return BuildingCategory.OfficeCommercial;
            if (height < _lowMaxHeight)
                return BuildingCategory.LowResidential;
            // mid height band:
            return footprint <= _zakkyoMaxFootprint ? BuildingCategory.Zakkyo : BuildingCategory.MidRise;
        }

        bool ShouldHaveCollider(float height, Vector3 pivot)
        {
            switch (_colliderMode)
            {
                case ColliderMode.None: return false;
                case ColliderMode.All: return true;
                case ColliderMode.Filtered:
                    if (_filterByHeight && height > _colliderMaxHeight) return false;
                    if (_filterByDistance)
                    {
                        var d = new Vector2(pivot.x - _colliderCenter.x, pivot.z - _colliderCenter.y);
                        if (d.magnitude > _colliderRadius) return false;
                    }
                    return true;
                default: return false;
            }
        }

        static string ShortUid(string uid)
        {
            int i = uid.LastIndexOf('_');
            string tail = i >= 0 ? uid.Substring(i + 1) : uid;
            return tail.Length > 8 ? tail.Substring(0, 8) : tail;
        }

        // --------------------------------------------------------- mesh splitting

        static bool TryBuildMesh(GlbReader.Geometry geo, List<(int start, int count)> segments,
                                 out Mesh mesh, out Vector3 pivot, out float height, out float footprint)
        {
            mesh = null; pivot = Vector3.zero; height = 0; footprint = 0;

            var remap = new Dictionary<int, int>();
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var tris = new List<int>();

            foreach (var seg in segments)
            {
                for (int t = seg.start; t < seg.start + seg.count; t++)
                {
                    int baseI = t * 3;
                    if (baseI + 2 >= geo.indices.Length) continue;
                    for (int k = 0; k < 3; k++)
                    {
                        int gi = geo.indices[baseI + k];
                        if (!remap.TryGetValue(gi, out int li))
                        {
                            li = verts.Count;
                            remap[gi] = li;
                            verts.Add(geo.positions[gi]);
                            norms.Add(geo.normals[gi]);
                        }
                        tris.Add(li);
                    }
                }
            }

            if (verts.Count == 0 || tris.Count == 0) return false;

            // bounds in world space (verts are still world-space here)
            Vector3 min = verts[0], max = verts[0];
            for (int i = 1; i < verts.Count; i++)
            {
                min = Vector3.Min(min, verts[i]);
                max = Vector3.Max(max, verts[i]);
            }
            height = max.y - min.y;
            footprint = Mathf.Max(0.01f, (max.x - min.x) * (max.z - min.z));

            // pivot at footprint centre, ground level -> local verts relative to pivot
            pivot = new Vector3((min.x + max.x) * 0.5f, min.y, (min.z + max.z) * 0.5f);
            for (int i = 0; i < verts.Count; i++) verts[i] -= pivot;

            mesh = new Mesh { name = "bldg" };
            mesh.indexFormat = verts.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return true;
        }

        // ------------------------------------------------------------- materials

        static Material[] GetOrCreateMaterials(Shader shader)
        {
            if (!Directory.Exists(MaterialDir))
            {
                Directory.CreateDirectory(MaterialDir);
                AssetDatabase.Refresh();
            }

            var mats = new Material[4];
            mats[(int)BuildingCategory.LowResidential] = MakeMat(shader, "Cat_LowResidential",
                new Color(0.78f, 0.74f, 0.66f), new Vector4(2.2f, 3.0f, 0, 0), 0.40f, 0.22f,
                new Color(1.0f, 0.85f, 0.55f), 2.0f, 0.55f, 0.30f);
            mats[(int)BuildingCategory.MidRise] = MakeMat(shader, "Cat_MidRise",
                new Color(0.62f, 0.62f, 0.64f), new Vector4(2.6f, 3.0f, 0, 0), 0.55f, 0.30f,
                new Color(1.0f, 0.86f, 0.6f), 2.6f, 0.45f, 0.22f);
            mats[(int)BuildingCategory.OfficeCommercial] = MakeMat(shader, "Cat_OfficeCommercial",
                new Color(0.18f, 0.20f, 0.26f), new Vector4(2.0f, 3.6f, 0, 0), 0.70f, 0.45f,
                new Color(0.7f, 0.85f, 1.0f), 3.8f, 0.35f, 0.15f);
            mats[(int)BuildingCategory.Zakkyo] = MakeMat(shader, "Cat_Zakkyo",
                new Color(0.42f, 0.38f, 0.36f), new Vector4(2.4f, 3.2f, 0, 0), 0.50f, 0.40f,
                new Color(1.0f, 0.7f, 0.4f), 3.2f, 0.45f, 0.55f);
            return mats;
        }

        static Material MakeMat(Shader shader, string name, Color wall, Vector4 winSize,
                                float fill, float litProb, Color emis, float emisStr,
                                float glassDark, float regionVar)
        {
            string path = $"{MaterialDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat; // keep user tweaks on rebuild

            mat = new Material(shader) { name = name };
            mat.SetColor("_WallColor", wall);
            mat.SetVector("_WindowSize", winSize);
            mat.SetFloat("_WindowFill", fill);
            mat.SetFloat("_LitProbability", litProb);
            mat.SetColor("_WindowEmission", emis);
            mat.SetFloat("_EmissionStrength", emisStr);
            mat.SetFloat("_GlassDarkness", glassDark);
            mat.SetFloat("_RegionVariation", regionVar);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }

    // ----------------------------------------------------------------- GLB reader

    /// <summary>Minimal binary glTF (.glb) reader for a single POSITION+NORMAL mesh.</summary>
    static class GlbReader
    {
        public struct Geometry
        {
            public Vector3[] positions; // Unity space
            public Vector3[] normals;   // Unity space
            public int[] indices;       // triangle list, Unity winding
        }

        [Serializable] class Root { public Accessor[] accessors; public BufferView[] bufferViews; public Mesh2[] meshes; }
        [Serializable] class Accessor { public int bufferView; public int byteOffset; public int componentType; public int count; public string type; }
        [Serializable] class BufferView { public int buffer; public int byteOffset; public int byteLength; public int byteStride; }
        [Serializable] class Mesh2 { public Primitive[] primitives; }
        [Serializable] class Primitive { public Attributes attributes; public int indices; }
        [Serializable] class Attributes { public int POSITION; public int NORMAL; }

        public static Geometry Read(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            if (BitConverter.ToUInt32(data, 0) != 0x46546C67u) throw new Exception("Not a GLB file.");

            string json = null; int binOffset = -1;
            int off = 12;
            while (off + 8 <= data.Length)
            {
                uint len = BitConverter.ToUInt32(data, off);
                uint type = BitConverter.ToUInt32(data, off + 4);
                int dataStart = off + 8;
                if (type == 0x4E4F534Au) json = System.Text.Encoding.UTF8.GetString(data, dataStart, (int)len);
                else if (type == 0x004E4942u) binOffset = dataStart;
                off = dataStart + (int)len;
            }
            if (json == null || binOffset < 0) throw new Exception("GLB missing JSON or BIN chunk.");

            var root = JsonUtility.FromJson<Root>(json);
            var prim = root.meshes[0].primitives[0];
            var posAcc = root.accessors[prim.attributes.POSITION];
            var nrmAcc = root.accessors[prim.attributes.NORMAL];

            var positions = ReadVec3(data, binOffset, root.bufferViews[posAcc.bufferView], posAcc);
            var normals = ReadVec3(data, binOffset, root.bufferViews[nrmAcc.bufferView], nrmAcc);

            // The PLATEAU export is non-indexed (sequential triangle list); JsonUtility
            // can't tell a missing "indices" from 0, so detect it from the raw JSON.
            int[] indices;
            int idxAccIndex = FindIndicesAccessor(json);
            if (idxAccIndex >= 0)
            {
                var idxAcc = root.accessors[idxAccIndex];
                indices = ReadIndices(data, binOffset, root.bufferViews[idxAcc.bufferView], idxAcc);
            }
            else
            {
                indices = new int[positions.Length];
                for (int i = 0; i < indices.Length; i++) indices[i] = i;
            }

            // glTF (RH, +Z toward viewer) -> Unity (LH): negate Z, reverse winding.
            for (int i = 0; i < positions.Length; i++)
            {
                positions[i].z = -positions[i].z;
                normals[i].z = -normals[i].z;
            }
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
            }

            return new Geometry { positions = positions, normals = normals, indices = indices };
        }

        static int FindIndicesAccessor(string json)
        {
            var m = Regex.Match(json, "\"indices\"\\s*:\\s*(\\d+)");
            return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : -1;
        }

        static Vector3[] ReadVec3(byte[] data, int binOffset, BufferView bv, Accessor acc)
        {
            int stride = bv.byteStride > 0 ? bv.byteStride : 12;
            int start = binOffset + bv.byteOffset + acc.byteOffset;
            var outArr = new Vector3[acc.count];
            for (int i = 0; i < acc.count; i++)
            {
                int p = start + i * stride;
                outArr[i] = new Vector3(
                    BitConverter.ToSingle(data, p),
                    BitConverter.ToSingle(data, p + 4),
                    BitConverter.ToSingle(data, p + 8));
            }
            return outArr;
        }

        static int[] ReadIndices(byte[] data, int binOffset, BufferView bv, Accessor acc)
        {
            int start = binOffset + bv.byteOffset + acc.byteOffset;
            var outArr = new int[acc.count];
            if (acc.componentType == 5125) // uint32
            {
                int stride = bv.byteStride > 0 ? bv.byteStride : 4;
                for (int i = 0; i < acc.count; i++)
                    outArr[i] = (int)BitConverter.ToUInt32(data, start + i * stride);
            }
            else if (acc.componentType == 5123) // uint16
            {
                int stride = bv.byteStride > 0 ? bv.byteStride : 2;
                for (int i = 0; i < acc.count; i++)
                    outArr[i] = BitConverter.ToUInt16(data, start + i * stride);
            }
            else throw new Exception("Unsupported index componentType: " + acc.componentType);
            return outArr;
        }
    }

    // ------------------------------------------------------------ buildings.json

    static class BuildingsJson
    {
        public struct Building
        {
            public string uid;
            public List<(int start, int count)> segments;
        }

        // ranges is a dict { uid: [ {triangle_start,triangle_count}, ... ] } which
        // JsonUtility can't parse, so pull it out with a targeted regex.
        static readonly Regex EntryRx = new Regex(
            "\"(?<uid>[^\"]+)\"\\s*:\\s*\\[(?<segs>[^\\]]*)\\]", RegexOptions.Compiled);
        static readonly Regex SegRx = new Regex(
            "\"triangle_start\"\\s*:\\s*(?<s>\\d+)\\s*,\\s*\"triangle_count\"\\s*:\\s*(?<c>\\d+)",
            RegexOptions.Compiled);

        public static List<Building> ReadRanges(string path, out int totalTriangles)
        {
            string text = File.ReadAllText(path);
            totalTriangles = 0;
            var mTot = Regex.Match(text, "\"total_triangles\"\\s*:\\s*(\\d+)");
            if (mTot.Success) totalTriangles = int.Parse(mTot.Groups[1].Value, CultureInfo.InvariantCulture);

            int rangesAt = text.IndexOf("\"ranges\"", StringComparison.Ordinal);
            string scope = rangesAt >= 0 ? text.Substring(rangesAt) : text;

            var list = new List<Building>();
            foreach (Match m in EntryRx.Matches(scope))
            {
                var segs = new List<(int, int)>();
                foreach (Match s in SegRx.Matches(m.Groups["segs"].Value))
                {
                    segs.Add((int.Parse(s.Groups["s"].Value, CultureInfo.InvariantCulture),
                              int.Parse(s.Groups["c"].Value, CultureInfo.InvariantCulture)));
                }
                if (segs.Count > 0)
                    list.Add(new Building { uid = m.Groups["uid"].Value, segments = segs });
            }
            return list;
        }
    }
}
