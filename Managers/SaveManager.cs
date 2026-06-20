using System.Text;
using System.Text.Json;
using CustomAlbums.Data;
using CustomAlbums.Utilities;
using System.IO.Compression;

namespace CustomAlbums.Managers
{
    public class SaveManager
    {
        private const string SaveLocation = "UserData";
        public static CustomAlbumsSave SaveData { get; private set; }
        internal static Logger Logger = new(nameof(SaveManager));
        internal static string PreviousScore { get; set; } = "-";

        internal static void SynchronizeAlbumAliases(string albumName, params string[] extraAliases)
        {
            if (!ModSettings.SavingEnabled || SaveData == null) return;

            var aliases = GetAlbumNameAliases(albumName)
                .Concat(extraAliases.SelectMany(GetAlbumNameAliases))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (aliases.Count <= 1) return;

            MergeHighestForAliases(aliases);
            MergeFullComboForAliases(aliases);
            SynchronizeSetAliases(SaveData.UnlockedMasters, aliases);
            SynchronizeSetAliases(SaveData.Collections, aliases);
            SynchronizeSetAliases(SaveData.Hides, aliases);
            SynchronizeHistoryAliases(aliases);

            if (aliases.Any(alias => alias.Equals(SaveData.SelectedAlbum, StringComparison.OrdinalIgnoreCase)))
                SaveData.SelectedAlbum = ResolveLoadedAlbumName(SaveData.SelectedAlbum);
        }

        internal static Dictionary<int, ChartSave> GetHighestForAlbum(string albumName)
        {
            if (SaveData == null) return null;
            return MergeHighestForAliases(GetAlbumNameAliases(albumName).ToList());
        }

        internal static List<int> GetFullComboForAlbum(string albumName)
        {
            if (SaveData == null) return null;
            return MergeFullComboForAliases(GetAlbumNameAliases(albumName).ToList());
        }

        internal static bool IsMasterUnlocked(string albumName)
        {
            return SaveData != null && GetAlbumNameAliases(albumName).Any(alias => SaveData.UnlockedMasters.Contains(alias));
        }

        internal static void AddAlbumFlag(HashSet<string> set, string albumName)
        {
            foreach (var alias in GetAlbumNameAliases(albumName))
                set.Add(alias);
        }

        internal static void RemoveAlbumFlag(HashSet<string> set, string albumName)
        {
            foreach (var alias in GetAlbumNameAliases(albumName))
                set.Remove(alias);
        }

        internal static void AddAlbumHistory(string albumName)
        {
            var aliases = GetAlbumNameAliases(albumName).ToList();
            SaveData.History.RemoveAll(history => aliases.Contains(history, StringComparer.OrdinalIgnoreCase));
            SaveData.History.Add(albumName);

            if (SaveData.History.Count > 10)
                SaveData.History.RemoveAt(0);
        }

        internal static string ResolveLoadedAlbumName(string albumName)
        {
            if (string.IsNullOrWhiteSpace(albumName)) return albumName;
            if (AlbumManager.LoadedAlbums.ContainsKey(albumName)) return albumName;

            var baseAlbumName = GetLibraryBaseAlbumName(albumName);
            return AlbumManager.LoadedAlbums.Keys.FirstOrDefault(name =>
                GetLibraryBaseAlbumName(name).Equals(baseAlbumName, StringComparison.OrdinalIgnoreCase)) ?? albumName;
        }

        private static Dictionary<int, ChartSave> GetOrCreateHighestForAlbum(string albumName)
        {
            var aliases = GetAlbumNameAliases(albumName).ToList();
            var highest = MergeHighestForAliases(aliases) ?? new Dictionary<int, ChartSave>();
            foreach (var alias in aliases)
                SaveData.Highest[alias] = highest;
            return highest;
        }

        private static List<int> GetOrCreateFullComboForAlbum(string albumName)
        {
            var aliases = GetAlbumNameAliases(albumName).ToList();
            var fullCombo = MergeFullComboForAliases(aliases) ?? new List<int>();
            foreach (var alias in aliases)
                SaveData.FullCombo[alias] = fullCombo;
            return fullCombo;
        }

