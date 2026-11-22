using System.Globalization;

internal static class Program
{
    private const int FramesPerSecond = 60;
    private const int ManualPeriod = FramesPerSecond / 4;
    private const int IndividualCriticalRolls = 64;
    private static readonly int[] SampleMinutes = { 5, 15, 30, 60, 90, 120 };

    private static void Main(string[] args)
    {
        int minutes = 360;
        if (args.Length > 1 || (args.Length == 1 && (!int.TryParse(args[0], out minutes) || minutes < 1 || minutes > 1440)))
        {
            Console.Error.WriteLine("Usage: dotnet run --project Tools/Progression -- [minutes: 1-1440]");
            Environment.ExitCode = 1;
            return;
        }

        foreach (bool previous in new[] { true, false })
        {
            Console.WriteLine(previous ? "\nPrevious effects" : "\nAdjusted effects");
            Run("Power + Auto priority", new[] { 1, 1, 3, 3, 3 }, previous, minutes);
            Run("Balanced", new[] { 1, 1, 1, 1, 1 }, previous, minutes);
            Run("Luck priority", new[] { 3, 3, 3, 3, 1 }, previous, minutes);
        }
    }

    private static void Run(string route, int[] weights, bool previous, int minutes)
    {
        var state = new DiggingProgress();
        var random = new Random(42);
        int autoPeriod = (int)Math.Round(DiggingEconomy.AutoInterval * FramesPerSecond);
        int nextAuto = -1;
        int nextManual = 0;
        int presetFrame = -1;
        int sampleIndex = 0;
        int waitingSince = 0;
        var purchaseWaits = new List<int>();
        var selected = SelectUpgrade(state, weights);
        Console.WriteLine($"\n[{route}]");
        Console.WriteLine("Event,Time,Upgrade,New level,Paid,Wait since prior purchase,Run earned");

        int lastFrame = minutes * 60 * FramesPerSecond;
        for (int frame = 0; frame <= lastFrame; frame++)
        {
            if (frame == nextManual)
            {
                double chance = DiggingEconomy.CriticalChance(state.Level(DigUpgrade.Critical), state.PrestigeMultiplier);
                state.QueueManualDig(RollPower(Power(state, previous), chance, random));
                nextManual += ManualPeriod;
            }
            if (frame == nextAuto)
            {
                var count = AutoRate(state, previous).Multiply(DiggingEconomy.AutoInterval);
                double chance = DiggingEconomy.CriticalChance(state.Level(DigUpgrade.Critical), state.PrestigeMultiplier);
                QueueAuto(state, Power(state, previous), count, chance, random);
                nextAuto += autoPeriod;
            }

            if (state.PendingHits.Count > 0 || !state.Carry.IsZero)
                state.ApplyPending();

            if (presetFrame < 0 && DiggingRebirth.TryExecute(RebirthKind.Prestige, state))
            {
                ReportWaits("Before IMOPRESTAGE", purchaseWaits);
                Console.WriteLine($"IMOPRESTAGE,{Clock(frame)}");
                presetFrame = frame;
                waitingSince = frame;
                purchaseWaits.Clear();
                nextAuto = -1;
                nextManual = frame + 1;
                selected = SelectUpgrade(state, weights);
            }
            else if (DiggingRebirth.CanUse(RebirthKind.Imofinity, state))
            {
                ReportWaits("After IMOPRESTAGE", purchaseWaits);
                Console.WriteLine($"IMOFINITY,{Clock(frame)},elapsed after IMOPRESTAGE,{Clock(frame - presetFrame)}");
                return;
            }
            else if (state.CanBuy(selected))
            {
                var price = DiggingEconomy.Price(selected, state.Level(selected));
                if (!state.Buy(selected))
                    throw new InvalidOperationException("Purchase no longer affordable.");
                if (selected == DigUpgrade.Auto && nextAuto < 0)
                    nextAuto = frame + autoPeriod;
                int wait = frame - waitingSince;
                purchaseWaits.Add(wait);
                Console.WriteLine($"Purchase,{Clock(frame)},{selected},{state.Level(selected)},{price.Format()},{Clock(wait)},{state.Earned.Format()}");
                waitingSince = frame;
                selected = SelectUpgrade(state, weights);
            }

            if (sampleIndex < SampleMinutes.Length && frame == SampleMinutes[sampleIndex] * 60 * FramesPerSecond)
            {
                var nextPrice = DiggingEconomy.Price(selected, state.Level(selected));
                Console.WriteLine($"Checkpoint,{Clock(frame)},run earned,{state.Earned.Format()},wallet,{state.Wallet.Format()},next,{selected},price,{nextPrice.Format()}");
                sampleIndex++;
            }
        }

        ReportWaits(presetFrame < 0 ? "Before IMOPRESTAGE (partial)" : "After IMOPRESTAGE (partial)", purchaseWaits);
        var shortage = DiggingEconomy.Price(selected, state.Level(selected)) - state.Wallet;
        Console.WriteLine($"Time limit,{Clock(lastFrame)},run earned,{state.Earned.Format()},next,{selected},shortage,{shortage.Format()},current wait,{Clock(lastFrame - waitingSince)}");
    }

