using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CustomAlbums.Data;
using CustomAlbums.Utilities;

namespace CustomAlbums.Managers
{
    public static class LibraryManager
    {
        public const string LibraryPath = "CustomAlbums_Library";

        private const int CacheVersion = 1;
        private const string CacheLocation = "UserData";
        private const string CacheFile = "CustomAlbums_LibraryIndex.json";

        private static readonly Logger Logger = new(nameof(LibraryManager));
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private static readonly List<LibraryAlbumEntry> Albums = new();

        public static IReadOnlyList<LibraryAlbumEntry> Entries => Albums;

        public static void RefreshIndex()
        {
            EnsureDirectories();

            var cachedAlbums = LoadCache()
                .GroupBy(album => album.RelativePath)
                .ToDictionary(group => group.Key, group => group.First());

            Albums.Clear();

            foreach (var path in SafeEnumerateLibraryFiles())
            {
                var relativePath = NormalizeRelativePath(Path.GetRelativePath(LibraryPath, path));
                var fileInfo = new FileInfo(path);

                if (cachedAlbums.TryGetValue(relativePath, out var cached) &&
                    cached.FileSize == fileInfo.Length &&
                    cached.LastWriteTimeUtc == fileInfo.LastWriteTimeUtc)
                {
                    Albums.Add(cached);
                    continue;
                }

                var entry = ReadEntry(path, relativePath, fileInfo);
                if (entry != null) Albums.Add(entry);
            }

            Albums.Sort((left, right) => string.Compare(left.Info.Name, right.Info.Name, StringComparison.OrdinalIgnoreCase));
            SaveCache();
            Logger.Msg($"Library index refreshed: {Albums.Count} albums.", false);
        }

        public static IEnumerable<LibraryAlbumEntry> Search(string query, string category = null)
        {
            var albums = Albums.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(category) && category != "All")
            {
                albums = albums.Where(album =>
                    album.Category.Equals(category, StringComparison.OrdinalIgnoreCase) ||
                    album.Category.StartsWith(category + "/", StringComparison.OrdinalIgnoreCase));
            }

            if (string.IsNullOrWhiteSpace(query)) return albums;

            query = query.Trim();
            return albums.Where(album =>
                Contains(album.Info.Name, query) ||
                Contains(album.Info.NameRomanized, query) ||
                Contains(album.Info.Author, query) ||
                Contains(album.FileName, query) ||
                (album.Info.SearchTags?.Any(tag => Contains(tag, query)) ?? false));
        }

        public static IEnumerable<string> GetCategories()
        {
            return Albums
                .Select(album => album.Category)
                .Where(category => !string.IsNullOrWhiteSpace(category))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(category => category, StringComparer.OrdinalIgnoreCase);
        }

        public static bool Activate(LibraryAlbumEntry entry)
        {
            if (entry == null) return false;

            EnsureDirectories();

            var sourcePath = GetLibraryFullPath(entry);
            if (!File.Exists(sourcePath))
            {
                Logger.Warning($"Library album is missing: {entry.RelativePath}");
                return false;
            }

            var destinationPath = Path.Combine(AlbumManager.SearchPath, entry.ActiveFileName);
            if (File.Exists(destinationPath))
            {
                SynchronizeSaveAliases(entry);
                return true;
            }

            try
            {
                File.Copy(sourcePath, destinationPath, false);
                SynchronizeSaveAliases(entry);
                Logger.Msg($"Activated library album: {entry.RelativePath}", false);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to activate library album {entry.RelativePath}: {ex.Message}");
                return false;
            }
        }

        public static bool Activate(string relativePath)
        {
            return Activate(GetByRelativePath(relativePath));
        }

