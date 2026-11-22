using System;
using System.Collections.Generic;

public enum DigUpgrade { Power, Auto, Critical, Carry, Luck }

internal readonly struct DigBatch
{
    public readonly LargeNumber Earned;
    public readonly int Harvests;
    public readonly int HarvestingStrikes;

    public DigBatch(LargeNumber earned, int harvests, int harvestingStrikes)
    {
        Earned = earned;
        Harvests = harvests;
        HarvestingStrikes = harvestingStrikes;
    }
}

internal readonly struct PendingDig
{
    public readonly LargeNumber Amount;
    public readonly LargeNumber StrikeSize;

    public PendingDig(LargeNumber amount, LargeNumber strikeSize = default)
    {
        Amount = amount;
        StrikeSize = amount > strikeSize ? strikeSize : default;
    }
}

public static class DiggingEconomy
{
    public const int UpgradeCount = 5;
    public const int HitsPerBatch = 96;
    public const double AutoInterval = 0.1;
    public const double CriticalMultiplier = 5;
    public const double PostPrestigeBaseReward = 6;
    public static readonly LargeNumber InitialTarget = LargeNumber.From(10);

    private const int AcceleratedLevelStart = 12;
    private const double LevelAcceleration = 0.03;
    private const double EarlyTargetGrowth = 1.20;
    private const double PostPrestigeTargetGrowth = 1.18;
    private const double EarlyYieldExponent = 0.684;
    private const double PostPrestigeYieldExponent = 0.827;
    private const double LateYieldStart = 130;
    private const double LateYieldExponent = 0.013;

    private static readonly double[] FirstPrices = { 3, 2, 5, 7, 4 };
    private static readonly double[] PriceGrowth = { 2.2, 3.0, 4.9, 5.3, 4.8 };

    public static LargeNumber Price(DigUpgrade upgrade, long level)
        => LevelGrowth(PriceGrowth[(int)upgrade], level).Multiply(FirstPrices[(int)upgrade]).Ceiling();

    public static LargeNumber Power(long level, double prestigeMultiplier)
        => BoostAboveBase(LevelGrowth(2, level), prestigeMultiplier);
    public static LargeNumber AutoHitsPerSecond(long level, double prestigeMultiplier)
        => level == 0 ? default : LevelGrowth(1.85, level - 1).Multiply(0.5 * prestigeMultiplier);
    public static double CriticalChance(long level, double prestigeMultiplier)
        => Rate(level * prestigeMultiplier, 0.05, 0.95);
    public static double CarryRate(long level, double prestigeMultiplier)
        => Rate(level * prestigeMultiplier, 0.25, 0.99);
    public static LargeNumber LuckMultiplier(long level, double prestigeMultiplier)
        => BoostAboveBase(LevelGrowth(1.5, level).Ceiling(), prestigeMultiplier);

    internal static LargeNumber Reward(LargeNumber target, LargeNumber luckMultiplier, bool hasPrestige)
    {
        double depth = Math.Max(0, target.Log10 - InitialTarget.Log10);
        double yieldLog = hasPrestige
            ? Math.Log10(PostPrestigeBaseReward) + PostPrestigeYieldExponent * depth
                + LateYieldExponent * Math.Max(0, depth - LateYieldStart)
            : EarlyYieldExponent * depth;
        return (LargeNumber.FromLog10(yieldLog) * luckMultiplier).Ceiling();
    }

    internal static LargeNumber NextTarget(LargeNumber target, bool hasPrestige)
        => target.Multiply(hasPrestige ? PostPrestigeTargetGrowth : EarlyTargetGrowth).Ceiling();

    private static LargeNumber LevelGrowth(double growth, long level)
    {
        double acceleratedLevels = Math.Max(0, level - AcceleratedLevelStart);
        double effectiveLevel = level + LevelAcceleration * acceleratedLevels * acceleratedLevels;
        // Keep even externally edited levels within the save format's signed exponent range,
        // with room to combine the result with other growth factors.
        return LargeNumber.FromLog10(Math.Min(Math.Log10(growth) * effectiveLevel, long.MaxValue / 32d));
    }

    private static LargeNumber BoostAboveBase(LargeNumber effect, double multiplier)
        => multiplier == 1 ? effect : LargeNumber.One + (effect - LargeNumber.One).Multiply(multiplier);

