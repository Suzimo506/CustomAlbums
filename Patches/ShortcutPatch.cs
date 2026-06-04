using HarmonyLib;
using Il2CppAssets.Scripts.Database;
using Il2CppAssets.Scripts.PeroTools.Nice.Components;
using CustomAlbums.Managers;
using CustomAlbums.Data;
using UnityEngine;

namespace CustomAlbums.Patches
{
    internal static class ShortcutPatch
    {
        internal static bool HandleScroll(FancyScrollView fsv, float time, bool forward)
        {
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            {
                var dbMusicTag = GlobalDataBase.dbMusicTag;
                if (dbMusicTag == null) return true;

                bool isCustomCategory = (dbMusicTag.selectedTagIndex == AlbumManager.Uid);
                if (!isCustomCategory)
                {
                    var curMusic = dbMusicTag.CurMusicInfo();
                    if (curMusic == null || !curMusic.uid.StartsWith($"{AlbumManager.Uid}-"))
                        return true;
                }

                var list = dbMusicTag.m_StageShowMusicUids;
                if (list == null || list.Count <= 1) return true;

                int currentIndex = dbMusicTag.curSelectedMusicIdx;
                int targetIndex = GetTargetFolderIndex(list, currentIndex, forward);

                if (targetIndex >= 0 && targetIndex < list.Count && targetIndex != currentIndex)
                {
                    fsv.ScrollToDataIndex(targetIndex, time);
                    return false; // Intercept original scrolling
                }
            }
            return true;
        }

        private static string GetPackTitle(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return "EMPTY";
            if (!uid.StartsWith($"{AlbumManager.Uid}-"))
                return "RANDOM_SONG";

            var pack = PackManager.GetPackFromUid(uid);
            if (pack != null && !string.IsNullOrEmpty(pack.Title))
            {
                return pack.Title;
            }

            return AlbumManager.GetCustomAlbumsTitle();
        }

        private static int GetTargetFolderIndex(Il2CppSystem.Collections.Generic.List<string> list, int currentIndex, bool forward)
        {
            int listCount = list.Count;
            if (listCount == 0) return 0;
            string currentPack = GetPackTitle(list[currentIndex]);

            if (forward)
            {
                for (int i = currentIndex + 1; i < listCount; i++)
                {
                    if (GetPackTitle(list[i]) != currentPack)
                    {
                        return i;
                    }
                }
                return listCount - 1;
            }
            else
            {
                int prevPackLastIndex = -1;
                for (int i = currentIndex - 1; i >= 0; i--)
                {
                    if (GetPackTitle(list[i]) != currentPack)
                    {
                        prevPackLastIndex = i;
                        break;
                    }
                }

                // If scrolling left and no different pack is found (indicating we are at the first pack), loop to the very end (random song)
                if (prevPackLastIndex == -1)
                {
                    return listCount - 1;
                }

                string prevPack = GetPackTitle(list[prevPackLastIndex]);
                for (int i = prevPackLastIndex - 1; i >= 0; i--)
                {
                    if (GetPackTitle(list[i]) != prevPack)
                    {
                        return i + 1;
                    }
                }

                return 0;
            }
        }
    }

    [HarmonyPatch(typeof(FancyScrollView), nameof(FancyScrollView.ScrollToNext))]
    internal static class ScrollToNextPatch
    {
        private static bool Prefix(FancyScrollView __instance, float time)
        {
            return ShortcutPatch.HandleScroll(__instance, time, true);
        }
    }

    [HarmonyPatch(typeof(FancyScrollView), nameof(FancyScrollView.ScrollToPrevious))]
    internal static class ScrollToPreviousPatch
    {
        private static bool Prefix(FancyScrollView __instance, float time)
        {
            return ShortcutPatch.HandleScroll(__instance, time, false);
        }
    }
}
