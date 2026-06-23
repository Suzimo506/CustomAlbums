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

        private const int CacheVersion = 3;
        private const string CacheLocation = "UserData";
        private const string CacheFile = "CustomAlbums_LibraryIndex.json";
        private const string DefaultCategory = "Unsorted";

        private static readonly Logger Logger = new(nameof(LibraryManager));
        private static int _deferSaveDepth;
        private static bool _savePending;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private static readonly object AlbumsLock = new();
        private static readonly List<LibraryAlbumEntry> Albums = new();
        private static int _lastSkippedCount;

        public static IReadOnlyList<LibraryAlbumEntry> Entries
        {
            get
            {
                lock (AlbumsLock)
                    return Albums.ToList();
            }
        }

        public static int LastSkippedCount => _lastSkippedCount;

        public static void LoadCachedIndex()
        {
            EnsureDirectories();
            var albums = LoadCache()
                .Select(UpdateCachedEntryState)
                .Where(album => album != null)
                .OrderBy(album => album.Info.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            lock (AlbumsLock)
            {
                Albums.Clear();
                Albums.AddRange(albums);
            }
        }

        public static void RefreshIndex()
        {
            EnsureDirectories();

            var cachedAlbums = LoadCache()
                .GroupBy(album => album.RelativePath)
                .ToDictionary(group => group.Key, group => group.First());

            var albums = new List<LibraryAlbumEntry>();

            var skippedCount = 0;
            foreach (var path in SafeEnumerateLibraryFiles())
            {
                var relativePath = NormalizeRelativePath(Path.GetRelativePath(LibraryPath, path));
                var fileInfo = new FileInfo(path);

                if (cachedAlbums.TryGetValue(relativePath, out var cached) &&
                    cached.FileSize == fileInfo.Length &&
                    cached.LastWriteTimeUtc == fileInfo.LastWriteTimeUtc)
                {
                    cached.Category = GetCategory(relativePath);
                    cached.FileName = Path.GetFileName(relativePath);
                    cached.ActiveFileName = GetActiveFileName(relativePath);
                    cached.LegacyActiveFileName = GetLegacyActiveFileName(relativePath);
                    cached.ChartMd5s ??= new List<string>();
                    albums.Add(cached);
                    continue;
                }

                var entry = ReadEntry(path, relativePath, fileInfo);
                if (entry != null)
                    albums.Add(entry);
                else
                    skippedCount++;
            }

            albums.Sort((left, right) => string.Compare(left.Info.Name, right.Info.Name, StringComparison.OrdinalIgnoreCase));
            lock (AlbumsLock)
            {
                Albums.Clear();
                Albums.AddRange(albums);
                _lastSkippedCount = skippedCount;
            }
            SaveCache();
            Logger.Msg($"Library index refreshed: {albums.Count} albums, skipped {skippedCount} unsupported files.", false);
        }

        public static IEnumerable<LibraryAlbumEntry> Search(string query, string category = null)
        {
            var albums = Entries.AsEnumerable();

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
            return Entries
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

            var destinationPath = GetActiveFullPath(entry.ActiveFileName);
            var legacyPath = GetActiveFullPath(entry.LegacyActiveFileName);
            if (File.Exists(destinationPath))
            {
                SynchronizeSaveAliases(entry);
                return true;
            }

            try
            {
                EnsureCategoryPack(entry);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? AlbumManager.SearchPath);
                File.Copy(sourcePath, destinationPath, false);
                if (!PathsEqual(destinationPath, legacyPath) && File.Exists(legacyPath))
                    TryDeleteFile(legacyPath);
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

        public static IReadOnlyList<LibraryAlbumEntry> GetInactiveCategoryEntries(string category)
        {
            return GetCategoryEntries(category)
                .Where(album => !album.IsActive)
                .ToList();
        }

        public static IReadOnlyList<LibraryAlbumEntry> GetActiveCategoryEntries(string category)
        {
            return GetCategoryEntries(category)
                .Where(album => album.IsActive)
                .ToList();
        }

        public static IReadOnlyList<LibraryAlbumEntry> GetCategoryEntries(string category)
        {
            if (string.IsNullOrWhiteSpace(category) ||
                category.Equals("All", StringComparison.OrdinalIgnoreCase) ||
                category.Equals("Active", StringComparison.OrdinalIgnoreCase))
                return Array.Empty<LibraryAlbumEntry>();

            return Search(null, category)
                .ToList();
        }

        public static bool Deactivate(LibraryAlbumEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.ActiveFileName)) return false;

            var activePath = GetActiveFullPath(entry.ActiveFileName);
            var legacyPath = GetActiveFullPath(entry.LegacyActiveFileName);
            if (!File.Exists(activePath) && !File.Exists(legacyPath)) return true;

            try
            {
                SynchronizeSaveAliases(entry);
                TryDeleteFile(activePath);
                if (!PathsEqual(activePath, legacyPath)) TryDeleteFile(legacyPath);
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

        public static IDisposable DeferSave()
        {
            _deferSaveDepth++;
            return new DeferredSaveScope();
        }

        public static bool HasChartMd5(LibraryAlbumEntry entry, string md5)
        {
            return entry?.ChartMd5s != null &&
                   !string.IsNullOrWhiteSpace(md5) &&
                   entry.ChartMd5s.Any(value => value.Equals(md5, StringComparison.OrdinalIgnoreCase));
        }

        public static LibraryAlbumEntry GetByRelativePath(string relativePath)
        {
            relativePath = NormalizeRelativePath(relativePath);
            return Entries.FirstOrDefault(album =>
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
                    var metadata = zip.GetEntry("chart_metadata.json");
                    var hasEditorXml = zip.Entries.Any(entry =>
                        entry.FullName.StartsWith("map", StringComparison.OrdinalIgnoreCase) &&
                        entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
                    Logger.Warning(metadata != null && hasEditorXml
                        ? $"Library album uses unsupported chart_metadata/map.xml format: {relativePath}"
                        : $"Library album has no info.json: {relativePath}");
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
                    LegacyActiveFileName = GetLegacyActiveFileName(relativePath),
                    Info = albumInfo,
                    HasPng = zip.GetEntry("cover.png") != null,
                    HasGif = zip.GetEntry("cover.gif") != null,
                    HasWebp = zip.GetEntry("cover.webp") != null,
                    ChartMd5s = ReadChartMd5s(zip)
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

        private static LibraryAlbumEntry UpdateCachedEntryState(LibraryAlbumEntry cached)
        {
            if (cached == null || string.IsNullOrWhiteSpace(cached.RelativePath)) return null;

            cached.Category = GetCategory(cached.RelativePath);
            cached.FileName = Path.GetFileName(cached.RelativePath);
            cached.ActiveFileName = GetActiveFileName(cached.RelativePath);
            cached.LegacyActiveFileName = GetLegacyActiveFileName(cached.RelativePath);
            cached.ChartMd5s ??= new List<string>();
            return cached;
        }

        private static void SaveCache()
        {
            try
            {
                Directory.CreateDirectory(CacheLocation);
                var cache = new LibraryIndexCache
                {
                    CacheVersion = CacheVersion,
                    Albums = Entries.ToList()
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
                ? DefaultCategory
                : directory.Replace(Path.DirectorySeparatorChar, '/');
        }

        private static string GetActiveFileName(string relativePath)
        {
            var legacyFileName = GetLegacyActiveFileName(relativePath);
            var category = GetCategory(relativePath);
            return category == DefaultCategory
                ? legacyFileName
                : NormalizeRelativePath(Path.Combine(GetSafeCategoryDirectory(category), legacyFileName));
        }

        private static string GetLegacyActiveFileName(string relativePath)
        {
            var name = Path.GetFileNameWithoutExtension(relativePath);
            var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(relativePath.ToLowerInvariant())))[..8];
            return $"{name}_{hash}.mdm";
        }

        private static void EnsureCategoryPack(LibraryAlbumEntry entry)
        {
            if (entry == null || entry.Category == DefaultCategory) return;

            var categoryDirectory = Path.Combine(AlbumManager.SearchPath, GetSafeCategoryDirectory(entry.Category));
            Directory.CreateDirectory(categoryDirectory);

            var packPath = Path.Combine(categoryDirectory, "pack.json");
            if (File.Exists(packPath)) return;

            var packConfig = new
            {
                Title = GetCategoryTitle(entry.Category),
                TitleColorHex = "#ffffff",
                LongTextScroll = true
            };
            File.WriteAllText(packPath, JsonSerializer.Serialize(packConfig, JsonOptions));
        }

        private static string GetCategoryTitle(string category)
        {
            if (string.IsNullOrWhiteSpace(category)) return DefaultCategory;
            var normalized = NormalizeRelativePath(category);
            var index = normalized.LastIndexOf('/');
            return index >= 0 ? normalized[(index + 1)..] : normalized;
        }

        private static string GetSafeCategoryDirectory(string category)
        {
            var parts = NormalizeRelativePath(category)
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(GetSafePathPart)
                .Where(part => !string.IsNullOrWhiteSpace(part));

            return Path.Combine(parts.DefaultIfEmpty(DefaultCategory).ToArray());
        }

        private static string GetSafePathPart(string value)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);

            foreach (var character in value.Trim())
                builder.Append(invalidChars.Contains(character) ? '_' : character);

            var result = builder.ToString().TrimEnd('.', ' ');
            return string.IsNullOrWhiteSpace(result) ? "_" : result;
        }

        private static string GetActiveFullPath(string relativePath)
        {
            return Path.Combine(AlbumManager.SearchPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }

        private static void TryDeleteFile(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        private static void SynchronizeSaveAliases(LibraryAlbumEntry entry)
        {
            var activeAlbumName = GetAlbumName(entry.ActiveFileName);
            var activePackedAlbumName = GetPackedAlbumName(entry);
            var libraryAlbumName = GetAlbumName(entry.FileName);
            var legacyAlbumName = GetAlbumName(entry.LegacyActiveFileName);
            SaveManager.SynchronizeAlbumAliases(activePackedAlbumName, activeAlbumName, libraryAlbumName, legacyAlbumName);
            RequestSave();
        }

        private static void RequestSave()
        {
            if (_deferSaveDepth > 0)
            {
                _savePending = true;
                return;
            }

            SaveManager.SaveSaveFile();
        }

        private static string GetAlbumName(string fileName)
        {
            return $"album_{Path.GetFileNameWithoutExtension(fileName)}";
        }

        private static string GetPackedAlbumName(LibraryAlbumEntry entry)
        {
            var albumName = GetAlbumName(entry.ActiveFileName);
            return entry.Category == DefaultCategory ? albumName : $"{albumName}_{GetCategoryTitle(entry.Category)}";
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

        private static List<string> ReadChartMd5s(ZipArchive zip)
        {
            var md5s = new List<string>();
            foreach (var entry in zip.Entries)
            {
                if (!entry.FullName.StartsWith("map", StringComparison.OrdinalIgnoreCase) ||
                    !entry.FullName.EndsWith(".bms", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    using var stream = entry.Open();
                    md5s.Add(stream.GetHash());
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Failed to calculate library chart MD5 for {entry.FullName}: {ex.Message}");
                }
            }

            return md5s
                .Where(md5 => !string.IsNullOrWhiteSpace(md5))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private sealed class DeferredSaveScope : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _deferSaveDepth = Math.Max(0, _deferSaveDepth - 1);
                if (_deferSaveDepth != 0 || !_savePending) return;

                _savePending = false;
                SaveManager.SaveSaveFile();
            }
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
