using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class FertilizerView : MonoBehaviour
{
    [SerializeField] private Button openButton;
    [SerializeField] private TMP_Text balanceLabel;
    [SerializeField] private TMP_Text timerLabel;
    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text panelBalance;
    [SerializeField] private TMP_Text boostDetails;
    [SerializeField] private TMP_Text capacityDetails;
    [SerializeField] private TMP_Text rateDetails;
    [SerializeField] private Button boostButton;
    [SerializeField] private Button capacityButton;
    [SerializeField] private Button rateButton;
    [SerializeField] private Button closeButton;

    private int shownSeconds = -1;
    internal bool IsOpen => panel.activeSelf;

    internal void Connect(DiggingPrototype digging)
    {
        openButton.onClick.AddListener(digging.OpenFertilizer);
        boostButton.onClick.AddListener(digging.BuyFertilizerBoost);
        capacityButton.onClick.AddListener(digging.BuyFertilizerCapacity);
        rateButton.onClick.AddListener(digging.BuyFertilizerRate);
        closeButton.onClick.AddListener(Close);
        Close();
    }

    internal void Open(FertilizerState state)
    {
        panel.SetActive(true);
        Refresh(state);
        closeButton.Select();
    }

    internal void Close() => panel.SetActive(false);

    internal void Refresh(FertilizerState state)
    {
        balanceLabel.text = $"肥料 {state.Balance.Format()} / {state.Capacity.Format()}";
        panelBalance.text = balanceLabel.text;
        capacityDetails.text = $"容量  {state.Capacity.Format()} → {FertilizerState.CapacityAt(state.CapacityLevel + 1).Format()}\n価格  肥料 {state.CapacityPrice.Format()}";
        rateDetails.text = $"離席獲得  {state.Rate.Format()} → {FertilizerState.RateAt(state.RateLevel + 1).Format()} / 分\n価格  肥料 {state.RatePrice.Format()}";
        capacityButton.interactable = state.Balance >= state.CapacityPrice;
        rateButton.interactable = state.Balance >= state.RatePrice;
        shownSeconds = -1;
        RefreshTimer(state);
    }

    internal void RefreshTimer(FertilizerState state)
    {
        int seconds = (int)Math.Ceiling(state.BoostRemaining);
        if (seconds == shownSeconds) return;
        shownSeconds = seconds;
        timerLabel.text = seconds > 0 ? $"10倍速  {seconds}秒" : "肥料を使う";
        boostDetails.text = $"掘削速度  ×{FertilizerState.BoostSpeed:0} / {FertilizerState.BoostDuration:0}秒\n"
            + (seconds > 0 ? $"効果中  {seconds}秒" : $"価格  肥料 {FertilizerState.BoostPrice.Format()}");
        boostButton.interactable = seconds == 0 && state.Balance >= FertilizerState.BoostPrice;
    }
}
