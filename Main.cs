using CustomAlbums.Managers;
using CustomAlbums.Patches;
using CustomAlbums.UI;
using CustomAlbums.Utilities;
using Il2CppInterop.Runtime.Injection;
using MelonLoader;
using static CustomAlbums.Patches.AnimatedCoverPatch;

namespace CustomAlbums
{
    public class Main : MelonMod
    {
        private static readonly Logger Logger = new("CustomAlbums");

        public const string MelonName = "CustomAlbums";
        public const string MelonAuthor = "Two Fellas";
        public const string MelonVersion = "4.2.0";
        private static string CurrentScene { get; set; } = string.Empty;
        internal static bool IsLobbyScene => CurrentScene == "UISystem_PC";

        public override void OnInitializeMelon()
        {
            base.OnInitializeMelon();

            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<MarqueeText>();
            }
            catch (Exception ex)
            {
                Logger.Warning("Failed to register library marquee text: " + ex.Message);
            }

            if (!Directory.Exists(AlbumManager.SearchPath)) Directory.CreateDirectory(AlbumManager.SearchPath);
            
            TitleConfigManager.Load();
            ModSettings.Register();
            AssetPatch.AttachHook();
            SavePatch.AttachHook();
            AlbumManager.LoadAlbums();
            SaveManager.LoadSaveFile();
            Logger.Msg("Initialized CustomAlbums!", false);
        }

        public override void OnLateInitializeMelon()
        {
            base.OnLateInitializeMelon();
            HotReloadManager.OnLateInitializeMelon();
        }

        /// <summary>
        ///     This override adds support for animated covers.
        /// </summary>
        public override void OnUpdate()
        {
            base.OnUpdate();
            PerfTrace.BeginFrame();
            if (!IsLobbyScene)
            {
                using (PerfTrace.Measure("CustomAlbums.Main.NonLobbyUpdate"))
                {
                    if (LibraryWindow.IsOpen) LibraryWindow.Close();
                }
                PerfTrace.UpdateReport();
                return;
            }

            using (PerfTrace.Measure("CustomAlbums.Main.OnUpdate"))
            {
                LibraryEntryButton.CreateOrRefresh();
                LibraryWindow.Update();
                MusicStageCellPatch.AnimateCoversUpdate();
            }

            PerfTrace.UpdateReport();
        }

        /// <summary>
        ///     This override adds support for hot reloading.
        /// </summary>
        public override void OnFixedUpdate()
        {
            base.OnFixedUpdate();
            if (!IsLobbyScene) return;

            using (PerfTrace.Measure("CustomAlbums.Main.OnFixedUpdate"))
            {
                HotReloadManager.FixedUpdate();

                // Dispatcher for GIF covers
                if (CoverManager.GifAlbumDatas.TryDequeue(out var gifData))
                {
                    CoverManager.LoadAnimatedCover(gifData);
                }
            }
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            base.OnSceneWasLoaded(buildIndex, sceneName);
            CurrentScene = sceneName;
            PerfTrace.SetGameMain(sceneName == "GameMain");
            LibraryEntryButton.Reset();
            LibraryWindow.Close();
            if (sceneName == "UISystem_PC")
            {
                LibraryEntryButton.CreateOrRefresh();
            }
            MusicStageCellPatch.CurrentScene = sceneName;
        }
    }
}
