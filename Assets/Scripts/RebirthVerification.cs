#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

internal static class RebirthVerification
{
    private const string ModeKey = "OimoDig.RebirthVerification";
    private const string FixtureKey = ModeKey + ".Fixture";
    public static bool IsEnabled => SessionState.GetBool(ModeKey, false);

    [MenuItem("Tools/OIMO-DIG Verification/1 - First Prestige at 1e9")]
    private static void FirstPrestige() => SelectFixture(1);
    [MenuItem("Tools/OIMO-DIG Verification/2 - Repeat 1e9 Prestige")]
    private static void RepeatPrestige() => SelectFixture(2);
    [MenuItem("Tools/OIMO-DIG Verification/3 - Jump from 1e50 to 1e60")]
    private static void Jump() => SelectFixture(3);
    [MenuItem("Tools/OIMO-DIG Verification/4 - Just below 1e308, dig once")]
    private static void ApproachImofinity() => SelectFixture(4);
    [MenuItem("Tools/OIMO-DIG Verification/5 - Exactly 1e308")]
    private static void AtImofinity() => SelectFixture(5);
    [MenuItem("Tools/OIMO-DIG Verification/6 - Fertilizer pacing (balance 120, Power Lv.35)")]
    private static void FertilizerPacing() => SelectFixture(6);
    [MenuItem("Tools/OIMO-DIG Verification/7 - Fertilizer with both rebirths unlocked")]
    private static void FertilizerRebirths() => SelectFixture(7);

    [MenuItem("Tools/OIMO-DIG Verification/Resume isolated save")]
    private static void Resume()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        SessionState.SetBool(ModeKey, true);
        SessionState.EraseInt(FixtureKey);
        Debug.Log("OIMO-DIG will load the isolated verification save on the next Play.");
    }

    [MenuItem("Tools/OIMO-DIG Verification/Return to normal save")]
    private static void UseNormalSave()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        SessionState.SetBool(ModeKey, false);
        SessionState.EraseInt(FixtureKey);
    }

    private static void SelectFixture(int fixture)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        SessionState.SetBool(ModeKey, true);
        SessionState.SetInt(FixtureKey, fixture);
        Debug.Log("OIMO-DIG verification fixture selected. Enter Play manually. Normal saves are isolated.");
    }

    public static bool TryTakeFixture(out DiggingProgress state)
    {
        int fixture = SessionState.GetInt(FixtureKey, 0);
        state = null;
        if (!IsEnabled || fixture == 0) return false;
        SessionState.EraseInt(FixtureKey);
        state = new DiggingProgress { HarvestCount = LargeNumber.From(20) };
        if (fixture == 6 || fixture == 7)
        {
            state.Fertilizer.Balance = LargeNumber.From(fixture == 6 ? 120 : 110);
            state.Levels[(int)DigUpgrade.Power] = 35;
            if (fixture == 7)
            {
                state.Earned = state.CycleBest = LargeNumber.PowerOfTen(308);
                state.Fertilizer.CapacityLevel = 1;
                state.Fertilizer.RateLevel = 2;
            }
            return true;
        }

        state.PrestigeExponent = fixture switch { 2 => 9, 3 => 50, 4 or 5 => 60, _ => 0 };
        state.Earned = LargeNumber.PowerOfTen(fixture switch { 3 => 60, 4 or 5 => 308, _ => 9 });
        if (fixture == 4)
        {
            LargeNumber.TryDecode("9.999999999999998|307", out state.Earned);
            state.Progress = LargeNumber.From(9);
            // Cross the remaining gap just above 1e308 under the tuned reward curve.
            state.Levels[(int)DigUpgrade.Luck] = 232;
        }

        state.CycleBest = state.Earned;
        return true;
    }
}
#endif
