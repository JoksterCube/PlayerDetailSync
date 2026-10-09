using System;
using System.Collections.Generic;
using System.Reflection;

namespace JoksterCube.PlayerDetailSync.Api;

internal static class CharacterStatusCapture
{
    private static readonly FieldInfo ShieldCapacityField = typeof(SE_Shield).GetField(
        "m_totalAbsorbDamage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
    private static readonly FieldInfo ShieldDamageField = typeof(SE_Shield).GetField(
        "m_damage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

    internal static TimedPlayerData[] Capture(Character character)
    {
        SEMan? statusManager = character.GetSEMan();
        if (statusManager == null) return [];

        var statuses = new List<TimedPlayerData>();
        foreach (StatusEffect status in statusManager.GetStatusEffects())
        {
            if (status == null || string.IsNullOrWhiteSpace(status.m_name)) continue;

            TimedPlayerData effect = TimedPlayerData.FromStatus(
                PlayerDataSnapshot.LocalizeName(status.m_name),
                status.GetRemaningTime(),
                status.m_ttl,
                PlayerDetailSyncApi.RegisterIcon(status.m_icon, status.m_name),
                status.m_icon);
            if (status is SE_Shield shield) CaptureShield(effect, shield);
            statuses.Add(effect);
        }

        return [.. statuses];
    }

    internal static void CaptureShield(TimedPlayerData effect, SE_Shield shield)
    {
        float capacity = (float)ShieldCapacityField.GetValue(shield);
        float damage = (float)ShieldDamageField.GetValue(shield);
        effect.ShieldMaxAbsorption = Math.Max(0f, capacity);
        effect.ShieldRemainingAbsorption = Math.Max(0f, capacity - damage);
    }
}