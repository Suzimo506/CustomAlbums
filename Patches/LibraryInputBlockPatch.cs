using CustomAlbums.UI;
using HarmonyLib;
using UnityEngine;

namespace CustomAlbums.Patches
{
    internal static class LibraryInputBlockPatch
    {
        internal static bool ShouldBlockKey(KeyCode key)
        {
            if (!LibraryWindow.IsSearchInputFocused) return false;

            return key == KeyCode.Space ||
                   key == KeyCode.Return ||
                   key == KeyCode.KeypadEnter ||
                   key == KeyCode.Escape;
        }
    }

    [HarmonyPatch(typeof(Input), nameof(Input.GetKeyDown), typeof(KeyCode))]
    [HarmonyPriority(Priority.First)]
    internal static class LibraryInputGetKeyDownPatch
    {
        private static bool Prefix(KeyCode key, ref bool __result)
        {
            if (!Main.IsLobbyScene) return true;
            if (!LibraryInputBlockPatch.ShouldBlockKey(key)) return true;

            __result = false;
            return false;
        }
    }
}
