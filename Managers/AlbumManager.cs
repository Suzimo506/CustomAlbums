using CustomAlbums.Data;
using CustomAlbums.ModExtensions;
using CustomAlbums.Utilities;
using Il2CppAssets.Scripts.PeroTools.Commons;
using Il2CppAssets.Scripts.PeroTools.GeneralLocalization;
using Il2CppPeroTools2.Resources;
using System.IO.Compression;
using UnityEngine;
using Logger = CustomAlbums.Utilities.Logger;

namespace CustomAlbums.Managers
{
    public static class AlbumManager
    {
        public const int Uid = 999;
        public const string SearchPath = "Custom_Albums";
        public const string SearchPattern = "*.mdm";
        public const string PackSearchPattern = "*.mdp";
        public static readonly string JsonName = $"ALBUM{Uid + 1}";
        public static readonly string MusicPackage = $"music_package_{Uid}";

        public static readonly Dictionary<string, string> Languages = new()
        {
            { "English", "Custom Albums" },
            { "ChineseS", "自定义" },
            { "ChineseT", "自定義" },
            { "Japanese", "カスタムアルバム" },
            { "Korean", "커스텀앨범" }
        };

        private static readonly Logger Logger = new(nameof(AlbumManager));
        internal static readonly FileSystemWatcher AlbumWatcher = new();
        internal static Events.LoadAlbumEvent OnAlbumLoaded;

        private static int MaxCount { get; set; }
        internal static string CurrentPack { get; set; } = null;
        public static Dictionary<string, Album> LoadedAlbums { get; } = new();


        public static Pack LoadPack(string directory)
        {
            // Get the files from the directory
            try
            {
                var zipFiles = ZipFile.OpenRead(directory);

                // Filter for .mdm files and find the pack.json file
                var mdms = zipFiles.Entries.Where(file => file.Name.EndsWith(".mdm"));
                var json = zipFiles.Entries.FirstOrDefault(file => file.Name.EndsWith(".json"));

                // Initialize pack and variables
                var pack = PackManager.CreatePack(json, directory);
                CurrentPack = pack.Title;

                // StartIndex for pack
                pack.StartIndex = MaxCount;

                // Count successfully loaded .mdm files
                pack.Length = mdms.Count(mdm =>
                {
                    var album = LoadOne(directory, mdm, mdm.FullName);
                    if (album is not null)
                    {
                        pack.Albums.Add(album);
                        return true;
                    }
                    return false;
                });

                // Set the current pack to null and add the pack to the pack list
                CurrentPack = null;
                PackManager.AddPack(pack);

                return pack;
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to load album at {directory}. Reason: {ex.Message}");
                Logger.Warning(ex.StackTrace);

                return null;
            }
        }

        public static Pack LoadFolderPack(string directory)
        {
            try
            {
                var packJsonPath = System.IO.Path.Combine(directory, "pack.json");
                if (!System.IO.File.Exists(packJsonPath)) return null;

                // Deserialize pack config file
                Pack pack;
                using (var stream = System.IO.File.OpenRead(packJsonPath))
                {
                    pack = Json.Deserialize<Pack>(stream);
                }
                pack.Path = directory;

                // Set the current pack title
                CurrentPack = pack.Title;
                pack.StartIndex = MaxCount;

                // Scan and load all albums
                var mdms = System.IO.Directory.GetFiles(directory, "*.mdm");
                pack.Length = mdms.Count(mdmPath =>
                {
                    var album = LoadOne(mdmPath);
                    if (album is not null)
                    {
                        pack.Albums.Add(album);
                        return true;
                    }
                    return false;
                });

                // Clean status and register
                CurrentPack = null;
                PackManager.AddPack(pack);
                return pack;
            }
            catch (Exception ex)
            {
                // Failed to load folder pack
                Logger.Warning($"Failed to load folder pack at {directory}. Reason: {ex.Message}");
                Logger.Warning(ex.StackTrace);
                CurrentPack = null;
                return null;
            }
        }

