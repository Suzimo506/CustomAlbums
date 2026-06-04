using CustomAlbums.Managers;
using CustomAlbums.Patches;
using CustomAlbums.Utilities;
using MelonLoader;
using static CustomAlbums.Patches.AnimatedCoverPatch;

namespace CustomAlbums
{
    public class Main : MelonMod
    {
        private static readonly Logger Logger = new("CustomAlbums");

        public const string MelonName = "CustomAlbums";
        public const string MelonAuthor = "Two Fellas";
        public const string MelonVersion = "4.1.9";

        public override void OnInitializeMelon()
        {
            base.OnInitializeMelon();

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
            MusicStageCellPatch.AnimateCoversUpdate();
        }

        /// <summary>
        ///     This override adds support for hot reloading.
        /// </summary>
        public override void OnFixedUpdate()
        {
            base.OnFixedUpdate();
            HotReloadManager.FixedUpdate();
            
            // Dispatcher for GIF covers
            if (CoverManager.GifAlbumDatas.TryDequeue(out var gifData))
            {
                CoverManager.LoadAnimatedCover(gifData);
            }
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            base.OnSceneWasLoaded(buildIndex, sceneName);
            MusicStageCellPatch.CurrentScene = sceneName;
        }
    }
}