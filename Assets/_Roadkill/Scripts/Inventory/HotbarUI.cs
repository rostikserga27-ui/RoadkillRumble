using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// IMGUI hotbar along the bottom of the screen, plus the money counter in the top-right corner.
    /// Matches DebugHud's style; both get replaced together when the real HUD arrives.
    /// </summary>
    public class HotbarUI : MonoBehaviour
    {
        [SerializeField] HotbarInventory hotbar;
        [SerializeField] MoneyManager money;
        [SerializeField] float slotSize = 58f;
        [SerializeField] float gap = 6f;
        [SerializeField] float bottomMargin = 120f;
        [SerializeField] Color selectedColor = new Color(1f, 0.8f, 0.2f);

        GUIStyle small;
        GUIStyle moneyStyle;

        public void Setup(HotbarInventory inventory, MoneyManager wallet)
        {
            hotbar = inventory;
            money = wallet;
        }

        void OnGUI()
        {
            if (hotbar == null || money == null) return;
            if (small == null)
            {
                small = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.LowerCenter, wordWrap = true };
                small.normal.textColor = Color.white;
                moneyStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperRight };
                moneyStyle.normal.textColor = new Color(0.55f, 1f, 0.55f);
            }

            float w = Screen.width, h = Screen.height;
            GUI.Label(new Rect(w - 260f, 46f, 240f, 30f), $"$ {money.Money}", moneyStyle);
            GUI.Label(new Rect(w - 260f, 74f, 240f, 20f), "F5: +$ (debug)", small);

            int count = hotbar.SlotCount;
            float total = count * slotSize + (count - 1) * gap;
            float x0 = (w - total) * 0.5f, y = h - bottomMargin;
            for (int i = 0; i < count; i++)
            {
                var rect = new Rect(x0 + i * (slotSize + gap), y, slotSize, slotSize);
                bool selected = i == hotbar.Selected;
                if (selected) Fill(new Rect(rect.x - 3f, rect.y - 3f, rect.width + 6f, rect.height + 6f), selectedColor);
                Fill(rect, new Color(0f, 0f, 0f, selected ? 0.75f : 0.5f));

                var item = hotbar.Get(i);
                if (item != null)
                {
                    Fill(new Rect(rect.x + 16f, rect.y + 8f, rect.width - 32f, rect.height - 32f), item.IconColor);
                    GUI.Label(new Rect(rect.x, rect.y, rect.width, rect.height - 2f), item.DisplayName, small);
                }
                GUI.Label(new Rect(rect.x + 4f, rect.y + 2f, 20f, 16f), (i + 1).ToString());
            }
        }

        static void Fill(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
