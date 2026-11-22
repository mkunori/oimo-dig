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
    private const double DigInterval = 0.25;
    private const int IndividuallyRolledAutoStrikes = 64;
    private const double AutosaveInterval = 3.0;
    private const double ClockCheckpointInterval = 15.0;
    private const double UpgradeHintRefreshInterval = 1.0;
    private const double HarvestAnimationDuration = 0.18;
    private const float FullDigLift = 0.53f;
    private const float HarvestJumpHeight = 0.45f;

    [SerializeField] private Slider digProgressSlider;
    [SerializeField] private TMP_Text ownedPotatoesText;
    [SerializeField] private Transform potatoVisual;
    [SerializeField] private DiggingFeedback diggingFeedback;
    [SerializeField] private GameObject firstDigHint;
    [SerializeField] private GameObject upgradePanel;
    [SerializeField] private Image upgradeBackdrop;
    [SerializeField] private Graphic upgradeToggleGraphic;
    [SerializeField] private TMP_Text upgradeHint;
    [SerializeField, Min(1)] private float stalledHarvestSeconds = 30;
    [SerializeField] private GameObject firstShovelUpgradeBadge;
    [SerializeField] private TMP_Text upgradeBadgeLabel;
    [SerializeField] private DigUpgradeCard[] upgradeCards;
    [SerializeField] private RebirthView rebirthView;
    [SerializeField] private FertilizerView fertilizerView;

    private DiggingProgress state;
    private bool canSave;
    private float displayedProgress;
    private double nextDigTime;
    private double lastDigTime = double.NegativeInfinity;
    private double? nextAutoDigTime;
    private Vector3 buriedLocalPosition;
    private Quaternion buriedLocalRotation;
    private double? digWobbleStart;
    private double? harvestAnimationStart;
    private bool saveDirty;
    private double nextAutosaveTime;
    private double lastFertilizerTick;
    private double nextClockCheckpoint;
    private bool foreground;
    private bool rapidHarvests;
    private double nextUpgradeHintRefresh;

    private InputDevice heldDevice;
    private int heldPointerId;
    private int heldTouchId;
    private TouchControl heldTouch;
    private double heldTouchStartTime;

#if UNITY_WEBGL && !UNITY_EDITOR
    private delegate void BrowserVisibilityHandler(int visible);
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern int OimoDigGetPageVisibility();
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern void OimoDigSetPageVisibilityCallback(BrowserVisibilityHandler callback);
    private static readonly BrowserVisibilityHandler WebVisibilityCallback = HandleBrowserVisibility;
    private static DiggingPrototype webSaveOwner;
#else
    private bool applicationPaused;