    private static double Rate(double level, double first, double limit)
        => level == 1 ? first : limit * (1 - Math.Pow(1 - first / limit, level));
}

internal sealed class DiggingProgress
{
    public LargeNumber Wallet;
    public LargeNumber Earned;
    public LargeNumber HarvestCount;
    public LargeNumber Progress;
    public LargeNumber Target = DiggingEconomy.InitialTarget;
    public readonly long[] Levels = new long[DiggingEconomy.UpgradeCount];
    public readonly LinkedList<PendingDig> PendingHits = new();
    public LargeNumber Carry;
    public LargeNumber CycleBest;
    public long PrestigeExponent;
    public long ImofinityCount;
    public readonly FertilizerState Fertilizer = new();
    public double PrestigeMultiplier => DiggingRebirth.Multiplier(PrestigeExponent);
    public LargeNumber ManualPower => DiggingEconomy.Power(Level(DigUpgrade.Power), PrestigeMultiplier);

    public long Level(DigUpgrade upgrade) => Levels[(int)upgrade];
    public bool CanBuy(DigUpgrade upgrade) => Wallet >= DiggingEconomy.Price(upgrade, Level(upgrade));

    public void QueueManualDig(LargeNumber amount) => PendingHits.AddLast(new PendingDig(amount));

    public void QueueAutoDig(LargeNumber strikeSize, LargeNumber strikes)
    {
        if (!strikes.IsZero)
            PendingHits.AddLast(new PendingDig(strikeSize * strikes, strikeSize));
    }

    public bool Buy(DigUpgrade upgrade)
    {
        if (!CanBuy(upgrade)) return false;
        Wallet -= DiggingEconomy.Price(upgrade, Level(upgrade));
        Levels[(int)upgrade]++;
        return true;
    }

    public DigBatch ApplyPending()
    {
        LargeNumber earned = default;
        int harvests = 0;
        int harvestingStrikes = 0;
        var luckMultiplier = DiggingEconomy.LuckMultiplier(Level(DigUpgrade.Luck), PrestigeMultiplier);
        bool hasPrestige = PrestigeExponent >= DiggingRebirth.PrestigeGoalExponent;
        double rate = DiggingEconomy.CarryRate(Level(DigUpgrade.Carry), PrestigeMultiplier);
        for (int i = 0; i < DiggingEconomy.HitsPerBatch; i++)
        {
            if (Carry.IsZero && PendingHits.Count == 0) break;
            bool freshStrike = Carry.IsZero;
            LargeNumber hit = freshStrike ? TakeDigAmount() : Carry;
            Carry = default;
            LargeNumber total = Progress + hit;
            if (total < Target)
            {
                Progress = total;
                continue;
            }

            LargeNumber surplus = total - Target;
            var reward = DiggingEconomy.Reward(Target, luckMultiplier, hasPrestige);
            harvests++;
            if (freshStrike) harvestingStrikes++;
            HarvestCount += LargeNumber.One;
            Wallet += reward;
            Earned += reward;
            if (Earned > CycleBest) CycleBest = Earned;
            earned += reward;
            Progress = default;
            Target = DiggingEconomy.NextTarget(Target, hasPrestige);
            Carry = surplus.Multiply(rate);
        }
        return new DigBatch(earned, harvests, harvestingStrikes);
    }

    private LargeNumber TakeDigAmount()
    {
        var head = PendingHits.First;
        PendingDig work = head.Value;
        LargeNumber needed = Target - Progress;
        if (!work.StrikeSize.IsZero && work.Amount > needed)
        {
            // Skip whole strikes on the same potato. Only the strike that reaches
            // the target can overflow into Carry. Untouched strikes remain queued.
            LargeNumber throughHarvest = (needed / work.StrikeSize).Ceiling() * work.StrikeSize;
            if (throughHarvest < needed) throughHarvest = needed;
            LargeNumber remaining = work.Amount - throughHarvest;
            if (!remaining.IsZero)
            {
                head.Value = new PendingDig(remaining, work.StrikeSize);
                return throughHarvest;
            }
        }

        PendingHits.RemoveFirst();
        return work.Amount;
    }

    public void ResetRun(long initialLevel)
    {
        Wallet = Earned = Progress = Carry = default;
        Target = DiggingEconomy.InitialTarget;
        PendingHits.Clear();
        Array.Fill(Levels, initialLevel);
    }
}
