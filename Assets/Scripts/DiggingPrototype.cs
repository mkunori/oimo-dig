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
    private const int AutoDigUnlockHarvests = 5;
    private const int AutoDigPrice = 2;
    private const double DigInterval = 0.25;
    private const double AutoDigInterval = 1.0;
    private const double AutosaveInterval = 3.0;
    private const double HarvestAnimationDuration = 0.18;
    private const float FullDigLift = 0.35f;
    private const float HarvestJumpHeight = 0.45f;
    private const string SaveKey = "OimoDig.PrototypeSave";
    private const int SaveVersion = 1;

    [Serializable]
    private sealed class SaveData
    {
        public int version;
        public int ownedPotatoes = -1;
        public int harvestedCount = -1;
        public int shovelLevel = -1;
        // A numeric flag lets the loader distinguish "not purchased" from a missing field.
        public int autoDigPurchased = -1;
        public int digProgress = -1;
        public int requiredDigAmount = -1;
    }

    private static readonly (int Price, int Power)[] ShovelUpgrades =
    {
        (3, 2),
        (5, 4),
        (8, 8)
    };

    [SerializeField] private Slider digProgressSlider;
    [SerializeField] private TMP_Text digAmountText;
    [SerializeField] private TMP_Text harvestCountText;
    [SerializeField] private TMP_Text ownedPotatoesText;
    [SerializeField] private Transform potatoVisual;
    [SerializeField] private GameObject firstDigHint;
    [SerializeField] private GameObject upgradePanel;
    [SerializeField] private GameObject firstShovelUpgradeBadge;
    [SerializeField] private TMP_Text shovelDetailsText;
    [SerializeField] private Button buyShovelButton;
    [SerializeField] private TMP_Text buyShovelButtonText;
    [SerializeField] private GameObject autoDigSection;
    [SerializeField] private Button buyAutoDigButton;
    [SerializeField] private TMP_Text buyAutoDigButtonText;

    private int digProgress;
    private int requiredDigAmount = InitialRequiredDigAmount;
    private int shovelLevel;
    private int harvestedCount;
    private int ownedPotatoes;
    private bool autoDigPurchased;
    private double nextDigTime;
    private double? nextAutoDigTime;
    private Vector3 buriedLocalPosition;
    private double? harvestAnimationStart;
    private bool saveDirty;
    private double nextAutosaveTime;

    private InputDevice heldDevice;
    private int heldPointerId;
    private int heldTouchId;
    private TouchControl heldTouch;
    private double heldTouchStartTime;

#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern int OimoDigGetPageVisibility();
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern void OimoDigSetPageSaveCallback(Action callback);
    private static readonly Action WebSaveCallback = SaveFromBrowser;
    private static DiggingPrototype webSaveOwner;
    private int pageVisibility;
#else
    private bool applicationPaused;
