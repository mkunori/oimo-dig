using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class DigUpgradeCard : MonoBehaviour
{
    [SerializeField] private DigUpgrade upgrade;
    [SerializeField] private TMP_Text title;
    [SerializeField] private TMP_Text details;
    [SerializeField] private Button purchase;
    [SerializeField] private TMP_Text purchaseLabel;

    private static readonly string[] Names = { "掘力", "自動掘", "会心掘", "貫通掘", "幸運掘" };
    public DigUpgrade Upgrade => upgrade;

    public void Connect(DiggingPrototype digging)
    {
        purchase.onClick.AddListener(() => digging.BuyUpgrade(upgrade));
    }

    internal void Refresh(long level, DigUpgradeQuote quote, double prestigeMultiplier, LargeNumber manualPower,
        double autoSpeedMultiplier)
    {
        bool affordable = quote.Count > 0;
        long purchasedLevel = level + quote.Count;
        long previewLevel = affordable ? purchasedLevel : System.Math.Min(level + 1, DiggingEconomy.MaximumUpgradeLevel);
        title.text = $"{Names[(int)upgrade]}  Lv.{level}";
        string effect = upgrade == DigUpgrade.Auto
            ? AutoEffect(level, previewLevel, prestigeMultiplier, manualPower, autoSpeedMultiplier)
            : $"{Effect(level, prestigeMultiplier)} → {Effect(previewLevel, prestigeMultiplier)}";
        string price = affordable ? $"価格  芋 {quote.Cost.Format()}個"
            : $"必要  芋 {DiggingEconomy.Price(upgrade, level).Format()}個";
        details.text = $"+{quote.Count} → Lv.{purchasedLevel}\n{price}\n{effect}";
        purchase.interactable = affordable;
        purchaseLabel.text = affordable ? "購入" : "芋が不足";
    }

    private string Effect(long level, double multiplier)
    {
        switch (upgrade)
        {
            case DigUpgrade.Power: return DiggingEconomy.Power(level, multiplier).Format() + " / 回";
            case DigUpgrade.Critical: return Percent(DiggingEconomy.CriticalChance(level, multiplier)) + " (5x)";
            case DigUpgrade.Carry: return Percent(DiggingEconomy.CarryRate(level, multiplier));
            default: return "×" + DiggingEconomy.LuckMultiplier(level, multiplier).Format();
        }
    }

    private static string AutoEffect(long level, long previewLevel, double prestigeMultiplier, LargeNumber power,
        double speedMultiplier)
    {
        var speed = DiggingEconomy.AutoHitsPerSecond(level, prestigeMultiplier).Multiply(speedMultiplier);
        var nextSpeed = DiggingEconomy.AutoHitsPerSecond(previewLevel, prestigeMultiplier).Multiply(speedMultiplier);
        return $"自動掘り速度  {speed.FormatRate()} → {nextSpeed.FormatRate()}回/秒\n" +
            $"自動掘削量(通常)  {(power * speed).FormatRate()} → {(power * nextSpeed).FormatRate()}/秒";
    }

    private static string Percent(double rate) => LargeNumber.From(rate * 100).Format() + "%";
}
