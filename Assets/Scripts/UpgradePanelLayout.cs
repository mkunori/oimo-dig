using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public sealed class UpgradePanelLayout : MonoBehaviour
{
    private const float MaximumWidth = 414f;
    private const float MaximumHeight = 620f;
    private const float SideMargin = 16f;
    private const float VerticalMargin = 94f;
    private const float FertilizerSideSpace = 224f;
    private const float FertilizerVerticalMargin = 157f;
    private const float ViewportVerticalPadding = 16f;

    [SerializeField] private RectTransform content;
    [SerializeField, Min(0)] private float headerHeight;

    private RectTransform panel;

    private void OnEnable()
    {
        panel = (RectTransform)transform;
        Resize(rebuildContent: true);
    }

    private void LateUpdate()
    {
        Resize();
    }

    private void Resize(bool rebuildContent = false)
    {
        if (content == null || !(panel.parent is RectTransform canvas))
            return;

        Vector2 available = canvas.rect.size;
        float width = Mathf.Min(MaximumWidth, Mathf.Max(0f, available.x - 2f * SideMargin));
        if (!Mathf.Approximately(panel.rect.width, width))
        {
            panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            rebuildContent = true;
        }
        if (rebuildContent)
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);

        // On narrower screens the fertilizer HUD sits below the panel.
        float margin = (available.x - width) / 2f < FertilizerSideSpace ? FertilizerVerticalMargin : VerticalMargin;
        float maxHeight = Mathf.Clamp(available.y - 2f * margin, 0f, MaximumHeight);
        float height = Mathf.Min(content.rect.height + ViewportVerticalPadding + headerHeight, maxHeight);
        if (!Mathf.Approximately(panel.rect.height, height))
            panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
    }
}
