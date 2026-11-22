using System;
using UnityEngine;

internal static class DiggingSave
{
    private const string OldKey = "OimoDig.PrototypeSave";
    private const string StageOneKey = "OimoDig.PrototypeSave.v2";
    private const string StageTwoKey = "OimoDig.PrototypeSave.v3";
    private const string Key = "OimoDig.PrototypeSave.v4";
    private const string IsolatedSuffix = ".RebirthVerification";
    private static string SaveKey
    {
        get
        {
#if UNITY_EDITOR
            if (RebirthVerification.IsEnabled) return Key + IsolatedSuffix;
#endif
            return Key;
        }
    }

    [Serializable]
    private sealed class Legacy
    {
        public int version;
        public int ownedPotatoes = -1;
        public int harvestedCount = -1;
        public int shovelLevel = -1;
        public int autoDigPurchased = -1;
        public int digProgress = -1;
        public int requiredDigAmount = -1;
    }

    [Serializable]
    private sealed class Snapshot
    {
        public int version;
        public string wallet, earned, harvestCount, progress, target, carry;
        public long[] levels;
        public string[] pendingHits;
        public string[] pendingHitStrikeSizes;
        public string cycleBest;
        public long prestigeExponent, imofinityCount;
        public string fertilizerBalance;
        public long fertilizerCapacityLevel, fertilizerRateLevel, fertilizerUtcTicks;
        public double boostRemaining;
    }