#endif

    private void Awake()
    {
        if (digProgressSlider == null || ownedPotatoesText == null || firstDigHint == null || upgradePanel == null ||
            firstShovelUpgradeBadge == null || upgradeBadgeLabel == null || rebirthView == null || fertilizerView == null || upgradeCards == null ||
            upgradeCards.Length != DiggingEconomy.UpgradeCount || Array.Exists(upgradeCards, card => card == null))
        {
            Debug.LogError("Assign the digging HUD, onboarding and upgrade UI references on DiggingPrototype.", this);
            enabled = false;
            return;
        }

        if (potatoVisual != null)
        {
            buriedLocalPosition = potatoVisual.localPosition;
            buriedLocalRotation = potatoVisual.localRotation;
        }

#if UNITY_EDITOR
        if (RebirthVerification.TryTakeFixture(out state))
            canSave = true;
        else
#endif
            canSave = DiggingSave.TryLoad(out state);
        if (!canSave)
            Debug.LogWarning("OIMO-DIG could not load this save. Existing data will be preserved.", this);

        upgradePanel.SetActive(false);
        if (upgradeBackdrop != null)
        {
            upgradeBackdrop.raycastTarget = false;
            upgradeBackdrop.gameObject.SetActive(false);
            var outsidePress = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            outsidePress.callback.AddListener(data =>
            {
                if (data is PointerEventData pointer && pointer.button == PointerEventData.InputButton.Left)
                {
                    if (fertilizerView.IsOpen)
                        CloseFertilizer();
                    else
                        SetUpgradePanelOpen(false);
                }
            });
            upgradeBackdrop.gameObject.AddComponent<EventTrigger>().triggers.Add(outsidePress);
        }
        rebirthView.Connect(this);
        fertilizerView.Connect(this);
        foreach (var card in upgradeCards)
            card.Connect(this);
        digProgressSlider.wholeNumbers = false;
        digProgressSlider.maxValue = 1f;
        displayedProgress = state.Progress.FractionOf(state.Target);
        digProgressSlider.SetValueWithoutNotify(displayedProgress);
        RefreshInventoryDisplay();
        SetVisualLift(DigLift);
        lastFertilizerTick = Time.realtimeSinceStartupAsDouble;
        foreground = IsForeground();
        if (foreground)
            state.Fertilizer.AccrueAwayTime(DateTime.UtcNow.Ticks);
        else if (state.Fertilizer.CheckpointUtcTicks == 0)
            state.Fertilizer.Checkpoint(DateTime.UtcNow.Ticks);
        fertilizerView.Refresh(state.Fertilizer);
        RequestSave(Time.unscaledTimeAsDouble, immediately: true);
    }

    private void OnEnable()
    {
        SetForeground(IsForeground());
#if UNITY_WEBGL && !UNITY_EDITOR
        webSaveOwner = this;
        OimoDigSetPageVisibilityCallback(WebVisibilityCallback);
#endif
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    [AOT.MonoPInvokeCallback(typeof(BrowserVisibilityHandler))]
    private static void HandleBrowserVisibility(int visible)
    {
        if (webSaveOwner != null)
            webSaveOwner.SetForeground(visible != 0);
    }
#endif

    private bool IsModalOpen => rebirthView.IsOpen || fertilizerView.IsOpen;

    internal void ToggleFertilizer()
    {
        if (!isActiveAndEnabled || rebirthView.IsOpen || upgradePanel.activeSelf)
            return;
        if (fertilizerView.IsOpen)
        {
            CloseFertilizer();
            return;
        }
        SuspendDigging();
        fertilizerView.Open(state.Fertilizer);
        RefreshMenuInput();
    }

    private void CloseFertilizer()
    {
        fertilizerView.Close();
        RefreshMenuInput();
    }

    internal void BuyFertilizerBoost()
    {
        if (!CanUseFertilizerPanel())
            return;
        AdvanceFertilizerClock();
        var fertilizer = state.Fertilizer;
        if (fertilizer.IsBoosted || fertilizer.Balance < FertilizerState.BoostPrice) return;
        fertilizer.BuyBoost();
        UpdateManualInterval();
        CloseFertilizer();
        CommitFertilizerUpdate();
        RefreshUpgradeCards();
    }

    internal void BuyFertilizerCapacity()
    {
        if (!CanUseFertilizerPanel() || state.Fertilizer.Balance < state.Fertilizer.CapacityPrice)
            return;
        state.Fertilizer.BuyCapacity();
        CommitFertilizerUpdate();
    }

    internal void BuyFertilizerRate()
    {
        if (!CanUseFertilizerPanel() || state.Fertilizer.Balance < state.Fertilizer.RatePrice)
            return;
        state.Fertilizer.BuyRate();
        CommitFertilizerUpdate();
    }

    private bool CanUseFertilizerPanel() => isActiveAndEnabled && foreground && fertilizerView.IsOpen;

    private void CommitFertilizerUpdate()
    {
        RequestSave(Time.unscaledTimeAsDouble, immediately: true);
        fertilizerView.Refresh(state.Fertilizer);
    }

    public void ToggleUpgradePanel()
    {
        if (!isActiveAndEnabled || IsModalOpen)
            return;

        SetUpgradePanelOpen(!upgradePanel.activeSelf);
    }

    private void SetUpgradePanelOpen(bool open)
    {
        ReleaseHold();
        if (open)
            RefreshUpgradeHint(Time.unscaledTimeAsDouble);
        upgradePanel.SetActive(open);
        RefreshMenuInput();
        RefreshUpgradeBadge();
    }

    private void RefreshMenuInput()
    {
        bool upgradeOpen = upgradePanel.activeSelf;
        bool fertilizerOpen = fertilizerView.IsOpen;
        bool menuOpen = upgradeOpen || fertilizerOpen;
        if (upgradeBackdrop != null)
        {
            upgradeBackdrop.raycastTarget = menuOpen;
            upgradeBackdrop.gameObject.SetActive(menuOpen);
        }
        if (upgradeToggleGraphic != null)
            upgradeToggleGraphic.raycastTarget = !fertilizerOpen;
        fertilizerView.SetToggleRaycast(!upgradeOpen);
    }

    public void BuyUpgrade(DigUpgrade upgrade)
    {
        if (!isActiveAndEnabled || IsModalOpen || !state.Buy(upgrade))
            return;

        RefreshInventoryDisplay();
        if (upgradePanel.activeSelf)
            RefreshUpgradeHint(Time.unscaledTimeAsDouble);
        RequestSave(Time.unscaledTimeAsDouble, immediately: true);
    }

    internal void OpenRebirth(RebirthKind kind)
    {
        if (!isActiveAndEnabled || IsModalOpen || !DiggingRebirth.CanUse(kind, state))
            return;
        SuspendDigging();
        rebirthView.Open(kind, state);
    }

    internal void ConfirmRebirth(RebirthKind kind)
    {
        if (!isActiveAndEnabled || !rebirthView.IsOpen || !DiggingRebirth.TryExecute(kind, state))
            return;

        SuspendDigging();
        nextDigTime = 0;
        lastDigTime = double.NegativeInfinity;
        if (diggingFeedback != null) diggingFeedback.ResetPace();
        rapidHarvests = false;
        harvestAnimationStart = digWobbleStart = null;
        if (potatoVisual != null) potatoVisual.localRotation = buriedLocalRotation;
        displayedProgress = 0;
        digProgressSlider.SetValueWithoutNotify(0);
        SetVisualLift(0);
        RequestSave(Time.unscaledTimeAsDouble, immediately: true);
        rebirthView.Close();
        SetUpgradePanelOpen(false);
        RefreshInventoryDisplay();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!isActiveAndEnabled || IsModalOpen || upgradePanel.activeSelf || !Application.isFocused || !foreground || heldDevice != null ||
            eventData.button != PointerEventData.InputButton.Left ||
            !(eventData is ExtendedPointerEventData pointer))
        {
            return;
        }

        // The raycaster hits the actual potato mesh or the soil in front of it.
        if (potatoVisual == null || eventData.pointerCurrentRaycast.gameObject != potatoVisual.gameObject)
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
        AdvanceFertilizerClock();
        TryManualDig(Time.unscaledTimeAsDouble, heldRepeat: false);
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
        if (diggingFeedback != null && diggingFeedback.UpdatePace(now) != rapidHarvests)
        {
            rapidHarvests = diggingFeedback.IsRapid;
            harvestAnimationStart = digWobbleStart = null;
            if (potatoVisual != null) potatoVisual.localRotation = buriedLocalRotation;
            SetVisualLift(DigLift);
        }
        UpdateHarvestAnimation(now);
        UpdateDigWobble(now);
        float targetProgress = state.Progress.FractionOf(state.Target);
        displayedProgress = Mathf.Lerp(displayedProgress, targetProgress, 1f - Mathf.Exp(-20f * Time.unscaledDeltaTime));
        digProgressSlider.SetValueWithoutNotify(displayedProgress);

        SetForeground(IsForeground());
        if (!foreground) return;
        AdvanceFertilizerClock();

        if (saveDirty && now >= nextAutosaveTime)
            SaveIfDirty();
        else if (now >= nextClockCheckpoint)
            RequestSave(now, immediately: true);
        if (IsModalOpen)
            return;

        // Read the same pointer after EventSystem has processed this frame's releases.
        if (heldDevice != null)
        {
            if (Application.isFocused && heldDevice.added && IsHeldPointerPressed())
                TryManualDig(now, heldRepeat: true);
            else
                ReleaseHold();
        }

        if (state.Level(DigUpgrade.Auto) > 0)
            TryAutoDig(now);

        if (state.PendingHits.Count > 0 || !state.Carry.IsZero)
            ApplyDig(now);

        if (upgradePanel.activeSelf && now >= nextUpgradeHintRefresh)
            RefreshUpgradeHint(now);
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

    private double ManualInterval => state.Fertilizer.IsBoosted ? DigInterval / FertilizerState.BoostSpeed : DigInterval;

    private void UpdateManualInterval() => nextDigTime = lastDigTime + ManualInterval;

    private void TryManualDig(double now, bool heldRepeat)
    {
        double interval = ManualInterval;
        if (now < nextDigTime || (!heldRepeat && now < lastDigTime + interval))
            return;

        // Preserve the boosted hold cadence across ordinary frame boundaries. A missed
        // interval, or lifting and pressing again, always reanchors it to the new hit.
        double nextTick = nextDigTime + interval;
        nextDigTime = state.Fertilizer.IsBoosted && heldRepeat && nextTick > now
            ? nextTick : now + interval;
        lastDigTime = now;
        var power = state.ManualPower;
        if (RollCritical(CriticalChance))
            power = power.Multiply(DiggingEconomy.CriticalMultiplier);
        state.QueueManualDig(power);
    }

    private double CriticalChance => DiggingEconomy.CriticalChance(
        state.Level(DigUpgrade.Critical), state.PrestigeMultiplier);

    private static bool RollCritical(double chance) => chance > 0 && UnityEngine.Random.value < chance;

    private double AutoSpeedMultiplier => state.Fertilizer.IsBoosted ? FertilizerState.BoostSpeed : 1;

    private void TryAutoDig(double now)
    {
        nextAutoDigTime ??= now + DiggingEconomy.AutoInterval;
        if (now < nextAutoDigTime.Value)
            return;

        double nextTick = nextAutoDigTime.Value + DiggingEconomy.AutoInterval;
        nextAutoDigTime = nextTick > now ? nextTick : now + DiggingEconomy.AutoInterval;
        var hitCount = DiggingEconomy.AutoHitsPerSecond(state.Level(DigUpgrade.Auto), state.PrestigeMultiplier)
            .Multiply(DiggingEconomy.AutoInterval * AutoSpeedMultiplier);
        QueueAutoStrikes(state.ManualPower, hitCount, CriticalChance);
    }

    private void QueueAutoStrikes(LargeNumber power, LargeNumber hitCount, double chance)
    {
        if (chance <= 0)
        {
            state.QueueAutoDig(power, hitCount);
            return;
        }

        LargeNumber criticalPower = power.Multiply(DiggingEconomy.CriticalMultiplier);
        LargeNumber wholeHits = hitCount.Floor();
        LargeNumber fraction = hitCount - wholeHits;
        for (int i = 0; i < IndividuallyRolledAutoStrikes && !wholeHits.IsZero; i++)
        {
            state.QueueAutoDig(RollCritical(chance) ? criticalPower : power, LargeNumber.One);
            wholeHits -= LargeNumber.One;
        }

        if (!wholeHits.IsZero)
        {
            // At high speeds, stochastically round the expected critical count
            // and keep separate strike sizes so each group still uses normal carry.
            LargeNumber criticalHits = (wholeHits.Multiply(chance) + LargeNumber.From(UnityEngine.Random.value)).Floor();
            LargeNumber normalHits = wholeHits - criticalHits;
            if (RollCritical(0.5))
            {
                state.QueueAutoDig(criticalPower, criticalHits);
                state.QueueAutoDig(power, normalHits);
            }
            else
            {
                state.QueueAutoDig(power, normalHits);
                state.QueueAutoDig(criticalPower, criticalHits);
            }
        }

        if (!fraction.IsZero)
            state.QueueAutoDig(RollCritical(chance) ? criticalPower : power, fraction);
    }

    private void ApplyDig(double now)
    {
        DigBatch batch = state.ApplyPending();
        bool harvested = batch.Harvests > 0;
        if (harvested)
        {
            displayedProgress = 0;
            digProgressSlider.SetValueWithoutNotify(0);
            if (potatoVisual != null && !rapidHarvests)
                harvestAnimationStart = now;

            RefreshInventoryDisplay();
        }

        if (!rapidHarvests) digWobbleStart = now;
        UpdateDigWobble(now);
        if (diggingFeedback != null)
            diggingFeedback.PlayDig(batch, now);
        if (harvestAnimationStart.HasValue)
            UpdateHarvestAnimation(now);
        else
            SetVisualLift(DigLift);

        RequestSave(now, immediately: harvested);
    }

    private float DigLift => FullDigLift * (rapidHarvests ? 0.6f : state.Progress.FractionOf(state.Target));

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

    private void UpdateDigWobble(double now)
    {
        if (potatoVisual == null || !digWobbleStart.HasValue)
            return;

        float t = (float)((now - digWobbleStart.Value) / 0.16);
        if (t >= 1f)
        {
            digWobbleStart = null;
            potatoVisual.localRotation = buriedLocalRotation;
            return;
        }

        float turn = 5f * (1f - t) * Mathf.Sin(t * Mathf.PI * 2f);
        potatoVisual.localRotation = buriedLocalRotation * Quaternion.Euler(turn * 0.35f, 0f, turn);
    }

    private void RefreshInventoryDisplay()
    {
        ownedPotatoesText.text = $"所持芋  {state.Wallet.Format()}";
        firstDigHint.SetActive(state.HarvestCount.IsZero);
        RefreshUpgradeCards();
        rebirthView.Refresh(state);
        RefreshUpgradeBadge();
    }

    private void RefreshUpgradeCards()
    {
        var power = state.ManualPower;
        foreach (var card in upgradeCards)
            card.Refresh(state.Level(card.Upgrade), state.CanBuy(card.Upgrade), state.PrestigeMultiplier,
                power, AutoSpeedMultiplier);
    }

    private void RefreshUpgradeBadge()
    {
        bool power = state.Level(DigUpgrade.Power) == 0 && state.CanBuy(DigUpgrade.Power);
        bool auto = state.Level(DigUpgrade.Auto) == 0 && state.CanBuy(DigUpgrade.Auto);
        firstShovelUpgradeBadge.SetActive(!upgradePanel.activeSelf && (power || auto));
        if (firstShovelUpgradeBadge.activeSelf)
            upgradeBadgeLabel.text = power ? "掘力を強化" : "自動掘を購入";
    }

    private void RefreshUpgradeHint(double now)
    {
        nextUpgradeHintRefresh = now + UpgradeHintRefreshInterval;
        if (upgradeHint == null)
            return;

        string hint = DigUpgradeHint.Describe(state, stalledHarvestSeconds, 1 / DigInterval);
        if (upgradeHint.text != hint)
            upgradeHint.text = hint;
        upgradeHint.gameObject.SetActive(hint.Length > 0);
    }

    private void RequestSave(double now, bool immediately)
    {
        nextClockCheckpoint = now + ClockCheckpointInterval;
        if (!canSave)
            return;

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

        try
        {
            if (foreground) state.Fertilizer.Checkpoint(DateTime.UtcNow.Ticks);
            DiggingSave.Save(state);
            saveDirty = false;
        }
        catch (PlayerPrefsException exception)
        {
            nextAutosaveTime = Time.unscaledTimeAsDouble + AutosaveInterval;
            Debug.LogWarning($"OIMO-DIG could not write the local save: {exception.Message}", this);
        }
    }

    private bool IsForeground()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return OimoDigGetPageVisibility() > 0;
#else
        return Application.isFocused && !applicationPaused;
#endif
    }

    private void SetForeground(bool visible)
    {
        if (state == null || foreground == visible) return;
        long utcTicks = DateTime.UtcNow.Ticks;
        if (visible)
        {
            state.Fertilizer.AccrueAwayTime(utcTicks);
            lastFertilizerTick = Time.realtimeSinceStartupAsDouble;
        }
        else
        {
            AdvanceFertilizerClock();
            state.Fertilizer.Checkpoint(utcTicks);
        }
        foreground = visible;
        SuspendDigging();
        CommitFertilizerUpdate();
    }

    private void AdvanceFertilizerClock()
    {
        if (!foreground) return;
        double now = Time.realtimeSinceStartupAsDouble;
        double delta = Math.Max(0, now - lastFertilizerTick);
        lastFertilizerTick = now;
        if (!state.Fertilizer.SpendBoostTime(delta)) return;
        if (!state.Fertilizer.IsBoosted)
        {
            UpdateManualInterval();
            RefreshUpgradeCards();
        }
        RequestSave(Time.unscaledTimeAsDouble, immediately: !state.Fertilizer.IsBoosted);
        fertilizerView.RefreshTimer(state.Fertilizer);
    }

    private void OnApplicationFocus(bool hasFocus)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (!hasFocus)
        {
            ReleaseHold();
            AdvanceFertilizerClock();
            SaveIfDirty();
        }
#else
        SetForeground(hasFocus && !applicationPaused);
#endif
    }

    private void OnApplicationPause(bool isPaused)
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        applicationPaused = isPaused;
        SetForeground(IsForeground());
#else
        if (isPaused) SetForeground(IsForeground());
#endif
    }

    private void OnDisable()
    {
        SetForeground(false);
        SaveIfDirty();
#if UNITY_WEBGL && !UNITY_EDITOR
        if (webSaveOwner == this)
        {
            OimoDigSetPageVisibilityCallback(null);
            webSaveOwner = null;
        }
#endif
        SuspendDigging();
        if (digWobbleStart.HasValue && potatoVisual != null)
            potatoVisual.localRotation = buriedLocalRotation;
        digWobbleStart = null;
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
