using System;
using CustomAlbums.UI;
using HarmonyLib;
using Il2CppAssets.Scripts.UI.Panels;
using MelonLoader;

namespace CustomAlbums.Patches
{
    [HarmonyPatch(typeof(PnlMenu), nameof(PnlMenu.OnEnable))]
    internal static class LibraryEntrancePatch
    {
        private static void Postfix()
        {
            try
            {
                LibraryEntryButton.CreateOrRefresh();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Failed to inject library entrance: {ex}");
            }
        }
    }
}
