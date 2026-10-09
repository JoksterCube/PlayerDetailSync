using HarmonyLib;

namespace JoksterCube.PlayerDetailSync.Api;

[HarmonyPatch(typeof(Character), nameof(Character.OnDamaged))]
internal static class CharacterDamageActivityPatch
{
    private static void Postfix(Character __instance, HitData __0)
    {
        if (__instance is Player player)
            PlayerActivityCapture.RecordDamage(player, __0);

        Character? attacker = __0.GetAttacker();
        if (attacker is Player attackerPlayer && attackerPlayer == Player.m_localPlayer)
            PlayerActivityCapture.RecordHuntingAction(attackerPlayer, __instance);
    }
}