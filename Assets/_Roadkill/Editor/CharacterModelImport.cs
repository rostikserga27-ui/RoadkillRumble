using UnityEditor;

namespace Roadkill.EditorTools
{
    /// <summary>
    /// Import settings for the Blender character models: bake Blender's Z-up axes into the mesh,
    /// no animation clips or Animator (the character is animated by CharacterAnimator in code),
    /// no cameras or lights.
    /// </summary>
    public class CharacterModelImport : AssetPostprocessor
    {
        const string Folder = "Assets/_Roadkill/Art/Character/";

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var importer = (ModelImporter)assetImporter;
            importer.bakeAxisConversion = true;
            importer.useFileScale = true;
            importer.globalScale = 1f;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.importNormals = ModelImporterNormals.Import;
        }
    }
}
