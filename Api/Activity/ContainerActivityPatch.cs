using HarmonyLib;

namespace JoksterCube.PlayerDetailSync.Api;

[HarmonyPatch(typeof(Container), nameof(Container.Interact))]
internal static class ContainerActivityPatch
{
    private static void Postfix(Container __instance, Humanoid __0, bool __result)
    {
        if (__result && __0 is Player player)
            PlayerActivityCapture.RecordContainerOpened(player, __instance);
    }
}