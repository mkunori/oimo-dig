using System;

internal enum RebirthKind { Prestige, Imofinity }

internal static class DiggingRebirth
{
    public const int PrestigeGoalExponent = 9;
    public const int ImofinityGoalExponent = 308;
    private static readonly LargeNumber PrestigeGoal = LargeNumber.PowerOfTen(PrestigeGoalExponent);
    private static readonly LargeNumber ImofinityGoal = LargeNumber.PowerOfTen(ImofinityGoalExponent);

    public static bool CanUse(RebirthKind kind, DiggingProgress state)
        => state.Earned >= (kind == RebirthKind.Prestige ? PrestigeGoal : ImofinityGoal);

    public static long NextPrestigeExponent(DiggingProgress state)
        => Math.Max(state.PrestigeExponent, state.Earned.Exponent);

    public static double Multiplier(long exponent)
        => exponent < PrestigeGoalExponent ? 1 : 1 + (exponent - 8) / 10.0;

    public static float GoalFraction(LargeNumber value)
        => (float)Math.Clamp(value.Log10 / ImofinityGoalExponent, 0, 1);

    public static long InitialLevel(long imofinityCount)
    {
        if (imofinityCount == 0) return 0;
        // Levels share a signed 64-bit representation with the normal upgrade system.
        const long max = long.MaxValue / 4;
        decimal level = 5;
        for (long i = 1; i < imofinityCount && level < max; i++) level *= 1.5m;
        return level >= max ? max : (long)decimal.Floor(level);
    }

    public static bool TryExecute(RebirthKind kind, DiggingProgress state)
    {
        if (!CanUse(kind, state)) return false;
        if (kind == RebirthKind.Prestige)
        {
            state.PrestigeExponent = NextPrestigeExponent(state);
        }
        else
        {
            state.ImofinityCount++;
            state.PrestigeExponent = 0;
            state.CycleBest = default;
        }

        state.ResetRun(InitialLevel(state.ImofinityCount));
        return true;
    }
}
