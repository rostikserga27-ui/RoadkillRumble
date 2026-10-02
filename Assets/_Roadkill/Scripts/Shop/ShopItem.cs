using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// A shop item hanging on a string. The hanger sways like a pendulum and the item bobs; looking at it
    /// tints it brighter. Purely local: every player has their own view of what they bought, so marking
    /// it sold only hides it for this player. The collider on the item is what the buy ray hits.
    /// </summary>
    public class ShopItem : MonoBehaviour
    {
        [SerializeField] ShopItemData data;
        [Tooltip("Pivots at the hook; the string and the item hang from it.")]
        [SerializeField] Transform hanger;
        [SerializeField] Transform item;
        [SerializeField] TextMesh label;
        [SerializeField] float swayDegrees = 6f;
        [SerializeField] float swaySpeed = 1.3f;
        [SerializeField] float bobHeight = 0.03f;
        [SerializeField] Color highlightTint = new Color(1f, 0.95f, 0.5f);

        public ShopItemData Data => data;
        public bool IsSold { get; private set; }

        Renderer[] renderers;
        Color[] baseColors;
        Vector3 itemRest;
        float phase;
        bool highlighted;

        public void Setup(ShopItemData itemData, Transform hangerPivot, Transform itemTransform, TextMesh priceLabel)
        {
            data = itemData;
            hanger = hangerPivot;
            item = itemTransform;
            label = priceLabel;
            Init();
        }

        void Awake()
        {
            if (item != null) Init();
        }

        void Init()
        {
            itemRest = item.localPosition;
            phase = Random.value * 10f;
            renderers = item.GetComponentsInChildren<Renderer>();
            baseColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                // Own material instance, so the highlight does not tint every object sharing the colour.
                renderers[i].material = new Material(renderers[i].sharedMaterial);
                baseColors[i] = renderers[i].material.color;
            }
        }

        void Update()
        {
            if (hanger == null) return;
            float t = Time.time * swaySpeed + phase;
            hanger.localRotation = Quaternion.Euler(Mathf.Sin(t) * swayDegrees, 0f, Mathf.Sin(t * 0.77f + 1.3f) * swayDegrees * 0.6f);
            item.localPosition = itemRest + Vector3.up * (Mathf.Sin(t * 1.9f) * bobHeight);
        }

        public void SetHighlighted(bool on)
        {
            if (on == highlighted || renderers == null) return;
            highlighted = on;
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].material.color = on ? Color.Lerp(baseColors[i], highlightTint, 0.55f) : baseColors[i];
        }

        public void SetLabel(string text)
        {
            if (label != null) label.text = text;
        }

        public void MarkSold()
        {
            IsSold = true;
            SetHighlighted(false);
            hanger.gameObject.SetActive(false);
            SetLabel("SOLD");
        }
    }
}
