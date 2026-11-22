using System;

internal sealed class FertilizerState
{
    public const double BoostDuration = 60;
    public const double BoostSpeed = 10;
    public static readonly LargeNumber BoostPrice = LargeNumber.From(30);

    public LargeNumber Balance;
    public long CapacityLevel;
    public long RateLevel;
    public double BoostRemaining;
    public long CheckpointUtcTicks;

    public bool IsBoosted => BoostRemaining > 0;
    public LargeNumber Capacity => CapacityAt(CapacityLevel);
    public LargeNumber Rate => RateAt(RateLevel);
    public LargeNumber CapacityPrice => Capacity.Multiply(0.50).Ceiling();
    public LargeNumber RatePrice => Capacity.Multiply(0.75).Ceiling();

    public static LargeNumber CapacityAt(long level) => LargeNumber.Power(1.25, level).Multiply(120);
    public static LargeNumber RateAt(long level) => LargeNumber.Power(1.25, level);

    public void AccrueAwayTime(long utcTicks)
    {
        if (CheckpointUtcTicks != 0 && utcTicks > CheckpointUtcTicks)
        {
            double minutes = (utcTicks - CheckpointUtcTicks) / (double)TimeSpan.TicksPerMinute;
            var total = Balance + Rate.Multiply(minutes);
            Balance = total > Capacity ? Capacity : total;
        }
        Checkpoint(utcTicks);
    }

    public void Checkpoint(long utcTicks) => CheckpointUtcTicks = Math.Max(CheckpointUtcTicks, utcTicks);

    public bool SpendBoostTime(double seconds)
    {
        if (!IsBoosted || seconds <= 0) return false;
        BoostRemaining = Math.Max(0, BoostRemaining - seconds);
        return true;
    }

    public void BuyBoost()
    {
        Balance -= BoostPrice;
        BoostRemaining = BoostDuration;
    }

    public void BuyCapacity()
    {
        Balance -= CapacityPrice;
        CapacityLevel++;
    }

    public void BuyRate()
    {
        Balance -= RatePrice;
        RateLevel++;
    }
}