        private static IEnumerable<string> GetAlbumNameAliases(string albumName)
        {
            if (string.IsNullOrWhiteSpace(albumName)) yield break;

            yield return albumName;

            var baseAlbumName = GetLibraryBaseAlbumName(albumName);
            if (!baseAlbumName.Equals(albumName, StringComparison.OrdinalIgnoreCase))
                yield return baseAlbumName;
        }

        private static string GetLibraryBaseAlbumName(string albumName)
        {
            if (string.IsNullOrWhiteSpace(albumName)) return albumName;

            var separator = albumName.LastIndexOf('_');
            if (separator <= "album_".Length) return albumName;

            var suffix = albumName[(separator + 1)..];
            return suffix.Length == 8 && suffix.All(IsHexDigit)
                ? albumName[..separator]
                : albumName;
        }

        private static bool IsHexDigit(char value)
        {
            return value is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
        }

        private static Dictionary<int, ChartSave> MergeHighestForAliases(IReadOnlyList<string> aliases)
        {
            Dictionary<int, ChartSave> merged = null;

            foreach (var alias in aliases)
            {
                if (!SaveData.Highest.TryGetValue(alias, out var highest)) continue;

                if (merged == null)
                {
                    merged = highest;
                    continue;
                }

                foreach (var (difficulty, save) in highest)
                {
                    if (merged.TryGetValue(difficulty, out var existing))
                        MergeChartSave(existing, save);
                    else
                        merged[difficulty] = save;
                }
            }

            if (merged == null) return null;

            foreach (var alias in aliases)
                SaveData.Highest[alias] = merged;

            return merged;
        }

        private static List<int> MergeFullComboForAliases(IReadOnlyList<string> aliases)
        {
            List<int> merged = null;

            foreach (var alias in aliases)
            {
                if (!SaveData.FullCombo.TryGetValue(alias, out var fullCombo)) continue;

                if (merged == null)
                {
                    merged = fullCombo;
                    continue;
                }

                foreach (var difficulty in fullCombo.Where(difficulty => !merged.Contains(difficulty)))
                    merged.Add(difficulty);
            }

            if (merged == null) return null;

            foreach (var alias in aliases)
                SaveData.FullCombo[alias] = merged;

            return merged;
        }

        private static void SynchronizeSetAliases(HashSet<string> set, IReadOnlyList<string> aliases)
        {
            if (!aliases.Any(set.Contains)) return;

            foreach (var alias in aliases)
                set.Add(alias);
        }

        private static void SynchronizeHistoryAliases(IReadOnlyList<string> aliases)
        {
            var historyIndex = SaveData.History.FindLastIndex(history => aliases.Contains(history, StringComparer.OrdinalIgnoreCase));
            if (historyIndex < 0) return;

            SaveData.History.RemoveAll(history => aliases.Contains(history, StringComparer.OrdinalIgnoreCase));
            SaveData.History.Insert(Math.Min(historyIndex, SaveData.History.Count), ResolveLoadedAlbumName(aliases[0]));
        }

        private static void MergeChartSave(ChartSave target, ChartSave source)
        {
            if (target == null || source == null) return;

            target.Passed |= source.Passed;
            target.Accuracy = Math.Max(target.Accuracy, source.Accuracy);
            target.Score = Math.Max(target.Score, source.Score);
            target.Combo = Math.Max(target.Combo, source.Combo);
            target.Evaluate = Math.Max(target.Evaluate, source.Evaluate);
            target.Clear = Math.Max(target.Clear, source.Clear);
            target.FailCount = Math.Max(target.FailCount, source.FailCount);
            target.AccuracyStr = (target.Accuracy / 100).ToStringInvariant("P2");
        }