    private static DigUpgrade SelectUpgrade(DiggingProgress state, int[] weights)
    {
        var selected = DigUpgrade.Power;
        var bestScore = DiggingEconomy.Price(selected, state.Level(selected)).Multiply(weights[0]);
        for (int i = 1; i < weights.Length; i++)
        {
            var candidate = (DigUpgrade)i;
            long level = state.Level(candidate);
            double prestige = state.PrestigeMultiplier;
            if (candidate == DigUpgrade.Critical &&
                DiggingEconomy.CriticalChance(level + 1, prestige) <= DiggingEconomy.CriticalChance(level, prestige))
                continue;
            if (candidate == DigUpgrade.Carry &&
                DiggingEconomy.CarryRate(level + 1, prestige) <= DiggingEconomy.CarryRate(level, prestige))
                continue;

            var score = DiggingEconomy.Price(candidate, level).Multiply(weights[i]);
            if (score >= bestScore)
                continue;
            selected = candidate;
            bestScore = score;
        }
        return selected;
    }

    private static LargeNumber Power(DiggingProgress state, bool previous)
    {
        if (!previous)
            return state.ManualPower;
        var effect = PreviousGrowth(2, state.Level(DigUpgrade.Power));
        return LargeNumber.One + (effect - LargeNumber.One).Multiply(state.PrestigeMultiplier);
    }

    private static LargeNumber AutoRate(DiggingProgress state, bool previous)
    {
        long level = state.Level(DigUpgrade.Auto);
        if (level == 0)
            return default;
        return previous
            ? PreviousGrowth(1.85, level - 1).Multiply(0.5 * state.PrestigeMultiplier)
            : DiggingEconomy.AutoHitsPerSecond(level, state.PrestigeMultiplier);
    }

    private static LargeNumber PreviousGrowth(double growth, long level)
    {
        double accelerated = Math.Max(0, level - 12);
        return LargeNumber.FromLog10(Math.Log10(growth) * (level + 0.03 * accelerated * accelerated));
    }

    private static LargeNumber RollPower(LargeNumber power, double chance, Random random)
        => chance > 0 && random.NextDouble() < chance ? power.Multiply(DiggingEconomy.CriticalMultiplier) : power;

    private static void QueueAuto(DiggingProgress state, LargeNumber power, LargeNumber count, double chance, Random random)
    {
        if (chance <= 0)
        {
            state.QueueAutoDig(power, count);
            return;
        }

        var whole = count.Floor();
        var fraction = count - whole;
        for (int i = 0; i < IndividualCriticalRolls && !whole.IsZero; i++)
        {
            state.QueueAutoDig(RollPower(power, chance, random), LargeNumber.One);
            whole -= LargeNumber.One;
        }
        if (!whole.IsZero)
        {
            var critical = (whole.Multiply(chance) + LargeNumber.From(random.NextDouble())).Floor();
            var normal = whole - critical;
            var criticalPower = power.Multiply(DiggingEconomy.CriticalMultiplier);
            if (random.NextDouble() < 0.5)
            {
                state.QueueAutoDig(criticalPower, critical);
                state.QueueAutoDig(power, normal);
            }
            else
            {
                state.QueueAutoDig(power, normal);
                state.QueueAutoDig(criticalPower, critical);
            }
        }
        if (!fraction.IsZero)
            state.QueueAutoDig(RollPower(power, chance, random), fraction);
    }

    private static void ReportWaits(string stage, List<int> waits)
    {
        if (waits.Count == 0)
            return;
        waits.Sort();
        Console.WriteLine($"{stage},purchases,{waits.Count},median wait,{Clock(waits[waits.Count / 2])}," +
            $"longest completed wait,{Clock(waits[^1])}");
    }

    private static string Clock(int frames)
    {
        var elapsed = TimeSpan.FromSeconds((double)frames / FramesPerSecond);
        return ((int)elapsed.TotalHours).ToString("00", CultureInfo.InvariantCulture) +
            elapsed.ToString(@"\:mm\:ss\.ff", CultureInfo.InvariantCulture);
    }
}
