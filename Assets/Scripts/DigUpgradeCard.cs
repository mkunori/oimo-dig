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

    public void Refresh(long level, bool affordable, double prestigeMultiplier)
    {
        title.text = $"{Names[(int)upgrade]}  Lv.{level}";
        details.text = $"{Effect(level, prestigeMultiplier)} → {Effect(level + 1, prestigeMultiplier)}\n価格  芋 {DiggingEconomy.Price(upgrade, level).Format()}個";
        purchase.interactable = affordable;
        purchaseLabel.text = affordable ? "購入" : "芋が不足";
    }

    private string Effect(long level, double multiplier)
    {
        switch (upgrade)
        {
            case DigUpgrade.Power: return DiggingEconomy.Power(level, multiplier).Format() + " / 回";
            case DigUpgrade.Auto: return DiggingEconomy.AutoPerSecond(level, multiplier).Format() + " / 秒";
            case DigUpgrade.Critical: return Percent(DiggingEconomy.CriticalChance(level, multiplier)) + " (5x)";
            case DigUpgrade.Carry: return Percent(DiggingEconomy.CarryRate(level, multiplier));
            default: return "×" + DiggingEconomy.LuckMultiplier(level, multiplier).Format();
        }
    }

    private static string Percent(double rate) => LargeNumber.From(rate * 100).Format() + "%";
}
