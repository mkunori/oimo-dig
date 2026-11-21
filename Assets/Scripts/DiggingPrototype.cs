using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public sealed class DiggingPrototype : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    private const int DigsPerHarvest = 10;
    private const double DigInterval = 0.25;

    [SerializeField] private Slider digProgressSlider;
    [SerializeField] private TMP_Text harvestCountText;

    private int digProgress;
    private int harvestedCount;
    private double nextDigTime;

    private InputDevice heldDevice;
    private int heldPointerId;
    private int heldTouchId;
    private TouchControl heldTouch;
    private double heldTouchStartTime;

    private void Awake()
    {
        if (digProgressSlider == null || harvestCountText == null)
        {
            Debug.LogError("DiggingPrototype requires a Dig Progress Slider and a Harvest Count Text.", this);
            enabled = false;
            return;
        }

        RefreshDisplay();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!isActiveAndEnabled || !Application.isFocused || heldDevice != null ||
            eventData.button != PointerEventData.InputButton.Left ||
            !(eventData is ExtendedPointerEventData pointer))
        {
            return;
        }

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
        if (heldDevice == null)
            return;

        // Read the same pointer after EventSystem has processed this frame's releases.
        if (!Application.isFocused || !heldDevice.added || !IsHeldPointerPressed())
        {
            ReleaseHold();
            return;
        }

        TryDig(Time.unscaledTimeAsDouble);
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

        digProgress++;
        if (digProgress == DigsPerHarvest)
        {
            harvestedCount++;
            digProgress = 0;
        }

        RefreshDisplay();
    }

    private void RefreshDisplay()
    {
        digProgressSlider.SetValueWithoutNotify(digProgress);
        harvestCountText.text = $"Harvested: {harvestedCount}";
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
    }

    private void ReleaseHold()
    {
        heldDevice = null;
        heldTouch = null;
    }
}
