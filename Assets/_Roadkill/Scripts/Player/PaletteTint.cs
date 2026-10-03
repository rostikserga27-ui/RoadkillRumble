using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Per-player colour on a palette-textured model: repaints a few palette cells (the fisherman's
    /// shirt) in a private copy of the palette texture, used only by the listed renderers. Everything
    /// else keeps sharing the one palette material.
    /// </summary>
    public class PaletteTint : MonoBehaviour
    {
        public Renderer[] renderers;
        [Tooltip("Palette cells to repaint, in palette order (row-major from the top-left).")]
        public int[] cells = { 2 };
        [Tooltip("Cells painted a shade darker (hems, recesses).")]
        public int[] darkCells = { 3 };
        public int columns = 8;
        public int cellSize = 16;
        [Range(0f, 1f), Tooltip("How far player colours are pulled toward grey, so they stay earthy.")]
        public float desaturate = 0.3f;

        Texture2D texture;

        public void Apply(Color color)
        {
            if (renderers == null || renderers.Length == 0) return;
            var source = renderers[0].sharedMaterial.mainTexture as Texture2D;
            if (source == null || !source.isReadable) return;

            if (texture == null)
            {
                texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    name = source.name + "_Tint"
                };
            }
            texture.SetPixels32(source.GetPixels32());
            Color main = Color.Lerp(color, new Color(0.55f, 0.55f, 0.55f), desaturate);
            foreach (int cell in cells) Fill(cell, main);
            foreach (int cell in darkCells) Fill(cell, main * 0.78f);
            texture.Apply(false);
            foreach (var r in renderers) r.material.mainTexture = texture;
        }

        void Fill(int cell, Color color)
        {
            int x = cell % columns * cellSize;
            int y = texture.height - (cell / columns + 1) * cellSize;
            var block = new Color[cellSize * cellSize];
            for (int i = 0; i < block.Length; i++) block[i] = new Color(color.r, color.g, color.b, 1f);
            texture.SetPixels(x, y, cellSize, cellSize, block);
        }

        void OnDestroy()
        {
            if (texture != null) Destroy(texture);
        }
    }
}
