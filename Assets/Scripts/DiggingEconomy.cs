using System;
using System.Collections.Generic;

public enum DigUpgrade { Power, Auto, Critical, Carry, Luck }

internal readonly struct DigUpgradeQuote
{
    public readonly long Count;
    public readonly LargeNumber Cost;

    public DigUpgradeQuote(long count, LargeNumber cost)
    {
        Count = count;
        Cost = cost;
    }
}

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
    internal const long MaximumUpgradeLevel = long.MaxValue / 4;
    public const double AutoInterval = 0.1;
    public const double CriticalMultiplier = 5;
    public const double PostPrestigeBaseReward = 6;
    public static readonly LargeNumber InitialTarget = LargeNumber.From(10);

    private const int AcceleratedLevelStart = 12;
    private const double PriceAndLuckAcceleration = 0.03;
    private const int DominantPriceLevel = 1024;
    private const double DiggingEffectAcceleration = 0.0155;
    private const double EarlyTargetGrowth = 1.20;
    private const double PostPrestigeTargetGrowth = 1.18;
    private const double EarlyYieldExponent = 0.684;
    private const double PostPrestigeYieldExponent = 0.827;
    private const double LateYieldStart = 130;
    private const double LateYieldExponent = 0.013;

    private static readonly double[] FirstPrices = { 3, 2, 5, 7, 4 };
    private static readonly double[] PriceGrowth = { 2.2, 3.0, 4.9, 5.3, 4.8 };

    public static LargeNumber Price(DigUpgrade upgrade, long level)
        => LevelGrowth(PriceGrowth[(int)upgrade], level, PriceAndLuckAcceleration)
            .Multiply(FirstPrices[(int)upgrade]).Ceiling();

    internal static DigUpgradeQuote Quote(DigUpgrade upgrade, long level, LargeNumber wallet, bool maximum)
    {
        if (level >= MaximumUpgradeLevel)
            return default;

        var firstPrice = Price(upgrade, level);
        if (firstPrice > wallet)
            return default;
        if (!maximum)
            return new DigUpgradeQuote(1, firstPrice);

        long end = level;
        LargeNumber cost = default;
        for (; end < DominantPriceLevel; end++)
        {
            var total = cost + Price(upgrade, end);
            if (total > wallet)
                return new DigUpgradeQuote(end - level, cost);
            cost = total;
        }

        // Beyond this point even the slowest price curve grows by over 20 decimal
        // orders per level until it flattens. LargeNumber discards the preceding
        // subtotal at each such step, so the last rising price is the subtotal.
        var flatPrice = Price(upgrade, MaximumUpgradeLevel - 1);
        long risingEnd = FirstPriceAtLeast(upgrade, end, flatPrice, inclusive: true);
        if (wallet < flatPrice)
        {
            long affordableEnd = FirstPriceAtLeast(upgrade, end, wallet, inclusive: false);
            return affordableEnd == end ? new DigUpgradeQuote(end - level, cost)
                : new DigUpgradeQuote(affordableEnd - level, Price(upgrade, affordableEnd - 1));
        }

        if (risingEnd > end)
            cost = Price(upgrade, risingEnd - 1);

        long low = 0;
        long high = MaximumUpgradeLevel - risingEnd;
        while (low < high)
        {
            long count = low + (high - low + 1) / 2;
            if (cost + flatPrice.Multiply(count) <= wallet)
                low = count;
            else
                high = count - 1;
        }
        return new DigUpgradeQuote(risingEnd - level + low, cost + flatPrice.Multiply(low));
    }

    private static long FirstPriceAtLeast(DigUpgrade upgrade, long level, LargeNumber limit, bool inclusive)
    {
        long high = MaximumUpgradeLevel;
        while (level < high)
        {
            long mid = level + (high - level) / 2;
            var price = Price(upgrade, mid);
            if (inclusive ? price >= limit : price > limit)
                high = mid;
            else
                level = mid + 1;
        }
        return level;
    }

    public static LargeNumber Power(long level, double prestigeMultiplier)
        => BoostAboveBase(LevelGrowth(2, level, DiggingEffectAcceleration), prestigeMultiplier);
    public static LargeNumber AutoHitsPerSecond(long level, double prestigeMultiplier)
        => level == 0 ? default :
            LevelGrowth(1.85, level - 1, DiggingEffectAcceleration).Multiply(0.5 * prestigeMultiplier);
    public static double CriticalChance(long level, double prestigeMultiplier)
        => Rate(level * prestigeMultiplier, 0.05, 0.95);
    public static double CarryRate(long level, double prestigeMultiplier)
        => Rate(level * prestigeMultiplier, 0.25, 0.99);
    public static LargeNumber LuckMultiplier(long level, double prestigeMultiplier)
        => BoostAboveBase(LevelGrowth(1.5, level, PriceAndLuckAcceleration).Ceiling(), prestigeMultiplier);

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

    private static LargeNumber LevelGrowth(double growth, long level, double acceleration)
    {
        double acceleratedLevels = Math.Max(0, level - AcceleratedLevelStart);
        double effectiveLevel = level + acceleration * acceleratedLevels * acceleratedLevels;
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
    public bool CanBuy(DigUpgrade upgrade) => Quote(upgrade, false).Count > 0;

    internal DigUpgradeQuote Quote(DigUpgrade upgrade, bool maximum)
        => DiggingEconomy.Quote(upgrade, Level(upgrade), Wallet, maximum);

    public void QueueManualDig(LargeNumber amount) => PendingHits.AddLast(new PendingDig(amount));

    public void QueueAutoDig(LargeNumber strikeSize, LargeNumber strikes)
    {
        if (!strikes.IsZero)
            PendingHits.AddLast(new PendingDig(strikeSize * strikes, strikeSize));
    }

    public bool Buy(DigUpgrade upgrade, bool maximum = false)
    {
        var quote = Quote(upgrade, maximum);
        if (quote.Count == 0) return false;
        Wallet -= quote.Cost;
        Levels[(int)upgrade] += quote.Count;
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