        public static bool Deactivate(LibraryAlbumEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.ActiveFileName)) return false;

            var activePath = Path.Combine(AlbumManager.SearchPath, entry.ActiveFileName);
            if (!File.Exists(activePath)) return true;

            try
            {
                SynchronizeSaveAliases(entry);
                File.Delete(activePath);
                Logger.Msg($"Deactivated library album: {entry.RelativePath}", false);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to deactivate library album {entry.RelativePath}: {ex.Message}");
                return false;
            }
        }

        public static bool Deactivate(string relativePath)
        {
            return Deactivate(GetByRelativePath(relativePath));
        }

        public static LibraryAlbumEntry GetByRelativePath(string relativePath)
        {
            relativePath = NormalizeRelativePath(relativePath);
            return Albums.FirstOrDefault(album =>
                album.RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase));
        }

        private static LibraryAlbumEntry ReadEntry(string path, string relativePath, FileInfo fileInfo)
        {
            try
            {
                using var zip = ZipFile.OpenRead(path);
                var info = zip.GetEntry("info.json");
                if (info == null)
                {
                    Logger.Warning($"Library album has no info.json: {relativePath}");
                    return null;
                }

                using var stream = info.Open();
                var albumInfo = Json.Deserialize<AlbumInfo>(stream);
                if (albumInfo == null) return null;

                return new LibraryAlbumEntry
                {
                    RelativePath = relativePath,
                    Category = GetCategory(relativePath),
                    FileName = Path.GetFileName(relativePath),
                    FileSize = fileInfo.Length,
                    LastWriteTimeUtc = fileInfo.LastWriteTimeUtc,
                    ActiveFileName = GetActiveFileName(relativePath),
                    Info = albumInfo,
                    HasPng = zip.GetEntry("cover.png") != null,
                    HasGif = zip.GetEntry("cover.gif") != null
                };
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to index library album {relativePath}: {ex.Message}");
                return null;
            }
        }

        private static List<LibraryAlbumEntry> LoadCache()
        {
            var cachePath = GetCachePath();
            if (!File.Exists(cachePath)) return new List<LibraryAlbumEntry>();

            try
            {
                using var stream = File.OpenRead(cachePath);
                var cache = JsonSerializer.Deserialize<LibraryIndexCache>(stream, JsonOptions);
                return cache?.CacheVersion == CacheVersion ? cache.Albums : new List<LibraryAlbumEntry>();
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to read library index cache: {ex.Message}");
                return new List<LibraryAlbumEntry>();
            }
        }

        private static void SaveCache()
        {
            try
            {
                Directory.CreateDirectory(CacheLocation);
                var cache = new LibraryIndexCache
                {
                    CacheVersion = CacheVersion,
                    Albums = Albums.ToList()
                };
                File.WriteAllText(GetCachePath(), JsonSerializer.Serialize(cache, JsonOptions));
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to save library index cache: {ex.Message}");
            }
        }

        private static void EnsureDirectories()
        {
            Directory.CreateDirectory(LibraryPath);
            Directory.CreateDirectory(AlbumManager.SearchPath);
            Directory.CreateDirectory(CacheLocation);
        }

        private static string GetLibraryFullPath(LibraryAlbumEntry entry)
        {
            return Path.Combine(LibraryPath, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string GetCachePath()
        {
            return Path.Combine(CacheLocation, CacheFile);
        }

        private static string GetCategory(string relativePath)
        {
            var directory = Path.GetDirectoryName(relativePath.Replace('/', Path.DirectorySeparatorChar));
            return string.IsNullOrWhiteSpace(directory)
                ? "Unsorted"
                : directory.Replace(Path.DirectorySeparatorChar, '/');
        }

        private static string GetActiveFileName(string relativePath)
        {
            var name = Path.GetFileNameWithoutExtension(relativePath);
            var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(relativePath.ToLowerInvariant())))[..8];
            return $"{name}_{hash}.mdm";
        }

        private static void SynchronizeSaveAliases(LibraryAlbumEntry entry)
        {
            var activeAlbumName = GetAlbumName(entry.ActiveFileName);
            var libraryAlbumName = GetAlbumName(entry.FileName);
            SaveManager.SynchronizeAlbumAliases(activeAlbumName, libraryAlbumName);
            SaveManager.SaveSaveFile();
        }

        private static string GetAlbumName(string fileName)
        {
            return $"album_{Path.GetFileNameWithoutExtension(fileName)}";
        }

        private static string NormalizeRelativePath(string relativePath)
        {
            return relativePath.Replace('\\', '/');
        }

        private static bool Contains(string text, string value)
        {
            return !string.IsNullOrEmpty(text) &&
                   text.Contains(value, StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> SafeEnumerateLibraryFiles()
        {
            try
            {
                if (!Directory.Exists(LibraryPath)) return Enumerable.Empty<string>();
                return Directory.GetFiles(LibraryPath, AlbumManager.SearchPattern, SearchOption.AllDirectories);
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to enumerate library albums: {ex.Message}");
                return Enumerable.Empty<string>();
            }
        }
    }
}
