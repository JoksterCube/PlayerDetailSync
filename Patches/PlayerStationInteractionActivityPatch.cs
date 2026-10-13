using HarmonyLib;
using UnityEngine;

namespace JoksterCube.PlayerDetailSync.Api;

[HarmonyPatch(typeof(Player), nameof(Player.Interact))]
internal static class PlayerStationInteractionActivityPatch
{
    private static void Postfix(Player __instance, GameObject __0)
    {
        if (__0 != null)
            PlayerActivityCapture.RecordStationInteraction(__instance, __0);
    }
}