        /// <summary>
        ///     Fixes the save file since this version of CAM uses a different naming scheme.
        ///     This allows cross-compatibility between CAM 3 and CAM 4, but not from CAM 4 to CAM 3.
        /// </summary>
        internal static void FixSaveFile()
        {
            if (!ModSettings.SavingEnabled) return;
            var firstHistory = SaveData.History.FirstOrDefault();
            var firstHighest = SaveData.Highest.FirstOrDefault();
            var firstFullCombo = SaveData.FullCombo.FirstOrDefault();
            var stringBuilder = new StringBuilder();

            // If we need to fix the history
            if (firstHistory != null && firstHistory.StartsWith("pkg_"))
            {
                var fixedList = new List<string>(SaveData.History.Count);
                foreach (var history in SaveData.History.Where(history => history.StartsWith("pkg_")))
                {
                    stringBuilder.Clear();
                    stringBuilder.Append(history);
                    stringBuilder.Remove(0, 4);
                    stringBuilder.Insert(0, "album_");
                    fixedList.Add(stringBuilder.ToString());
                }

                SaveData.History = fixedList;
            }

            // If we need to fix the highest
            if (!firstHighest.Equals(default(KeyValuePair<string, Dictionary<int, ChartSave>>)) &&
                firstHighest.Key.StartsWith("pkg_"))
            {
                var fixedDictionaryHighest =
                    new Dictionary<string, Dictionary<int, ChartSave>>(SaveData.Highest.Count);
                foreach (var (key, value) in SaveData.Highest.Where(kv => kv.Key.StartsWith("pkg_")))
                {
                    stringBuilder.Clear();
                    stringBuilder.Append(key);
                    stringBuilder.Remove(0, 4);
                    stringBuilder.Insert(0, "album_");
                    fixedDictionaryHighest.Add(stringBuilder.ToString(), value);
                }

                SaveData.Highest = fixedDictionaryHighest;
            }

            if (!SaveData.UnlockedMasters.Any()) 
            {
                var unlockedHighest = SaveData.Highest.Where(kv =>
                    kv.Value.ContainsKey(3) && kv.Value.TryGetValue(2, out var chartSave) && chartSave.Evaluate >= 4).Select(kv => kv.Key);
                var folderCharts = AlbumManager.LoadedAlbums.Where(kv => kv.Value.HasDifficulty(2) && kv.Value.HasDifficulty(3) && !kv.Value.IsPackaged).Select(kv => kv.Key);
                var concat = unlockedHighest.Concat(folderCharts);
                SaveData.UnlockedMasters.UnionWith(concat);
            }

            // If we don't need to fix the FullCombo then return
            if (!firstFullCombo.Equals(default(KeyValuePair<string, List<int>>)) &&
                !firstFullCombo.Key.StartsWith("pkg_")) return;

            var fixedDictionaryFc = new Dictionary<string, List<int>>(SaveData.FullCombo.Count);
            foreach (var (key, value) in SaveData.FullCombo.Where(kv => kv.Key.StartsWith("pkg_")))
            {
                if (!key.StartsWith("pkg_")) continue;
                stringBuilder.Clear();
                stringBuilder.Append(key);
                stringBuilder.Remove(0, 4);
                stringBuilder.Insert(0, "album_");
                fixedDictionaryFc.Add(stringBuilder.ToString(), value);
            }

            SaveData.FullCombo = fixedDictionaryFc;
        }

        private static void RestoreBackup()
        {
            var backupPath = Path.Join(SaveLocation, "Backups", "Backups.zip");
            
            // If a backup does not exist at all
            if (!File.Exists(backupPath))
            {
                Logger.Fail("No backups found. Please delete the CustomAlbums.json file in UserData folder to create a new save.");
                return;
            }

            // Traverse the .zip file, trying every single backup in this archive
            using var backupEntries = ZipFile.OpenRead(backupPath);
            foreach (var backup in backupEntries.Entries.OrderByDescending(bak => bak.LastWriteTime)
                         .Where(bak => bak.Name.EndsWith("CustomAlbums.json.bak")))
            {
                try
                {
                    SaveData = Json.Deserialize<CustomAlbumsSave>(backup.Open());
                }
                catch (Exception)
                {
                    continue;
                }
                
                // If our backup that we are trying to load is valid but empty, continue to try and find the last save with data in it
                if (SaveData.IsEmpty()) continue;
                Logger.Success($"Restored backup from {backup.LastWriteTime.DateTime}.");
                return;
            }
            Logger.Fail("Could not restore save file. Please delete the CustomAlbums.json file in UserData folder to create a new save.");
        }