        public static Album LoadOne(string directory, ZipArchiveEntry mdm, string fullFileName)
        {
            var fileName = Path.GetFileNameWithoutExtension(fullFileName);
            
            if (LoadedAlbums.ContainsKey($"album_{fileName}")) return null;

            try
            {
                var album = new Album(directory, mdm, MaxCount, CurrentPack);
                if (album.Info is null) return null;

                var albumName = album.AlbumName;
                Logger.Msg("Adding " + albumName + " as a pack!");

                LoadedAlbums.Add(albumName, album);
                MaxCount++;

                if (album.HasPng)
                    ResourcesManager.instance.LoadFromName<Sprite>($"{albumName}_cover").hideFlags |=
                        HideFlags.DontUnloadUnusedAsset;

                if (album.HasGif) CoverManager.EnqueueGifToLoad(album);

                Logger.Msg($"Loaded {albumName}: {album.Info.Name}");
                OnAlbumLoaded?.Invoke(typeof(AlbumManager), new AlbumEventArgs(album));
                return album;
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to load album at {fileName}. Reason: {ex.Message}");
                Logger.Warning(ex.StackTrace);
            }

            return null;
        }

        public static Album LoadOne(string path)
        {
            var isDirectory = File.GetAttributes(path).HasFlag(FileAttributes.Directory);
            var fileName = isDirectory ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
            
            if (LoadedAlbums.ContainsKey($"album_{fileName}")) return null;
            
            try
            {
                var album = new Album(path, MaxCount, CurrentPack);
                if (album.Info is null) return null;

                var albumName = album.AlbumName;
                
                LoadedAlbums.Add(albumName, album);
                MaxCount++;

                if (album.HasPng)
                    ResourcesManager.instance.LoadFromName<Sprite>($"{albumName}_cover").hideFlags |=
                        HideFlags.DontUnloadUnusedAsset;

                if (album.HasGif) CoverManager.EnqueueGifToLoad(album);

                Logger.Msg($"Loaded {albumName}: {album.Info.Name}");
                OnAlbumLoaded?.Invoke(typeof(AlbumManager), new AlbumEventArgs(album));
                return album;
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to load album at {fileName}. Reason: {ex.Message}");
                Logger.Warning(ex.StackTrace);
            }

            return null;
        }

        public static void LoadAlbums()
        {
            LoadedAlbums.Clear();
            MaxCount = 0;
            
            var packs = new List<string>();
            var files = new List<string>();
            files.AddRange(Directory.GetFiles(SearchPath, SearchPattern));
            
            // Scan folder packs and regular album directories
            foreach (var dir in Directory.GetDirectories(SearchPath))
            {
                if (System.IO.File.Exists(System.IO.Path.Combine(dir, "pack.json")))
                {
                    packs.Add(dir);
                }
                else
                {
                    files.Add(dir);
                }
            }
            packs.AddRange(Directory.GetFiles(SearchPath, PackSearchPattern));

            foreach (var pack in packs)
            {
                if (System.IO.Directory.Exists(pack))
                {
                    LoadFolderPack(pack);
                }
                else
                {
                    LoadPack(pack);
                }
            }
            foreach (var file in files) LoadOne(file);

            Logger.Msg($"Finished loading {LoadedAlbums.Count} albums.", false);
        }

        public static IEnumerable<string> GetAllUid()
            => LoadedAlbums.Select(album => $"{Uid}-{album.Value.Index}");

        public static Album GetByUid(string uid)
        {
            if (string.IsNullOrEmpty(uid) || !uid.StartsWith($"{Uid}-")) return null;
            if (int.TryParse(uid[4..], out var index))
            {
                return LoadedAlbums.FirstOrDefault(album => album.Value.Index == index).Value;
            }
            return null;
        }
        public static string GetAlbumNameFromUid(string uid)
        {
            var album = GetByUid(uid);
            return album is null ? string.Empty : album.AlbumName;
        }
        public static IEnumerable<string> GetAlbumUidsFromNames(this IEnumerable<string> albumNames)
        {
            return albumNames.Where(name => LoadedAlbums.ContainsKey(name))
                .Select(name => $"{Uid}-{LoadedAlbums[name].Index}");
        }

        /// <summary>
        ///     Gets the current "Custom Albums" title based on language.
        /// </summary>
        /// <returns>The current "Custom Albums" title based on language.</returns>
        public static string GetCustomAlbumsTitle()
        {
            return Languages.GetValueOrDefault(
                SingletonScriptableObject<LocalizationSettings>
                .instance?
                .GetActiveOption("Language") ?? "English");
        }
    }
}