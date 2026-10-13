using HarmonyLib;

namespace JoksterCube.PlayerDetailSync.Api;

[HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
internal static class CharacterDeathWebhookPatch
{
    private static void Prefix(Character __instance, out (int QueueCount, bool HadKey) __state)
    {
        string key = __instance.m_defeatSetGlobalKey;
        // Sent before OnDeath sets the defeat key, so the server knows where the boss fell when crediting it.
        if (__instance.m_bossOrder > 0 && !string.IsNullOrEmpty(key))
            KillCreditSync.ReportTargetDeath(key, __instance.transform.position);
        else if (WebhookNotifications.IsMinibossName(__instance.m_name))
            KillCreditSync.ReportTargetDeath(__instance.m_name, __instance.transform.position);
        Player? player = Player.m_localPlayer;
        bool hadKey = !string.IsNullOrEmpty(key) &&
            (Player.m_addUniqueKeyQueue.Contains(key) || (player != null && player.HaveUniqueKey(key)));
        __state = (Player.m_addUniqueKeyQueue.Count, hadKey);
    }

    private static void Postfix(Character __instance, (int QueueCount, bool HadKey) __state)
    {
        // With World Advancement Progression, its private keys decide who progresses instead.
        if (KillCreditSync.ProgressionInstalled) return;

        // Vanilla queues the defeat key only for the local player of the client that owns the boss.
        if (Player.m_addUniqueKeyQueue.Count > __state.QueueCount && !string.IsNullOrEmpty(__instance.m_defeatSetGlobalKey))
            KillCreditSync.ReportBossKey(__instance.m_defeatSetGlobalKey, !__state.HadKey);
    }
}