using System.Collections.Generic;
using TMPro;
using UnityEngine;

public sealed class DiggingFeedback : MonoBehaviour
{
    [SerializeField] private ParticleSystem soilParticles;
    [SerializeField] private TMP_Text rewardTemplate;

    private const float RewardDuration = 0.65f;
    private const int RewardSlots = 4;
    private const double SummaryInterval = 0.5;
    private const double RapidParticleInterval = 0.12;

    private sealed class Reward
    {
        public TMP_Text Label;
        public double StartedAt;
        public float HorizontalOffset;
    }

    private readonly List<Reward> rewards = new();
    private LargeNumber pendingReward;
    private double rewardDue;
    private double nextParticleTime;
    private double nextPaceSample;
    private int harvestsInWindow;
    private int strikesInWindow;
    private int consecutiveWindows;
    internal bool IsRapid { get; private set; }

    // Require two qualifying windows and separate harvesting strikes. A chain
    // that continues over multiple frames still counts as one strike.
    internal bool UpdatePace(double now)
    {
        if (nextPaceSample == 0 || now >= nextPaceSample + 1)
        {
            ResetPace();
            nextPaceSample = now + 1;
        }
        else if (now >= nextPaceSample)
        {
            bool transition = IsRapid ? harvestsInWindow < 2 : harvestsInWindow > 3 && strikesInWindow >= 2;
            consecutiveWindows = transition ? consecutiveWindows + 1 : 0;
            if (consecutiveWindows >= 2)
            {
                IsRapid = !IsRapid;
                consecutiveWindows = 0;
                if (!IsRapid) rewardDue = now;
            }
            harvestsInWindow = strikesInWindow = 0;
            nextPaceSample += 1;
        }
        return IsRapid;
    }

    internal void ResetPace()
    {
        nextPaceSample = 0;
        nextParticleTime = 0;
        harvestsInWindow = strikesInWindow = consecutiveWindows = 0;
        IsRapid = false;
        rewardDue = 0;
    }

    internal void PlayDig(DigBatch batch, double now)
    {
        if (!isActiveAndEnabled)
            return;

        bool harvested = batch.Harvests > 0;
        harvestsInWindow += batch.Harvests;
        strikesInWindow += batch.HarvestingStrikes;
        if (soilParticles != null && (!IsRapid || now >= nextParticleTime))
        {
            if (IsRapid) nextParticleTime = now + RapidParticleInterval;
            int count = harvested && !IsRapid ? 9 : 4;
            for (int i = 0; i < count; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                var particle = new ParticleSystem.EmitParams
                {
                    position = soilParticles.transform.position +
                               new Vector3(side * Random.Range(0.42f, 0.63f), 0f, Random.Range(-0.44f, -0.21f)),
                    velocity = new Vector3(side * Random.Range(0.55f, 1.1f),
                        Random.Range(0.45f, harvested ? 1.25f : 0.9f), Random.Range(-0.45f, -0.2f)),
                    startSize = Random.Range(0.035f, harvested ? 0.11f : 0.066f),
                    startLifetime = Random.Range(0.22f, 0.38f)
                };
                soilParticles.Emit(particle, 1);
            }
        }

        if (!harvested || rewardTemplate == null) return;
        if (pendingReward.IsZero)
            rewardDue = IsRapid ? now + SummaryInterval : now;
        pendingReward += batch.Earned;
        FlushReward(now);
    }

    private void FlushReward(double now)
    {
        if (pendingReward.IsZero || now < rewardDue) return;
        var reward = rewards.Find(item => !item.Label.gameObject.activeSelf || now - item.StartedAt >= RewardDuration);
        if (reward == null)
        {
            if (rewards.Count >= RewardSlots) return;
            reward = new Reward { Label = Instantiate(rewardTemplate, rewardTemplate.transform.parent) };
            reward.Label.enableAutoSizing = true;
            reward.Label.fontSizeMax = rewardTemplate.fontSize;
            reward.Label.fontSizeMin = 12;
            reward.Label.textWrappingMode = TextWrappingModes.NoWrap;
            reward.Label.transform.SetSiblingIndex(rewardTemplate.transform.GetSiblingIndex() + 1);
            rewards.Add(reward);
        }

        reward.StartedAt = now;
        reward.HorizontalOffset = rewards.IndexOf(reward) % 2 == 0 ? -14f : 24f;
        reward.Label.text = "+" + pendingReward.FormatWholeReward();
        pendingReward = default;
        reward.Label.alpha = 1f;
        reward.Label.rectTransform.anchoredPosition = rewardTemplate.rectTransform.anchoredPosition +
            new Vector2(reward.HorizontalOffset, 0f);
        reward.Label.gameObject.SetActive(true);
    }

    private void LateUpdate()
    {
        double now = Time.unscaledTimeAsDouble;
        FlushReward(now);
        foreach (var reward in rewards)
        {
            if (!reward.Label.gameObject.activeSelf)
                continue;

            float progress = (float)((now - reward.StartedAt) / RewardDuration);
            if (progress >= 1f)
            {
                reward.Label.gameObject.SetActive(false);
                continue;
            }

            reward.Label.rectTransform.anchoredPosition = rewardTemplate.rectTransform.anchoredPosition +
                new Vector2(reward.HorizontalOffset, 33f * progress);
            reward.Label.alpha = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.35f, 1f, progress));
        }
    }

    private void OnDisable()
    {
        foreach (var reward in rewards)
        {
            if (reward.Label != null)
                reward.Label.gameObject.SetActive(false);
        }
        if (soilParticles != null)
            soilParticles.Clear();
    }
}
