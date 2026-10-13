using System.Reflection;
using HarmonyLib;

namespace JoksterCube.PlayerDetailSync.Api;

[HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
internal static class PlayerDeathWebhookPatch
{
    private static readonly FieldInfo? LastHitField = AccessTools.Field(typeof(Character), "m_lastHit");

    // Player deaths run only on the dying player's own client.
    private static void Postfix(Player __instance)
    {
        if (__instance != Player.m_localPlayer) return;
        var lastHit = LastHitField?.GetValue(__instance) as HitData;
        Character? attacker = lastHit?.GetAttacker();
        long killerId = attacker is Player killer && killer != __instance ? killer.GetPlayerID() : 0L;
        string killerToken = attacker != null && attacker is not Player ? attacker.m_name : "";
        KillCreditSync.ReportPlayerDeath(killerId, lastHit?.m_hitType ?? HitData.HitType.Undefined, killerToken,
            __instance.transform.position);
    }
}
