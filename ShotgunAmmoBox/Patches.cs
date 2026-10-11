using GameNetcodeStuff;
using HarmonyLib;

namespace ShotgunAmmoBox;

[HarmonyPatch]
internal static class Patches
{
    // ShotgunItem.FindAmmoInInventory returns an inventory slot (or 50 for the utility slot). When there's no loose shell
    // we return BoxSlotBase + the box's slot instead; the reload later destroys "that slot", which DrawFromBox intercepts.
    private const int BoxSlotBase = 1_000_000; // far past any inventory size, even with slot mods
    private const int BeltMarker = BoxSlotBase - 1; // the toolbelt (ItemOnlySlot), kept apart from every numbered slot

    [HarmonyPatch(typeof(GameNetworkManager), "Start")]
    [HarmonyPostfix]
    private static void AfterGameNetworkManagerStart(GameNetworkManager __instance) =>
        BoxRegistry.RegisterShellShop(BoxRegistry.MenuPrefabs(__instance.GetComponent<Unity.Netcode.NetworkManager>()), "main menu");

    [HarmonyPatch(typeof(StartOfRound), "Awake")]
    [HarmonyPostfix]
    private static void AfterStartOfRoundAwake(StartOfRound __instance) => BoxRegistry.FixupFromGame(__instance);

    // LethalLib's hook adds its store keywords and then runs Terminal.Awake, so this postfix sees them.
    [HarmonyPatch(typeof(Terminal), "Awake")]
    [HarmonyPostfix]
    private static void AfterTerminalAwake(Terminal __instance) => BoxRegistry.AddShellAliases(__instance);

    // Start loads the ship's saved items; LethalLib has finished adding items to the list by then.
    [HarmonyPatch(typeof(StartOfRound), "Start")]
    [HarmonyPrefix]
    private static void BeforeStartOfRoundStart(StartOfRound __instance) => BoxRegistry.KeepBoxLast(__instance);

    [HarmonyPatch(typeof(ShotgunItem), "FindAmmoInInventory")]
    [HarmonyPostfix]
    private static void ReloadFromBox(ShotgunItem __instance, ref int __result)
    {
        if (__result != -1 || __instance.playerHeldBy == null) return;
        var player = __instance.playerHeldBy;
        for (int i = 0; i < player.ItemSlots.Length; i++)
        {
            if (player.ItemSlots[i] is AmmoBoxItem box && box.Shells > 0)
            {
                __result = BoxSlotBase + i;
                return;
            }
        }
        if (player.ItemOnlySlot is AmmoBoxItem belt && belt.Shells > 0) __result = BeltMarker;
    }

    [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.DestroyItemInSlotAndSync))]
    [HarmonyPrefix]
    private static bool DrawFromBox(PlayerControllerB __instance, int itemSlot)
    {
        if (itemSlot < BeltMarker) return true;
        int slot = itemSlot - BoxSlotBase;
        var item = itemSlot == BeltMarker ? __instance.ItemOnlySlot : slot < __instance.ItemSlots.Length ? __instance.ItemSlots[slot] : null;
        if (item is AmmoBoxItem box) box.ConsumeForReload();
        return false;
    }
}