    public static bool TryLoad(out DiggingProgress state)
    {
        state = new DiggingProgress();
        var loaded = new DiggingProgress();
        try
        {
            string saveKey = SaveKey;
            bool normalSave = saveKey == Key;
            string previousKey = normalSave ? StageTwoKey : StageTwoKey + IsolatedSuffix;
            string snapshotKey = PlayerPrefs.HasKey(saveKey) ? saveKey :
                PlayerPrefs.HasKey(previousKey) ? previousKey :
                normalSave && PlayerPrefs.HasKey(StageOneKey) ? StageOneKey : null;
            if (snapshotKey != null)
            {
                var saved = JsonUtility.FromJson<Snapshot>(PlayerPrefs.GetString(snapshotKey));
                int expectedVersion = snapshotKey == saveKey ? 4 : snapshotKey == previousKey ? 3 : 2;
                if (saved == null || saved.version != expectedVersion || saved.levels == null ||
                    saved.levels.Length != DiggingEconomy.UpgradeCount ||
                    Array.Exists(saved.levels, level => level < 0 || level > DiggingEconomy.MaximumUpgradeLevel) ||
                    !LargeNumber.TryDecode(saved.wallet, out loaded.Wallet) ||
                    !LargeNumber.TryDecode(saved.earned, out loaded.Earned) ||
                    !LargeNumber.TryDecode(saved.harvestCount, out loaded.HarvestCount) ||
                    !LargeNumber.TryDecode(saved.progress, out loaded.Progress) ||
                    !LargeNumber.TryDecode(saved.target, out loaded.Target) ||
                    !LargeNumber.TryDecode(saved.carry, out loaded.Carry) ||
                    loaded.Wallet > loaded.Earned ||
                    loaded.Target < DiggingEconomy.InitialTarget || loaded.Progress >= loaded.Target ||
                    saved.pendingHits == null)
                    return false;

                Array.Copy(saved.levels, loaded.Levels, saved.levels.Length);
                string[] strikeSizes = saved.pendingHitStrikeSizes;
                bool hasStrikeSizes = strikeSizes != null && strikeSizes.Length > 0;
                if (hasStrikeSizes && strikeSizes.Length != saved.pendingHits.Length)
                    return false;
                for (int i = 0; i < saved.pendingHits.Length; i++)
                {
                    if (!LargeNumber.TryDecode(saved.pendingHits[i], out LargeNumber hit) || hit.IsZero)
                        return false;
                    LargeNumber strikeSize = default;
                    if (hasStrikeSizes && (!LargeNumber.TryDecode(strikeSizes[i], out strikeSize) || strikeSize >= hit))
                        return false;
                    loaded.PendingHits.AddLast(new PendingDig(hit, strikeSize));
                }

                if (expectedVersion == 2)
                {
                    loaded.CycleBest = loaded.Earned;
                }
                else
                {
                    if (!LargeNumber.TryDecode(saved.cycleBest, out loaded.CycleBest) ||
                        loaded.CycleBest < loaded.Earned || saved.imofinityCount < 0 ||
                        saved.imofinityCount >= long.MaxValue ||
                        (saved.prestigeExponent != 0 && (saved.prestigeExponent < DiggingRebirth.PrestigeGoalExponent ||
                            loaded.CycleBest.IsZero || saved.prestigeExponent > loaded.CycleBest.Exponent)))
                        return false;
                    loaded.PrestigeExponent = saved.prestigeExponent;
                    loaded.ImofinityCount = saved.imofinityCount;
                }

                if (expectedVersion == 4)
                {
                    var fertilizer = loaded.Fertilizer;
                    if (!LargeNumber.TryDecode(saved.fertilizerBalance, out fertilizer.Balance) ||
                        saved.fertilizerCapacityLevel < 0 || saved.fertilizerCapacityLevel > long.MaxValue / 4 ||
                        saved.fertilizerRateLevel < 0 || saved.fertilizerRateLevel > long.MaxValue / 4 ||
                        saved.fertilizerUtcTicks <= 0 || saved.fertilizerUtcTicks > DateTime.MaxValue.Ticks ||
                        !(saved.boostRemaining >= 0 && saved.boostRemaining <= FertilizerState.BoostDuration))
                        return false;
                    fertilizer.CapacityLevel = saved.fertilizerCapacityLevel;
                    fertilizer.RateLevel = saved.fertilizerRateLevel;
                    fertilizer.CheckpointUtcTicks = saved.fertilizerUtcTicks;
                    fertilizer.BoostRemaining = saved.boostRemaining;
                    if (fertilizer.Balance > fertilizer.Capacity) return false;
                }
            }
            else if (normalSave && PlayerPrefs.HasKey(OldKey))
            {
                var old = new Legacy();
                JsonUtility.FromJsonOverwrite(PlayerPrefs.GetString(OldKey), old);
                if (old.version != 1 || old.harvestedCount < 0 || old.ownedPotatoes < 0 ||
                    old.ownedPotatoes > old.harvestedCount || old.shovelLevel < 0 || old.shovelLevel > 3 ||
                    old.autoDigPurchased < 0 || old.autoDigPurchased > 1 ||
                    (old.autoDigPurchased == 1 && old.harvestedCount < 5) ||
                    old.requiredDigAmount < 10 || old.digProgress < 0 || old.digProgress >= old.requiredDigAmount)
                    return false;

                loaded.Wallet = LargeNumber.From(old.ownedPotatoes);
                loaded.HarvestCount = loaded.Earned = LargeNumber.From(old.harvestedCount);
                loaded.CycleBest = loaded.Earned;
                loaded.Progress = LargeNumber.From(old.digProgress);
                loaded.Target = LargeNumber.From(old.requiredDigAmount);
                loaded.Levels[(int)DigUpgrade.Power] = old.shovelLevel;
                loaded.Levels[(int)DigUpgrade.Auto] = old.autoDigPurchased;
            }
        }
        catch (ArgumentException)
        {
            return false;
        }

        state = loaded;
        return true;
    }

    public static void Save(DiggingProgress state)
    {
        var pending = new string[state.PendingHits.Count];
        var strikeSizes = new string[pending.Length];
        int index = 0;
        foreach (var work in state.PendingHits)
        {
            pending[index] = work.Amount.Encode();
            strikeSizes[index++] = work.StrikeSize.Encode();
        }

        var saved = new Snapshot
        {
            version = 4,
            wallet = state.Wallet.Encode(),
            earned = state.Earned.Encode(),
            harvestCount = state.HarvestCount.Encode(),
            progress = state.Progress.Encode(),
            target = state.Target.Encode(),
            carry = state.Carry.Encode(),
            levels = state.Levels,
            pendingHits = pending,
            pendingHitStrikeSizes = strikeSizes,
            cycleBest = state.CycleBest.Encode(),
            prestigeExponent = state.PrestigeExponent,
            imofinityCount = state.ImofinityCount,
            fertilizerBalance = state.Fertilizer.Balance.Encode(),
            fertilizerCapacityLevel = state.Fertilizer.CapacityLevel,
            fertilizerRateLevel = state.Fertilizer.RateLevel,
            fertilizerUtcTicks = state.Fertilizer.CheckpointUtcTicks,
            boostRemaining = state.Fertilizer.BoostRemaining
        };
        PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(saved));
        PlayerPrefs.Save();
    }
}