#endif

    private int DigPower => shovelLevel == 0 ? 1 : ShovelUpgrades[shovelLevel - 1].Power;
    private bool ShovelAtMaxLevel => shovelLevel == ShovelUpgrades.Length;
    private bool CanBuyShovel => !ShovelAtMaxLevel && ownedPotatoes >= ShovelUpgrades[shovelLevel].Price;
    private bool AutoDigUnlocked => harvestedCount >= AutoDigUnlockHarvests;
    private bool CanBuyAutoDig => AutoDigUnlocked && !autoDigPurchased && ownedPotatoes >= AutoDigPrice;

    private void Awake()
    {
        if (digProgressSlider == null || digAmountText == null || harvestCountText == null ||
            ownedPotatoesText == null || firstDigHint == null || upgradePanel == null ||
            firstShovelUpgradeBadge == null || shovelDetailsText == null ||
            buyShovelButton == null || buyShovelButtonText == null || autoDigSection == null ||
            buyAutoDigButton == null || buyAutoDigButtonText == null)
        {
            Debug.LogError("Assign the digging HUD, onboarding and upgrade UI references on DiggingPrototype.", this);
            enabled = false;
            return;
        }

        if (potatoVisual != null)
            buriedLocalPosition = potatoVisual.localPosition;

        LoadSave();
        upgradePanel.SetActive(false);
        RefreshDigDisplay();
        RefreshInventoryDisplay();
        SetVisualLift(DigLift);
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    private void OnEnable()
    {
        webSaveOwner = this;
        OimoDigSetPageSaveCallback(WebSaveCallback);
    }

    [AOT.MonoPInvokeCallback(typeof(Action))]
    private static void SaveFromBrowser()
    {
        if (webSaveOwner != null)
            webSaveOwner.SaveIfDirty();
    }
#endif

    public void ToggleUpgradePanel()
    {
        if (!isActiveAndEnabled)
            return;

        upgradePanel.SetActive(!upgradePanel.activeSelf);
        RefreshFirstShovelUpgradeBadge();
    }

    public void BuyShovelUpgrade()
    {
        if (!isActiveAndEnabled || !CanBuyShovel)
            return;

        ownedPotatoes -= ShovelUpgrades[shovelLevel].Price;
        shovelLevel++;
        RefreshInventoryDisplay();
        RequestSave(Time.unscaledTimeAsDouble, immediately: true);
    }

    public void BuyAutoDig()
    {
        if (!isActiveAndEnabled || !CanBuyAutoDig)
            return;

        ownedPotatoes -= AutoDigPrice;
        autoDigPurchased = true;
        nextAutoDigTime = Time.unscaledTimeAsDouble + AutoDigInterval;
        RefreshInventoryDisplay();
        RequestSave(Time.unscaledTimeAsDouble, immediately: true);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!isActiveAndEnabled || !Application.isFocused || !IsDiggingAvailable() || heldDevice != null ||
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
        TryManualDig(Time.unscaledTimeAsDouble);
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

        if (!IsDiggingAvailable())
        {
            SuspendDigging();
            return;
        }

        // Read the same pointer after EventSystem has processed this frame's releases.
        if (heldDevice != null)
        {
            if (Application.isFocused && heldDevice.added && IsHeldPointerPressed())
                TryManualDig(now);
            else
                ReleaseHold();
        }

        if (autoDigPurchased)
            TryAutoDig(now);

        if (saveDirty && now >= nextAutosaveTime)
            SaveIfDirty();
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

    private void TryManualDig(double now)
    {
        if (now < nextDigTime)
            return;

        // Schedule from this dig so a delayed frame can never cause a burst.
        nextDigTime = now + DigInterval;
        ApplyDig(now);
    }

    private void TryAutoDig(double now)
    {
        nextAutoDigTime ??= now + AutoDigInterval;
        if (now < nextAutoDigTime.Value)
            return;

        nextAutoDigTime = now + AutoDigInterval;
        ApplyDig(now);
    }

    private void ApplyDig(double now)
    {
        int power = DigPower;
        bool harvested = requiredDigAmount - digProgress <= power;
        if (harvested)
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
            digProgress += power;
        }

        RefreshDigDisplay();
        if (harvestAnimationStart.HasValue)
            UpdateHarvestAnimation(now);
        else
            SetVisualLift(DigLift);

        RequestSave(now, immediately: harvested);
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
        firstDigHint.SetActive(harvestedCount == 0);

        shovelDetailsText.text = $"現在の掘削力  {DigPower}";
        if (!ShovelAtMaxLevel)
        {
            var nextUpgrade = ShovelUpgrades[shovelLevel];
            shovelDetailsText.text += $"\n次の掘削力  {nextUpgrade.Power}\n価格  芋 {nextUpgrade.Price}個";
        }

        buyShovelButton.interactable = CanBuyShovel;
        buyShovelButtonText.text = ShovelAtMaxLevel ? "最大レベル" : CanBuyShovel ? "購入" : "芋が不足";
        RefreshFirstShovelUpgradeBadge();

        autoDigSection.SetActive(AutoDigUnlocked);
        buyAutoDigButton.interactable = CanBuyAutoDig;
        buyAutoDigButtonText.text = autoDigPurchased ? "稼働中" : CanBuyAutoDig ? "購入" : "芋が不足";
    }

    private void RefreshFirstShovelUpgradeBadge()
    {
        firstShovelUpgradeBadge.SetActive(shovelLevel == 0 && CanBuyShovel && !upgradePanel.activeSelf);
    }

    private void LoadSave()
    {
        if (!PlayerPrefs.HasKey(SaveKey))
            return;

        var data = new SaveData();
        try
        {
            JsonUtility.FromJsonOverwrite(PlayerPrefs.GetString(SaveKey), data);
        }
        catch (ArgumentException)
        {
            Debug.LogWarning("OIMO-DIG could not read the local save. Starting with default progress.", this);
            return;
        }

        if (data.version != SaveVersion || data.harvestedCount < 0 ||
            data.ownedPotatoes < 0 || data.ownedPotatoes > data.harvestedCount ||
            data.shovelLevel < 0 || data.shovelLevel > ShovelUpgrades.Length ||
            data.autoDigPurchased < 0 || data.autoDigPurchased > 1 ||
            (data.autoDigPurchased == 1 && data.harvestedCount < AutoDigUnlockHarvests) ||
            data.requiredDigAmount < InitialRequiredDigAmount ||
            data.digProgress < 0 || data.digProgress >= data.requiredDigAmount)
        {
            Debug.LogWarning("OIMO-DIG local save is invalid or uses an unsupported version. Starting with default progress.", this);
            return;
        }

        ownedPotatoes = data.ownedPotatoes;
        harvestedCount = data.harvestedCount;
        shovelLevel = data.shovelLevel;
        autoDigPurchased = data.autoDigPurchased == 1;
        digProgress = data.digProgress;
        requiredDigAmount = data.requiredDigAmount;
    }

    private void RequestSave(double now, bool immediately)
    {
        if (!saveDirty)
        {
            saveDirty = true;
            nextAutosaveTime = now + AutosaveInterval;
        }

        if (immediately)
            SaveIfDirty();
    }

    private void SaveIfDirty()
    {
        if (!saveDirty)
            return;

        var data = new SaveData
        {
            version = SaveVersion,
            ownedPotatoes = ownedPotatoes,
            harvestedCount = harvestedCount,
            shovelLevel = shovelLevel,
            autoDigPurchased = autoDigPurchased ? 1 : 0,
            digProgress = digProgress,
            requiredDigAmount = requiredDigAmount
        };

        try
        {
            PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(data));
            PlayerPrefs.Save();
            saveDirty = false;
        }
        catch (PlayerPrefsException exception)
        {
            nextAutosaveTime = Time.unscaledTimeAsDouble + AutosaveInterval;
            Debug.LogWarning($"OIMO-DIG could not write the local save: {exception.Message}", this);
        }
    }

    private bool IsDiggingAvailable()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        // A tab can become visible again without the Unity canvas regaining focus.
        int visibility = OimoDigGetPageVisibility();
        if (visibility != pageVisibility)
        {
            pageVisibility = visibility;
            SuspendDigging();
            if (visibility < 0)
                SaveIfDirty();
        }

        return visibility > 0;
#else
        return Application.isFocused && !applicationPaused;
#endif
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
            return;

#if UNITY_WEBGL && !UNITY_EDITOR
        ReleaseHold();
#else
        SuspendDigging();
#endif
        SaveIfDirty();
    }

    private void OnApplicationPause(bool isPaused)
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        applicationPaused = isPaused;
#endif
        if (isPaused)
        {
            SuspendDigging();
            SaveIfDirty();
        }
    }

    private void OnDisable()
    {
        SaveIfDirty();
#if UNITY_WEBGL && !UNITY_EDITOR
        if (webSaveOwner == this)
        {
            OimoDigSetPageSaveCallback(null);
            webSaveOwner = null;
        }
#endif
        SuspendDigging();
        if (harvestAnimationStart.HasValue)
        {
            harvestAnimationStart = null;
            SetVisualLift(DigLift);
        }
    }

    private void SuspendDigging()
    {
        ReleaseHold();
        nextAutoDigTime = null;
    }

    private void ReleaseHold()
    {
        heldDevice = null;
        heldTouch = null;
    }
}
