using Il2Cpp;
using CustomAlbums.Managers;
using CustomAlbums.Utilities;
using HarmonyLib;

namespace CustomAlbums.Patches
{
    internal class TreeItemPatch
    {
        // TODO: Finish "Album" support
        private static readonly Logger Logger = new(nameof(TreeItemPatch));
        internal static bool IsCustomAlbumStateInjected { get; private set; }
        internal static void SetCustomAlbumStateInjected(bool injected) => IsCustomAlbumStateInjected = injected;

        [HarmonyPatch(typeof(PnlMusicTagItem), nameof(PnlMusicTagItem.OnTagClicked))]
        internal class OnMusicTagClickedPatch
        {
            private static void Prefix(int tagIndex, PnlMusicTagItem __instance)
            {
                if (tagIndex == AlbumManager.Uid) SetCustomAlbumStateInjected(true);
            }
        }

        [HarmonyPatch(typeof(PnlMusicTagItem), nameof(PnlMusicTagItem.Enable))]
        internal class EnablePatch
        {
            private static void Prefix(PnlMusicTagItem __instance)
            {
                // STUB
            }
        }

        [HarmonyPatch(typeof(PnlMusicTagItem), nameof(PnlMusicTagItem.AddDataToMgr))]
        internal class AddDataPatch
        {
           // STUB
        }
    }
}
