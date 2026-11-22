using System.Collections.Generic;
using TMPro;
using UnityEngine;

public sealed class DiggingFeedback : MonoBehaviour
{
    [SerializeField] private ParticleSystem soilParticles;
    [SerializeField] private TMP_Text rewardTemplate;

    private const float RewardDuration = 0.65f;

    private sealed class Reward
    {
        public TMP_Text Label;
        public double StartedAt;
        public float HorizontalOffset;
    }

    private readonly List<Reward> rewards = new();

    public void PlayDig(int earnedPotatoes)
    {
        if (!isActiveAndEnabled)
            return;

        bool harvested = earnedPotatoes > 0;
        if (soilParticles != null)
        {
            int count = harvested ? 9 : 4;
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

        if (harvested && rewardTemplate != null)
            ShowReward(earnedPotatoes);
    }

    private void ShowReward(int amount)
    {
        var reward = rewards.Find(item => !item.Label.gameObject.activeSelf);
        if (reward == null)
        {
            reward = new Reward { Label = Instantiate(rewardTemplate, rewardTemplate.transform.parent) };
            reward.Label.transform.SetSiblingIndex(rewardTemplate.transform.GetSiblingIndex() + 1);
            rewards.Add(reward);
        }

        reward.StartedAt = Time.unscaledTimeAsDouble;
        reward.HorizontalOffset = rewards.IndexOf(reward) % 2 == 0 ? -14f : 24f;
        reward.Label.text = $"+{amount}";
        reward.Label.alpha = 1f;
        reward.Label.rectTransform.anchoredPosition = rewardTemplate.rectTransform.anchoredPosition +
            new Vector2(reward.HorizontalOffset, 0f);
        reward.Label.gameObject.SetActive(true);
    }

    private void LateUpdate()
    {
        double now = Time.unscaledTimeAsDouble;
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
