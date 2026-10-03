using System.IO;
using System;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Roadkill.EditorTools
{
    /// <summary>All forest FBXs share one matte palette material; keep the authored flat normals.</summary>
    [InitializeOnLoad]
    public sealed class ForestKitImport : AssetPostprocessor
    {
        const string Root = "Assets/_Roadkill/Art/ForestKit";
        const string TexturePath = Root + "/ForestPalette.png";
        const string MaterialPath = Root + "/M_ForestPalette.mat";

        static ForestKitImport() => EditorApplication.delayCall += EnsureMaterial;

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Root + "/")) return;
            var model = (ModelImporter)assetImporter;
            model.globalScale = 1f;
            model.useFileScale = true;
            model.importNormals = ModelImporterNormals.Import;
            model.importTangents = ModelImporterTangents.None;
            model.importAnimation = false;
            model.animationType = ModelImporterAnimationType.None;
            model.isReadable = false;
            model.addCollider = false;
            model.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        }

        void OnPreprocessTexture()
        {
            if (assetPath != TexturePath) return;
            var texture = (TextureImporter)assetImporter;
            texture.sRGBTexture = true;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.mipmapEnabled = false;
            texture.textureCompression = TextureImporterCompression.Uncompressed;
            texture.maxTextureSize = 256;
            texture.alphaSource = TextureImporterAlphaSource.None;
        }

        Material OnAssignMaterialModel(Material source, Renderer renderer)
        {
            if (!assetPath.StartsWith(Root + "/")) return null;
            return AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        }

        [MenuItem("Roadkill/Forest Kit/Configure Shared Material")]
        public static void EnsureMaterial()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += EnsureMaterial;
                return;
            }
            if (!File.Exists(TexturePath)) return;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            if (texture == null) return;
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null) return;
            var shader = Shader.Find(GraphicsSettings.currentRenderPipeline != null
                ? "Universal Render Pipeline/Lit" : "Standard");
            material = new Material(shader) { name = "M_ForestPalette", color = Color.white };
            material.mainTexture = texture;
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0f);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0f);
            AssetDatabase.CreateAsset(material, MaterialPath);
            AssetDatabase.SaveAssets();
            foreach (var path in Directory.GetFiles(Root, "*.fbx", SearchOption.AllDirectories))
                AssetDatabase.ImportAsset(path.Replace('\\', '/'), ImportAssetOptions.ForceUpdate);
        }

        [Serializable] sealed class AssetRecord
        {
            public string name;
            public string category;
            public int triangles;
            public float[] dimensions_m;
        }
        [Serializable] sealed class Manifest { public AssetRecord[] assets; }

        [MenuItem("Roadkill/Forest Kit/Validate Imported Assets")]
        public static void ValidateImportedAssets()
        {
            EnsureMaterial();
            var source = File.ReadAllText("ArtSource/ForestKit/manifest.json");
            var manifest = JsonUtility.FromJson<Manifest>("{\"assets\":" + source + "}");
            var shared = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (shared == null) throw new InvalidOperationException("Forest palette material missing");
            var report = new StringBuilder("Forest kit Unity import verification\n");
            foreach (var record in manifest.assets)
            {
                var path = Root + "/" + record.category + "/" + record.name + ".fbx";
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null) throw new InvalidOperationException("Missing FBX: " + path);
                long triangles = 0;
                var bounds = new Bounds();
                bool first = true;
                foreach (var filter in asset.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mesh = filter.sharedMesh;
                    for (var i = 0; i < mesh.subMeshCount; i++) triangles += mesh.GetIndexCount(i) / 3;
                    for (var i = 0; i < 8; i++)
                    {
                        var b = mesh.bounds;
                        var point = b.center + Vector3.Scale(b.extents,
                            new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        point = asset.transform.InverseTransformPoint(filter.transform.TransformPoint(point));
                        if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                        else bounds.Encapsulate(point);
                    }
                }
                if (triangles != record.triangles)
                    throw new InvalidOperationException(record.name + ": triangle count changed on import");
                var expected = new Vector3(record.dimensions_m[0], record.dimensions_m[2], record.dimensions_m[1]);
                if ((bounds.size - expected).magnitude > 0.025f || Mathf.Abs(bounds.min.y) > 0.025f)
                    throw new InvalidOperationException(record.name + ": axes, ground pivot or scale mismatch: " + bounds.size);
                foreach (var renderer in asset.GetComponentsInChildren<Renderer>(true))
                    foreach (var material in renderer.sharedMaterials)
                        if (material != shared) throw new InvalidOperationException(record.name + ": unshared material");
                report.AppendLine(record.name + ": PASS (" + triangles + " tris, scale, pivot, material)");
            }
            File.WriteAllText("ArtSource/ForestKit/Unity_Import_QA.txt", report.ToString());
            Debug.Log("Forest kit: all " + manifest.assets.Length + " imported assets passed verification.");
        }
    }
}
