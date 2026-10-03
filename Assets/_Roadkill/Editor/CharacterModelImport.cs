using UnityEditor;
using UnityEngine;

namespace Roadkill.EditorTools
{
    /// <summary>
    /// Import settings for the Blender character models: bake Blender's Z-up axes into the mesh,
    /// no animation clips (the characters are posed by code and physics), no cameras or lights.
    /// The fisherman imports as Humanoid (the avatar is kept for future retargeting; the prefab
    /// builder strips the Animator) and gets its shared palette material by name. His palette
    /// textures stay crisp: point filtering, no mipmaps, no compression.
    /// </summary>
    public class CharacterModelImport : AssetPostprocessor
    {
        // The old placeholder (Character.fbx, FPArm.fbx), kept importing exactly as before.
        const string LegacyFolder = "Assets/_Deprecated/Art/Character/";
        public const string FishermanFolder = "Assets/_Roadkill/Art/Fisherman/";
        public const string FishermanMaterialPath = FishermanFolder + "M_Fisherman.mat";
        const string BlenderMaterialName = "M_SK_Fisherman";
        /// <summary>Set for a moment by FishermanRagdollBuilder, which reads the vertices to fit colliders.</summary>
        public static bool ForceReadable;

        void OnPreprocessModel()
        {
            bool legacy = assetPath.StartsWith(LegacyFolder);
            bool fisherman = assetPath.StartsWith(FishermanFolder);
            if (!legacy && !fisherman) return;

            var importer = (ModelImporter)assetImporter;
            importer.bakeAxisConversion = true;
            importer.useFileScale = true;
            importer.globalScale = 1f;
            importer.importAnimation = false;
            importer.optimizeGameObjects = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.isReadable = ForceReadable;

            if (legacy)
            {
                // Generic keeps the skinning (None would turn the skinned meshes into rigid MeshRenderers).
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
                return;
            }

            bool skinned = !assetPath.EndsWith("FPArm.fbx");
            importer.animationType = skinned ? ModelImporterAnimationType.Human : ModelImporterAnimationType.None;
            if (skinned) importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            var material = AssetDatabase.LoadAssetAtPath<Material>(FishermanMaterialPath);
            if (material != null)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), BlenderMaterialName), material);
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(FishermanFolder)) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            // The mask map holds data (R metallic, G occlusion, A smoothness), not colour.
            importer.sRGBTexture = !assetPath.Contains("MaskMap");
            // Readable so PaletteTint can repaint the shirt cells in each player's colour (128x32 px).
            importer.isReadable = assetPath.Contains("BaseColor");
        }
    }
}
