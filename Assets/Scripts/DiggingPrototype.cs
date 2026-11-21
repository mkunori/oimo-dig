using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public sealed class DiggingPrototype : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    private const int InitialRequiredDigAmount = 10;
    private const int ShovelUpgradePrice = 3;
    private const int UpgradedDigPower = 2;
    private const double DigInterval = 0.25;
    private const double HarvestAnimationDuration = 0.18;
    private const float FullDigLift = 0.35f;
    private const float HarvestJumpHeight = 0.45f;

    [SerializeField] private Slider digProgressSlider;
    [SerializeField] private TMP_Text digAmountText;
    [SerializeField] private TMP_Text harvestCountText;
    [SerializeField] private TMP_Text ownedPotatoesText;
    [SerializeField] private Transform potatoVisual;
    [SerializeField] private GameObject upgradePanel;
    [SerializeField] private TMP_Text shovelDetailsText;
    [SerializeField] private Button buyShovelButton;
    [SerializeField] private TMP_Text buyShovelButtonText;

    private int digProgress;
    private int requiredDigAmount = InitialRequiredDigAmount;
    private int digPower = 1;
    private int harvestedCount;
    private int ownedPotatoes;
    private double nextDigTime;
    private Vector3 buriedLocalPosition;
    private double? harvestAnimationStart;

    private InputDevice heldDevice;
    private int heldPointerId;
    private int heldTouchId;
    private TouchControl heldTouch;
    private double heldTouchStartTime;

    private void Awake()
    {
        if (digProgressSlider == null || digAmountText == null || harvestCountText == null ||
            ownedPotatoesText == null || upgradePanel == null || shovelDetailsText == null ||
            buyShovelButton == null || buyShovelButtonText == null)
        {
            Debug.LogError("Assign the digging HUD and shovel upgrade UI references on DiggingPrototype.", this);
            enabled = false;
            return;
        }

        if (potatoVisual != null)
            buriedLocalPosition = potatoVisual.localPosition;

        upgradePanel.SetActive(false);
        RefreshDigDisplay();
        RefreshInventoryDisplay();
    }

    public void ToggleUpgradePanel()
    {
        if (isActiveAndEnabled)
            upgradePanel.SetActive(!upgradePanel.activeSelf);
    }

    public void BuyShovelUpgrade()
    {
        if (!isActiveAndEnabled || digPower == UpgradedDigPower || ownedPotatoes < ShovelUpgradePrice)
            return;

        ownedPotatoes -= ShovelUpgradePrice;
        digPower = UpgradedDigPower;
        RefreshInventoryDisplay();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!isActiveAndEnabled || !Application.isFocused || heldDevice != null ||
            eventData.button != PointerEventData.InputButton.Left ||
            !(eventData is ExtendedPointerEventData pointer))
        {
            return;
        }

        if (potatoVisual != null && !HitsVisiblePotato(eventData))
            return;

        if (pointer.device is Touchscreen touchscreen)
        {
            // Track this finger even when another finger becomes the primary touch.
            foreach (TouchControl touch in touchscreen.touches)
            {
                if (touch.touchId.ReadValue() == pointer.touchId)
                {
                    heldTouch = touch;
                    heldTouchStartTime = touch.startTime.ReadValue();
                    break;
                }
            }

            if (heldTouch == null)
                return;
        }
        else if (!(pointer.device is Mouse))
        {
            return;
        }

        heldDevice = pointer.device;
        heldPointerId = pointer.pointerId;
        heldTouchId = pointer.touchId;
        TryDig(Time.unscaledTimeAsDouble);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (heldDevice != null && eventData.button == PointerEventData.InputButton.Left &&
            eventData is ExtendedPointerEventData pointer && pointer.device == heldDevice &&
            pointer.pointerId == heldPointerId && pointer.touchId == heldTouchId)
        {
            ReleaseHold();
        }
    }

    private void LateUpdate()
    {
        double now = Time.unscaledTimeAsDouble;
        UpdateHarvestAnimation(now);

        if (heldDevice == null)
            return;

        // Read the same pointer after EventSystem has processed this frame's releases.
        if (!Application.isFocused || !heldDevice.added || !IsHeldPointerPressed())
        {
            ReleaseHold();
            return;
        }

        TryDig(now);
    }

    private bool IsHeldPointerPressed()
    {
        if (heldDevice is Mouse mouse)
            return mouse.leftButton.isPressed;

        // Touch slots and IDs can be reused. Check that this is still the original contact.
        return heldTouch.press.isPressed &&
               heldTouch.touchId.ReadValue() == heldTouchId &&
               heldTouch.startTime.ReadValue() == heldTouchStartTime;
    }

    private void TryDig(double now)
    {
        if (now < nextDigTime)
            return;

        // Schedule from this dig so a delayed frame can never cause a burst.
        nextDigTime = now + DigInterval;

        if (requiredDigAmount - digProgress <= digPower)
        {
            harvestedCount++;
            ownedPotatoes++;
            digProgress = 0;
            // ceil(requiredDigAmount * 1.2), using integer arithmetic.
            requiredDigAmount = (int)Math.Min(int.MaxValue, (requiredDigAmount * 6L + 4) / 5);
            if (potatoVisual != null)
                harvestAnimationStart = now;

            RefreshInventoryDisplay();
        }
        else
        {
            digProgress += digPower;
        }

        RefreshDigDisplay();
        if (harvestAnimationStart.HasValue)
            UpdateHarvestAnimation(now);
        else
            SetVisualLift(DigLift);
    }

    private float DigLift => FullDigLift * ((float)digProgress / requiredDigAmount);

    private void UpdateHarvestAnimation(double now)
    {
        if (!harvestAnimationStart.HasValue)
            return;

        float progress = (float)((now - harvestAnimationStart.Value) / HarvestAnimationDuration);
        if (progress >= 1f)
        {
            harvestAnimationStart = null;
            SetVisualLift(DigLift);
            return;
        }

        float lift = Mathf.Lerp(FullDigLift, 0f, progress * progress);
        float jump = HarvestJumpHeight * Mathf.Sin(Mathf.PI * progress);
        SetVisualLift(lift + jump);
    }

    private void SetVisualLift(float lift)
    {
        if (potatoVisual != null)
            potatoVisual.position = BuriedWorldPosition + Vector3.up * lift;
    }

    private Vector3 BuriedWorldPosition => potatoVisual.parent.TransformPoint(buriedLocalPosition);

    private bool HitsVisiblePotato(PointerEventData eventData)
    {
        Camera eventCamera = eventData.enterEventCamera;
        if (eventCamera == null)
            return false;

        // The existing box receives pointer events. Test the rendered sphere in its
        // local space so the empty corners of the box and buried half cannot start a dig.
        Ray ray = eventCamera.ScreenPointToRay(eventData.position);
        Matrix4x4 worldToVisual = potatoVisual.worldToLocalMatrix;
        Vector3 origin = worldToVisual.MultiplyPoint3x4(ray.origin);
        Vector3 direction = worldToVisual.MultiplyVector(ray.direction);
        float a = direction.sqrMagnitude;
        float b = Vector3.Dot(origin, direction);
        float discriminant = b * b - a * (origin.sqrMagnitude - 0.25f);
        if (discriminant < 0f)
            return false;

        float distance = (-b - Mathf.Sqrt(discriminant)) / a;
        return distance >= 0f && ray.GetPoint(distance).y >= BuriedWorldPosition.y;
    }

    private void RefreshDigDisplay()
    {
        digProgressSlider.maxValue = requiredDigAmount;
        digProgressSlider.SetValueWithoutNotify(digProgress);
        digAmountText.text = $"{digProgress} / {requiredDigAmount}";
    }

    private void RefreshInventoryDisplay()
    {
        harvestCountText.text = $"収穫累計  {harvestedCount}";
        ownedPotatoesText.text = $"所持芋  {ownedPotatoes}";
        shovelDetailsText.text = $"現在の掘削力  {digPower}\n強化後の掘削力  {UpgradedDigPower}\n価格  芋 {ShovelUpgradePrice}個";

        bool upgraded = digPower == UpgradedDigPower;
        buyShovelButton.interactable = !upgraded && ownedPotatoes >= ShovelUpgradePrice;
        buyShovelButtonText.text = upgraded ? "強化済み" : buyShovelButton.interactable ? "購入" : "芋が不足";
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
            ReleaseHold();
    }

    private void OnApplicationPause(bool isPaused)
    {
        if (isPaused)
            ReleaseHold();
    }

    private void OnDisable()
    {
        ReleaseHold();
        if (harvestAnimationStart.HasValue)
        {
            harvestAnimationStart = null;
            SetVisualLift(DigLift);
        }
    }

    private void ReleaseHold()
    {
        heldDevice = null;
        heldTouch = null;
    }
}
