using System;

internal static class DigUpgradeHint
{
    private const double SavingHarvestThreshold = 5;

    internal static string Describe(DiggingProgress state, double stalledSeconds, double manualHitsPerSecond)
    {
        if (Array.TrueForAll(state.Levels, level => level == 0))
            return "";

        var remaining = state.Target - state.Progress;
        double prestige = state.PrestigeMultiplier;
        long powerLevel = state.Level(DigUpgrade.Power);
        long autoLevel = state.Level(DigUpgrade.Auto);
        var power = state.ManualPower;
        var autoRate = DiggingEconomy.AutoHitsPerSecond(autoLevel, prestige);
        var totalRate = LargeNumber.From(manualHitsPerSecond) + autoRate;
        var seconds = remaining / (power * totalRate);
        var powerPrice = DiggingEconomy.Price(DigUpgrade.Power, powerLevel);
        var autoPrice = DiggingEconomy.Price(DigUpgrade.Auto, autoLevel);

        string hint = "";
        bool affordable = false;
        if (seconds >= LargeNumber.From(stalledSeconds))
        {
            var powerSeconds = remaining / (DiggingEconomy.Power(powerLevel + 1, prestige) * totalRate);
            var nextAutoRate = DiggingEconomy.AutoHitsPerSecond(autoLevel + 1, prestige);
            var autoSeconds = remaining / (power * (LargeNumber.From(manualHitsPerSecond) + nextAutoRate));
            bool canBuyPower = state.CanBuy(DigUpgrade.Power) && powerSeconds < seconds;
            bool canBuyAuto = state.CanBuy(DigUpgrade.Auto) && autoSeconds < seconds;
            bool usePower = canBuyPower != canBuyAuto ? canBuyPower
                : powerSeconds < autoSeconds || (powerSeconds <= autoSeconds && powerPrice <= autoPrice);
            var nextSeconds = usePower ? powerSeconds : autoSeconds;
            if (nextSeconds < seconds)
            {
                affordable = usePower ? canBuyPower : canBuyAuto;
                string name = usePower ? "掘力" : "自動掘";
                var price = usePower ? powerPrice : autoPrice;
                hint = $"{PurchaseStatus(name, price, state.Wallet, affordable)}\n" +
                    $"通常時  {seconds.Ceiling().Format()}秒 → {nextSeconds.Ceiling().Format()}秒";
            }
        }

        if (affordable)
            return hint;

        var savingGoal = powerPrice <= autoPrice ? powerPrice : autoPrice;
        string luckHint = DescribeLuck(state, savingGoal);
        return luckHint.Length > 0 && (hint.Length == 0 || state.CanBuy(DigUpgrade.Luck)) ? luckHint : hint;
    }

    private static string DescribeLuck(DiggingProgress state, LargeNumber savingGoal)
    {
        long level = state.Level(DigUpgrade.Luck);
        double prestige = state.PrestigeMultiplier;
        bool hasPrestige = state.PrestigeExponent >= DiggingRebirth.PrestigeGoalExponent;
        var reward = DiggingEconomy.Reward(state.Target, DiggingEconomy.LuckMultiplier(level, prestige), hasPrestige);
        var harvests = ((savingGoal - state.Wallet) / reward).Ceiling();
        if (harvests < LargeNumber.From(SavingHarvestThreshold))
            return "";

        var nextReward = DiggingEconomy.Reward(state.Target, DiggingEconomy.LuckMultiplier(level + 1, prestige), hasPrestige);
        if (nextReward <= reward)
            return "";

        // Estimate both paths at the current yield, including the cost of buying Luck first.
        var price = DiggingEconomy.Price(DigUpgrade.Luck, level);
        var harvestsToBuy = ((price - state.Wallet) / reward).Ceiling();
        var walletAfterBuy = (state.Wallet + harvestsToBuy * reward) - price;
        var harvestsWithLuck = harvestsToBuy + ((savingGoal - walletAfterBuy) / nextReward).Ceiling();
        if (harvestsWithLuck >= harvests)
            return "";

        return $"{PurchaseStatus("幸運掘", price, state.Wallet, state.CanBuy(DigUpgrade.Luck))}\n" +
            $"次の収穫  {reward.Format()}個 → {nextReward.Format()}個";
    }

    private static string PurchaseStatus(string name, LargeNumber price, LargeNumber wallet, bool affordable)
        => affordable ? $"{name}  購入" : $"{name}  不足 {(price - wallet).Format()}芋";
}
