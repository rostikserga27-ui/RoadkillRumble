using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Roadkill
{
    /// <summary>Flat greybox materials, cached per colour. Works on the built-in pipeline and URP.</summary>
    public static class RkMaterials
    {
        static readonly Dictionary<Color, Material> cache = new Dictionary<Color, Material>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => cache.Clear();

        public static Material Get(Color color)
        {
            if (cache.TryGetValue(color, out var existing) && existing != null) return existing;
            string shaderName = GraphicsSettings.currentRenderPipeline != null
                ? "Universal Render Pipeline/Lit"
                : "Standard";
            var shader = Shader.Find(shaderName);
            var material = new Material(shader) { color = color };
            cache[color] = material;
            return material;
        }
    }
}
