using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class RebirthView : MonoBehaviour
{
    [SerializeField] private TMP_Text goalValue;
    [SerializeField] private Slider goalGauge;
    [SerializeField] private RectTransform cycleBestMarker;
    [SerializeField] private Button prestigeButton;
    [SerializeField] private TMP_Text prestigeLabel;
    [SerializeField] private Button imofinityButton;
    [SerializeField] private GameObject confirmation;
    [SerializeField] private TMP_Text confirmationTitle;
    [SerializeField] private TMP_Text confirmationBody;
    [SerializeField] private Button cancelButton;
    [SerializeField] private Button confirmButton;

    private RebirthKind selection;
    public bool IsOpen => confirmation.activeSelf;

    internal void Connect(DiggingPrototype digging)
    {
        prestigeButton.onClick.AddListener(() => digging.OpenRebirth(RebirthKind.Prestige));
        imofinityButton.onClick.AddListener(() => digging.OpenRebirth(RebirthKind.Imofinity));
        cancelButton.onClick.AddListener(Close);
        confirmButton.onClick.AddListener(() => digging.ConfirmRebirth(selection));
        Close();
    }

    internal void Refresh(DiggingProgress state)
    {
        bool imofinityReady = DiggingRebirth.CanUse(RebirthKind.Imofinity, state);
        string earned = state.Earned.Format();
        if (!imofinityReady && state.Earned.Exponent == 307 && earned == "10e307")
            earned = "9.99…e307";
        goalValue.text = earned;
        goalGauge.SetValueWithoutNotify(DiggingRebirth.GoalFraction(state.Earned));
        float best = DiggingRebirth.GoalFraction(state.CycleBest);
        cycleBestMarker.anchorMin = new Vector2(best, 0);
        cycleBestMarker.anchorMax = new Vector2(best, 1);
        cycleBestMarker.gameObject.SetActive(!state.CycleBest.IsZero);
        prestigeButton.interactable = DiggingRebirth.CanUse(RebirthKind.Prestige, state);
        string prestigeStatus = prestigeButton.interactable
            ? FormatMultiplier(state.PrestigeMultiplier) + " → " +
                FormatMultiplier(DiggingRebirth.Multiplier(DiggingRebirth.NextPrestigeExponent(state)))
            : $"必要  1e{DiggingRebirth.PrestigeGoalExponent}";
        prestigeLabel.text = $"IMOPRESTAGE\n<size=85%>{prestigeStatus}</size>";
        imofinityButton.gameObject.SetActive(imofinityReady);
    }

    internal void Open(RebirthKind kind, DiggingProgress state)
    {
        selection = kind;
        bool prestige = kind == RebirthKind.Prestige;
        long initialLevel = DiggingRebirth.InitialLevel(state.ImofinityCount + (prestige ? 0 : 1));
        double multiplier = prestige ? DiggingRebirth.Multiplier(DiggingRebirth.NextPrestigeExponent(state)) : 1;
        confirmationTitle.text = prestige ? "イモプレステージ" : "イモフィニティ";
        string reset = "初期化：所持芋・今周累計・掘削進捗・必要量・通常強化";
        if (!prestige) reset += "・最高到達点";
        string effect = "";
        if (!prestige)
            effect = $"\nイモフィニティ回数：{state.ImofinityCount + 1}";
        else if (state.PrestigeExponent == 0)
            effect = $"\n幸運掘Lv.0の初回収穫：1個 → {DiggingEconomy.PostPrestigeBaseReward:0}個";
        confirmationBody.text = $"{reset}\n\n倍率：{FormatMultiplier(state.PrestigeMultiplier)} → {FormatMultiplier(multiplier)}" +
            $"\n開始時の通常強化：Lv.{initialLevel}" + effect;
        confirmation.SetActive(true);
        cancelButton.Select();
    }

    internal void Close() => confirmation.SetActive(false);

    private static string FormatMultiplier(double value) => "×" + value.ToString("0.00", CultureInfo.InvariantCulture);
}
