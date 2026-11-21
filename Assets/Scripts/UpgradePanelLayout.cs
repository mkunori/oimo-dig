using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public sealed class UpgradePanelLayout : MonoBehaviour
{
    private const float MinimumPreferredWidth = 184f;
    private const float MaximumWidth = 336f;
    private const float TopMargin = 24f;

    [SerializeField] private RectTransform content;

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
        float gutter = Mathf.Abs(panel.anchoredPosition.x);
        float desiredWidth = Mathf.Clamp(available.x * 0.44f, MinimumPreferredWidth, MaximumWidth);
        float width = Mathf.Min(desiredWidth, Mathf.Max(0f, available.x - 2f * gutter));
        if (!Mathf.Approximately(panel.rect.width, width))
        {
            panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            rebuildContent = true;
        }
        if (rebuildContent)
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);

        // The content keeps its preferred height so a short viewport can scroll.
        float maxHeight = Mathf.Max(0f, available.y - panel.anchoredPosition.y - TopMargin);
        float height = Mathf.Min(content.rect.height, maxHeight);
        if (!Mathf.Approximately(panel.rect.height, height))
            panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
    }
}