        internal static void LoadSaveFile()
        {
            if (!ModSettings.SavingEnabled) return;
            try
            {
                using var fileStream = File.OpenRead(Path.Join(SaveLocation, "CustomAlbums.json"));
                SaveData = Json.Deserialize<CustomAlbumsSave>(fileStream);
                FixSaveFile();
            }
            catch (Exception ex)
            {
                if (ex is FileNotFoundException)
                {
                    SaveData = new CustomAlbumsSave();
                }
                else
                {
                    Logger.Warning("Could not load save file. Attempting to restore backup...");
                    RestoreBackup();
                }
            }
        }

        internal static void SaveSaveFile()
        {
            if (!ModSettings.SavingEnabled) return;
            try
            {
                if (SaveData is null)
                {
                    Logger.Warning("Trying to save null data, not saving.");
                    return;
                }

                File.WriteAllText(Path.Join(SaveLocation, "CustomAlbums.json"), JsonSerializer.Serialize(SaveData));
            }
            catch (Exception ex)
            {
                Logger.Warning("Failed to save save file. " + ex.StackTrace);
            }
        }

        /// <summary>
        /// Saves custom score given scoring information.
        /// </summary>
        /// <param name="uid">The UID of the chart.</param>
        /// <param name="musicDifficulty">The difficulty index of the chart played.</param>
        /// <param name="score">The score of the play.</param>
        /// <param name="accuracy">The accuracy of the play.</param>
        /// <param name="maxCombo">The maximum combo of the play.</param>
        /// <param name="evaluate">The judgement ranking of the play.</param>
        /// <param name="miss">The amount of misses in the play.</param>
        internal static void SaveScore(string uid, int musicDifficulty, int score, float accuracy, int maxCombo,
            string evaluate, int miss)
        {
            if (!ModSettings.SavingEnabled) return;

            var album = AlbumManager.GetByUid(uid);
            if (!album?.IsPackaged ?? true) return;

            var newEvaluate = evaluate switch
            {
                "sss" => 6,
                "ss" => 5,
                "s" => 4,
                "a" => 3,
                "b" => 2,
                "c" => 1,
                _ => 0
            };

            var albumName = album.AlbumName;
            var currChartScore = GetOrCreateHighestForAlbum(albumName);

            // Create new save data if the difficulty doesn't exist
            currChartScore.TryAdd(musicDifficulty, new ChartSave());

            // Set previous score for PnlVictory logic
            var newScore = currChartScore[musicDifficulty];
            PreviousScore = newScore.Passed ? newScore.Score.ToString() : "-";

            // Set the correct new score, taking the max of everything
            newScore.Passed = true;
            newScore.Accuracy = Math.Max(accuracy, newScore.Accuracy);
            newScore.Score = Math.Max(score, newScore.Score);
            newScore.Combo = Math.Max(maxCombo, newScore.Combo);
            newScore.Evaluate = Math.Max(newEvaluate, newScore.Evaluate);
            newScore.AccuracyStr = (newScore.Accuracy / 100).ToStringInvariant("P2");
            newScore.Clear++;

            if (musicDifficulty is 2 && AlbumManager.LoadedAlbums[albumName].HasDifficulty(3) && newScore.Evaluate >= 4)
                AddAlbumFlag(SaveData.UnlockedMasters, albumName);

            // If there were no misses then add the chart/difficulty to the FullCombo list
            if (miss != 0) return;

            var fullCombo = GetOrCreateFullComboForAlbum(albumName);

            if (!fullCombo.Contains(musicDifficulty))
                fullCombo.Add(musicDifficulty);
        }
    }
}
