using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using CustomAlbums.Data;
using CustomAlbums.Patches;
using CustomAlbums.Utilities;
using HarmonyLib;
using Il2Cpp;
using Il2CppAssets.Scripts.Database;
using Il2CppAssets.Scripts.Database.DataClass;
using Il2CppAssets.Scripts.PeroTools.Commons;
using Il2CppAssets.Scripts.PeroTools.GeneralLocalization;
using Il2CppAssets.Scripts.PeroTools.Managers;
using Il2CppAssets.Scripts.UI.Panels;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppPeroTools2.Resources;
using static Il2CppAssets.Scripts.Database.DBConfigCustomTags;

namespace CustomAlbums.Managers
{
    internal static class HotReloadManager
    {
        private static readonly Logger Logger = new(nameof(HotReloadManager));

        // Localization texts for the notification banner
        private static readonly Dictionary<string, string> AddedTranslations = new()
        {
            { "English", "Added {0} charts" },
            { "ChineseS", "添加了 {0} 张谱面" },
            { "ChineseT", "添加了 {0} 張譜面" },
            { "Japanese", "{0}個の譜面を追加しました" },
            { "Korean", "{0}개의 보면을 추가했습니다" }
        };

        private static readonly Dictionary<string, string> DeletedTranslations = new()
        {
            { "English", "Deleted {0} charts" },
            { "ChineseS", "删除了 {0} 张谱面" },
            { "ChineseT", "删除了 {0} 張譜面" },
            { "Japanese", "{0}個の譜面を削除しました" },
            { "Korean", "{0}개의 보면을 삭제했습니다" }
        };

        // Gets the formatted notification for the current language
        private static string GetLocalizedMessage(Dictionary<string, string> translations, int count)
        {
            var language = SingletonScriptableObject<LocalizationSettings>.instance?.GetActiveOption("Language") ?? "English";
            if (!translations.TryGetValue(language, out var format))
            {
                format = translations["English"];
            }
            return string.Format(format, count);
        }

        // Thread-safe queues for FileSystemWatcher background events
        private static ConcurrentQueue<string> AlbumsToAdd { get; } = new();

        private static ConcurrentQueue<string> AlbumsToDelete { get; } = new();
        private static ConcurrentDictionary<string, DateTime> LastFileEvent { get; } = new();
        private static PnlStage PnlStageInstance { get; set; }

        // Hot-loaded MusicInfo cache: uid -> MusicInfo
        // Used by GetMusicInfoFromAll Harmony Postfix
        private static readonly Dictionary<string, MusicInfo> HotLoadedMusicInfos = new();
        private static readonly Dictionary<string, string> HotLoadedMusicNames = new();
        private static readonly Dictionary<string, string> HotLoadedMusicAuthors = new();

        /// <summary>
        ///     Checks if a file is fully written and no longer locked by other processes.
        /// </summary>
        private static bool IsFileUnlocked(string path)
        {
            try
            {
                using var fileStream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
                return fileStream.Length > 0;
            }
            catch (Exception ex) when (ex is FileNotFoundException or IOException)
            {
                return false;
            }
        }


        private static int ParseDifficulty(string difficulty)
        {
            return int.TryParse(difficulty, out var value) ? value : 0;
        }

        private static MusicExInfo CreateMusicExInfo(Album album)
        {
            var albumInfo = album.Info;
            var changedDiff = new Il2CppStructArray<int>(5);
            changedDiff[0] = ParseDifficulty(albumInfo.Difficulty1);
            changedDiff[1] = ParseDifficulty(albumInfo.Difficulty2);
            changedDiff[2] = ParseDifficulty(albumInfo.Difficulty3);
            changedDiff[3] = ParseDifficulty(albumInfo.Difficulty4);
            changedDiff[4] = ParseDifficulty(albumInfo.Difficulty5);

            var musicExInfo = new MusicExInfo();
            musicExInfo.m_AlbumIndex = AlbumManager.Uid + 1;
            musicExInfo.m_AlbumUidIndex = AlbumManager.Uid;
            musicExInfo.m_MusicIndex = album.Index;
            musicExInfo.m_AlbumUidName = $"music_package_{AlbumManager.Uid}";
            musicExInfo.m_AlbumJsonName = AlbumManager.JsonName;
            musicExInfo.m_ChangedDiff = changedDiff;
            return musicExInfo;
        }

        /// <summary>
        ///     Hot-add: Injects a new .mdm file into the game's runtime database.
        /// </summary>
        private static int ProcessAdditions()
        {
            var addedCount = 0;

            while (AlbumsToAdd.TryDequeue(out var path))
            {
                try
                {
                    // 1. Load album into AlbumManager
                    var album = AlbumManager.LoadOne(path);
                    if (album == null)
                    {
                        Logger.Warning($"HotReload: Failed to load album from {path}");
                        continue;
                    }

                    var albumName = album.AlbumName;
                    var uid = $"{AlbumManager.Uid}-{album.Index}";
                    var albumInfo = album.Info;
                    Logger.Msg($"HotReload: Adding {albumName} (UID: {uid})", false);

                    // 2. Transmute: Native DBObject generation via JSON
                    // Instead of reflection, re-serialize ALL custom albums and let the game natively deserialize them!
                    var masterAlbums = Singleton<ConfigManager>.instance.GetConfigObject<DBConfigAlbums>(-1);
                    var albumsInfo = masterAlbums?.GetAlbumsInfoByUid(AlbumManager.MusicPackage);
                    var globalAlbumConfig = albumsInfo != null 
                        ? Singleton<ConfigManager>.instance.GetConfigObject<DBConfigALBUM>(albumsInfo.albumJsonIndex) 
                        : null;

                    if (globalAlbumConfig != null)
                    {
                        var jsonArray = new JsonArray();
                        var localJsonArray = new JsonArray();

                        var firstSongs = new HashSet<string>(AlbumManager.LoadedAlbums.Values
                            .GroupBy(a => PackManager.GetPackFromUid(a.Uid))
                            .Select(g => g.OrderBy(a => a.Index).First().Uid));

                        foreach (var (albumStr, albumObj) in AlbumManager.LoadedAlbums)
                        {
                            var aInfo = albumObj.Info;
                            var pack = PackManager.GetPackFromUid(albumObj.Uid);
                            var isFirstSong = firstSongs.Contains(albumObj.Uid);
                            var titleString = pack?.Title ?? "Unclassified";

                            var displayName = aInfo.Name ?? "";
                            if (isFirstSong)
                            {
                                var titleConfig = TitleConfigManager.Config;
                                var formatStart = $"{(titleConfig.IsBold ? "<b>" : "")}{(titleConfig.IsItalic ? "<i>" : "")}<color={titleConfig.Color}><size={titleConfig.Size}>";
                                var formatEnd = $"</size></color>{(titleConfig.IsItalic ? "</i>" : "")}{(titleConfig.IsBold ? "</b>" : "")}";
                                displayName = $"{formatStart}【{titleString}】{formatEnd} {aInfo.Name}";
                            }

                            var customChartJson = new
                            {
                                uid = albumObj.Uid,
                                name = displayName,
                                author = aInfo.Author ?? "",
                                bpm = aInfo.Bpm ?? "0",
                                music = $"{albumStr}_music",
                                demo = $"{albumStr}_demo",
                                cover = $"{albumStr}_cover",
                                noteJson = $"{albumStr}_map",
                                scene = aInfo.Scene ?? "scene_01",
                                unlockLevel = "0",
                                levelDesigner = aInfo.LevelDesigner ?? "",
                                levelDesigner1 = aInfo.LevelDesigner1 ?? aInfo.LevelDesigner ?? "",
                                levelDesigner2 = aInfo.LevelDesigner2 ?? aInfo.LevelDesigner ?? "",
                                levelDesigner3 = aInfo.LevelDesigner3 ?? aInfo.LevelDesigner ?? "",
                                levelDesigner4 = aInfo.LevelDesigner4 ?? aInfo.LevelDesigner ?? "",
                                levelDesigner5 = aInfo.LevelDesigner5 ?? aInfo.LevelDesigner ?? "",
                                difficulty1 = aInfo.Difficulty1 ?? "0",
                                difficulty2 = aInfo.Difficulty2 ?? "0",
                                difficulty3 = aInfo.Difficulty3 ?? "0",
                                difficulty4 = aInfo.Difficulty4 ?? "0",
                                difficulty5 = aInfo.Difficulty5 ?? "0"
                            };
                            jsonArray.Add(JsonSerializer.SerializeToNode(customChartJson));

                            localJsonArray.Add(JsonSerializer.SerializeToNode(new
                            {
                                name = displayName,
                                author = aInfo.Author ?? ""
                            }));
                        }

                        var fullJsonStr = JsonSerializer.Serialize(jsonArray);
                        var fullLocalJsonStr = JsonSerializer.Serialize(localJsonArray);

                        // 1. Re-deserialize the entire list of custom albums into the global config
                        globalAlbumConfig.Deserialize(fullJsonStr);
                        
                        // 2. Bypass engine cache completely by manually instantiating and injecting the localized databases
                        var localDicProp = typeof(BaseDBConfigLocalObject<DBConfigLocalALBUM, LocalALBUMInfo>).GetProperty("m_LocalDic", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (localDicProp != null)
                        {
                            var globalLocalDic = localDicProp.GetValue(globalAlbumConfig) as Il2CppSystem.Collections.Generic.Dictionary<int, DBConfigLocalALBUM>;
                            if (globalLocalDic != null)
                            {
                                globalLocalDic.Clear();
                                for (int i = 0; i <= 15; i++)
                                {
                                    var newLocalAlbum = new DBConfigLocalALBUM();
                                    newLocalAlbum.Deserialize(fullLocalJsonStr);
                                    globalLocalDic.Add(i, newLocalAlbum);
                                }
                            }
                        }

                        // 4. Update the global dictionaries
                        GlobalDataBase.s_DbMusicTag.AddAllMusicInfo(globalAlbumConfig);

                        // 4. Re-initialize ExInfo for all the newly created MusicInfo objects
                        var newMusicInfoList = new Il2CppSystem.Collections.Generic.List<MusicInfo>();
                        globalAlbumConfig.GetAllMusicInfo(newMusicInfoList);
                        int idx = 0;
                        foreach (var m in newMusicInfoList)
                        {
                            var uidSplit = m.uid.Split('-');
                            if (uidSplit.Length < 2) { idx++; continue; }
                            if (!int.TryParse(uidSplit[1], out var parsedIndex)) { idx++; continue; }

                            var aObj = AlbumManager.LoadedAlbums.Values.FirstOrDefault(a => a.Index == parsedIndex);
                            if (aObj != null)
                            {
                                m.Init(idx);
                                m.InitExInfo();
                                m.m_MusicExInfo = CreateMusicExInfo(aObj);

                                HotLoadedMusicInfos[m.uid] = m;
                                HotLoadedMusicNames[m.uid] = aObj.Info.Name ?? "";
                                HotLoadedMusicAuthors[m.uid] = aObj.Info.Author ?? "";
                            }
                            idx++;
                        }

                        Logger.Msg($"HotReload: Natively transmuted MusicInfo for {uid}", false);
                    }
                    else
                    {
                        Logger.Error("HotReload: globalAlbumConfig is null! Cannot inject metadata.");
                    }

                    // Add to the front-end view list so UI updates correctly
                    try
                    {
                        var dhColBase = Il2CppAssets.Scripts.Database.DataHelper.collections;
                        var dhColPtr = dhColBase != null ? dhColBase.Pointer : IntPtr.Zero;
                        var uids = GlobalDataBase.s_DbMusicTag.m_StageShowMusicUids;
                        if (uids != null)
                        {
                            if (!uids.Contains(uid))
                            {
                                // Only add if it's not currently displaying the collections list to avoid alias pollution
                                if (dhColPtr == IntPtr.Zero || uids.Pointer != dhColPtr)
                                {
                                    uids.Add(uid);
                                    Logger.Msg($"HotReload: Added {uid} to m_StageShowMusicUids", false);
                                }
                            }
                        }
                        // Add to All Music tag (Index 0)

                        var allMusicTag = GlobalDataBase.dbMusicTag.GetAlbumTagInfo(0);
                        if (allMusicTag != null)
                        {
                            if (allMusicTag.m_MusicUids != null && !allMusicTag.m_MusicUids.Contains(uid))
                            {
                                if (allMusicTag.m_MusicUids.Pointer != dhColPtr)
                                    allMusicTag.m_MusicUids.Add(uid);
                            }
                            if (allMusicTag.m_DisplayMusicUids != null)
                            {
                                foreach (var d in allMusicTag.m_DisplayMusicUids)
                                {
                                    if (d.musicUids != null && !d.musicUids.Contains(uid))
                                    {
                                        if (d.musicUids.Pointer != dhColPtr)
                                            d.musicUids.Add(uid);
                                    }
                                }
                            }
                        }

                        // 5. Removed dicLevelConfig injection. It was causing Headquarters to crash by supplying an incorrect difficulty level.
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"HotReload: Failed to add to view lists: {ex.Message}");
                    }
                    
                    // 3. Inject search tags into ConfigManager
                    try
                    {
                        var config = Singleton<ConfigManager>.instance.GetConfigObject<DBConfigMusicSearchTag>();
                        var searchTag = new MusicSearchTagInfo
                        {
                            uid = uid,
                            listIndex = config.count
                        };

                        var tags = new List<string> { "custom albums" };
                        if (albumInfo.SearchTags != null) tags.AddRange(albumInfo.SearchTags);
                        if (!string.IsNullOrEmpty(albumInfo.NameRomanized)) tags.Add(albumInfo.NameRomanized);
                        for (var i = 0; i < tags.Count; i++) tags[i] = tags[i].ToLower();
                        searchTag.tag = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray(tags.ToArray());

                        if (!config.m_Dictionary.ContainsKey(uid))
                        {
                            config.m_Dictionary.Add(uid, searchTag);
                            config.list.Add(searchTag);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"HotReload: Failed to add search tags: {ex.Message}");
                    }

                    // 4. Preload cover resources
                    try
                    {
                        if (album.HasFile("cover.png") || album.HasFile("cover.gif"))
                        {
                            ResourcesManager.instance
                                .LoadFromName<UnityEngine.Sprite>($"{albumName}_cover")
                                .hideFlags |= UnityEngine.HideFlags.DontUnloadUnusedAsset;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"HotReload: Failed to preload cover: {ex.Message}");
                    }

                    // UI Hijack will handle rendering the name and author without needing these native injections.
                    addedCount++;
                    Logger.Msg($"HotReload: Successfully added {albumInfo.Name}", false);
                }
                catch (Exception ex)
                {
                    Logger.Warning($"HotReload: Error adding album: {ex.Message}");
                    Logger.Warning(ex.StackTrace);
                }
            }

            return addedCount;
        }

        /// <summary>
        ///     Hot-delete: Removes a specified album from the game's runtime database.
        /// </summary>
        private static int ProcessDeletions()
        {
            var deletedCount = 0;

            while (AlbumsToDelete.TryDequeue(out var albumFileName))
            {
                try
                {
                    var albumKey = $"album_{albumFileName}";
                    Logger.Msg($"HotReload: Removing {albumKey}", false);

                    if (!AlbumManager.LoadedAlbums.TryGetValue(albumKey, out var album))
                    {
                        Logger.Warning($"HotReload: Album {albumKey} not found");
                        continue;
                    }

                    var uid = $"{AlbumManager.Uid}-{album.Index}";

                    HotLoadedMusicInfos.Remove(uid);
                    HotLoadedMusicNames.Remove(uid);
                    HotLoadedMusicAuthors.Remove(uid);

                    // Remove from game music database
                    try
                    {
                        var musicInfo = GlobalDataBase.s_DbMusicTag.GetMusicInfoFromAll(uid);
                        if (musicInfo != null)
                        {
                            GlobalDataBase.s_DbMusicTag.RemoveShowMusicUid(musicInfo);
                        }
                        
                        var allMusicInfo = GlobalDataBase.s_DbMusicTag.m_AllMusicInfo;
                        if (allMusicInfo != null && allMusicInfo.ContainsKey(uid))
                        {
                            allMusicInfo.Remove(uid);
                        }
                        
                        // Remove from all tags (like "All Music" or "Favorites")
                        if (GlobalDataBase.s_DbMusicTag.m_AllAlbumTagData != null)
                        {
                            foreach (var tag in GlobalDataBase.s_DbMusicTag.m_AllAlbumTagData)
                            {
                                var tagInfo = tag.Value;
                                if (tagInfo.m_MusicUids != null && tagInfo.m_MusicUids.Contains(uid))
                                {
                                    tagInfo.m_MusicUids.Remove(uid);
                                }
                                if (tagInfo.m_DisplayMusicUids != null)
                                {
                                    foreach (var d in tagInfo.m_DisplayMusicUids)
                                    {
                                        if (d.musicUids != null && d.musicUids.Contains(uid))
                                        {
                                            d.musicUids.Remove(uid);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"HotReload: Failed to remove from DB: {ex.Message}");
                    }

                    // Remove from ConfigManager
                    try
                    {
                        var config = Singleton<ConfigManager>.instance.GetConfigObject<DBConfigMusicSearchTag>();
                        if (config != null)
                        {
                            if (config.m_Dictionary.ContainsKey(uid))
                            {
                                var tagInfo = config.m_Dictionary[uid];
                                config.m_Dictionary.Remove(uid);
                                config.list.Remove(tagInfo);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"HotReload: Failed to remove search tags: {ex.Message}");
                    }

                    // Clear resource cache
                    AlbumManager.LoadedAlbums.Remove(albumKey);
                    CoverManager.CachedAnimatedCovers.Remove(album.Index);
                    CoverManager.CachedCovers.Remove(album.Index);
                    AssetPatch.RemoveFromCache($"{albumKey}_demo");
                    AssetPatch.RemoveFromCache($"{albumKey}_music");
                    AssetPatch.RemoveFromCache($"{albumKey}_cover");

                    deletedCount++;
                    Logger.Msg($"HotReload: Removed {albumKey}", false);
                }
                catch (Exception ex)
                {
                    Logger.Warning($"HotReload: Error removing: {ex.Message}");
                    Logger.Warning(ex.StackTrace);
                }
            }

            return deletedCount;
        }

        /// <summary>
        ///     Rebuild Custom Albums tag to update the UID list.
        /// </summary>
        private static void RebuildCustomAlbumsTag()
        {
            try
            {
                var existingTag = GlobalDataBase.dbMusicTag.GetAlbumTagInfo(AlbumManager.Uid);
                if (existingTag != null)
                {
                    var uids = AlbumManager.GetAllUid().ToList();
                    
                    if (existingTag.customInfo != null)
                    {
                        existingTag.customInfo.music_list = uids.ToIl2Cpp();
                    }
                    
                    if (existingTag.m_MusicUids != null)
                    {
                        existingTag.m_MusicUids.Clear();
                        foreach (var uid in uids) existingTag.m_MusicUids.Add(uid);
                    }
                    
                    if (existingTag.m_DisplayMusicUids != null)
                    {
                        foreach (var d in existingTag.m_DisplayMusicUids)
                        {
                            if (d.musicUids != null)
                            {
                                d.musicUids.Clear();
                                foreach (var uid in uids) d.musicUids.Add(uid);
                            }
                        }
                    }
                    
                    Logger.Msg($"HotReload: Tag updated ({uids.Count} albums)", false);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"HotReload: Tag rebuild failed: {ex.Message}");
            }
        }

        private static void RefreshUI()
        {
            try
            {
                var stage = UnityEngine.Object.FindObjectOfType<Il2CppAssets.Scripts.UI.Panels.PnlStage>();
                if (stage != null && stage.gameObject.activeInHierarchy)
                {
                    stage.RefreshStageUI();
                    Logger.Msg("HotReload: UI refreshed (RefreshStageUI)", false);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"HotReload: Failed to refresh UI: {ex.Message}");
            }
        }

        /// <summary>
        ///     Consume the queue in Unity's FixedUpdate (main thread safe).
        /// </summary>
        internal static void FixedUpdate()
        {
            if (AlbumsToAdd.IsEmpty && AlbumsToDelete.IsEmpty) return;

            if (PnlStageInstance == null) return;

            Logger.Msg($"HotReload: Processing queue (add={AlbumsToAdd.Count}, del={AlbumsToDelete.Count})", false);
            
            var oldSelectedUid = DataHelper.selectedMusicUidFromInfoList;
            var oldSelectedAlbumName = AlbumManager.GetAlbumNameFromUid(oldSelectedUid);

            var deletedCount = ProcessDeletions();
            var addedCount = ProcessAdditions();

            if (deletedCount > 0 || addedCount > 0)
            {
                if (addedCount > 0)
                {
                    Il2CppAssets.Scripts.UI.Controls.ShowText.ShowInfo(GetLocalizedMessage(AddedTranslations, addedCount));
                }
                if (deletedCount > 0)
                {
                    Il2CppAssets.Scripts.UI.Controls.ShowText.ShowInfo(GetLocalizedMessage(DeletedTranslations, deletedCount));
                }

                if (!string.IsNullOrEmpty(oldSelectedAlbumName) && oldSelectedUid != "0-0")
                {
                    if (AlbumManager.LoadedAlbums.TryGetValue(oldSelectedAlbumName, out var newAlbum))
                    {
                        var newUid = $"{AlbumManager.Uid}-{newAlbum.Index}";
                        if (newUid != oldSelectedUid)
                        {
                            DataHelper.selectedMusicUidFromInfoList = newUid;
                            Logger.Msg($"HotReload: Updated selected UID from {oldSelectedUid} to {newUid}", false);
                        }
                    }
                    else
                    {
                        DataHelper.selectedMusicUidFromInfoList = "0-0";
                        Logger.Msg($"HotReload: Selected album was deleted. Resetting selection to 0-0", false);
                    }
                }

                // Registers the hidden difficulty of the hot-reloaded chart
                if (addedCount > 0)
                {
                    try
                    {
                        HiddenSupportPatch.RegisterHiddenChartsDirectly();
                        Logger.Msg("HotReload: Hidden charts registered directly", false);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"HotReload: Failed to register hidden charts: {ex.Message}");
                    }
                }

                RebuildCustomAlbumsTag();
                RefreshUI();

                // Compatibility with HiddenQoL: if one-key toggle is enabled, automatically trigger after hot reload
                try
                {
                    var hiddenQolAssembly = System.AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => a.GetName().Name == "HiddenQol_Fixed" || a.GetName().Name == "HiddenQol");
                    
                    if (hiddenQolAssembly != null)
                    {
                        var saveType = hiddenQolAssembly.GetType("HiddenQol.Save");
                        var settingProp = saveType?.GetProperty("Setting", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        
                        if (settingProp != null)
                        {
                            var settingObj = settingProp.GetValue(null);
                            var qolEnabledField = settingObj?.GetType().GetField("QolEnabled", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                            
                            if (qolEnabledField != null && (bool)qolEnabledField.GetValue(settingObj))
                            {
                                var qolManagerType = hiddenQolAssembly.GetType("HiddenQol.Managers.QoLManager");
                                var activateMethod = qolManagerType?.GetMethod("ActivateAllHidden", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                                
                                if (activateMethod != null)
                                {
                                    activateMethod.Invoke(null, null);
                                    Logger.Msg("HotReload: Triggered HiddenQoL ActivateAllHidden", false);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"HotReload: Failed to trigger HiddenQoL: {ex.Message}");
                }
            }
        }

        /// <summary>
        ///     Initialize FileSystemWatcher to monitor Custom_Albums directory changes.
        /// </summary>
        internal static void OnLateInitializeMelon()
        {
            try
            {

                var watchPath = Path.GetFullPath(AlbumManager.SearchPath);
                Logger.Msg($"HotReload: Watching directory: {watchPath}", false);

                if (!Directory.Exists(watchPath))
                {
                    Logger.Warning($"HotReload: Directory does not exist: {watchPath}");
                    return;
                }

                AlbumManager.AlbumWatcher.Path = watchPath;
                AlbumManager.AlbumWatcher.Filter = AlbumManager.SearchPattern;

                AlbumManager.AlbumWatcher.Created += (_, e) =>
                {
                    var now = DateTime.Now;
                    if (LastFileEvent.TryGetValue(e.FullPath, out var lastTime) && (now - lastTime).TotalMilliseconds < 500) return;
                    LastFileEvent[e.FullPath] = now;

                    Logger.Msg($"HotReload: Detected new file: {e.Name}", false);
                    Task.Run(() =>
                    {
                        var attempts = 0;
                        while (!IsFileUnlocked(e.FullPath) && attempts < 50)
                        {
                            Thread.Sleep(200);
                            attempts++;
                        }

                        if (attempts < 50)
                        {
                            AlbumsToAdd.Enqueue(e.FullPath);
                            Logger.Msg($"HotReload: Queued for addition: {e.Name}", false);
                        }
                        else
                        {
                            Logger.Warning($"HotReload: Timed out waiting for file: {e.Name}");
                        }
                    });
                };

                AlbumManager.AlbumWatcher.Deleted += (_, e) =>
                {
                    var now = DateTime.Now;
                    if (LastFileEvent.TryGetValue(e.FullPath, out var lastTime) && (now - lastTime).TotalMilliseconds < 500) return;
                    LastFileEvent[e.FullPath] = now;

                    Logger.Msg($"HotReload: Detected deletion: {e.Name}", false);
                    AlbumsToDelete.Enqueue(Path.GetFileNameWithoutExtension(e.Name));
                };

                AlbumManager.AlbumWatcher.Changed += (_, e) =>
                {
                    if (e.ChangeType != WatcherChangeTypes.Changed) return;
                    var now = DateTime.Now;
                    if (LastFileEvent.TryGetValue(e.FullPath, out var lastTime) && (now - lastTime).TotalMilliseconds < 500) return;
                    LastFileEvent[e.FullPath] = now;

                    Logger.Msg($"HotReload: Detected change: {e.Name}", false);
                    Task.Run(() =>
                    {
                        var attempts = 0;
                        while (!IsFileUnlocked(e.FullPath) && attempts < 50)
                        {
                            Thread.Sleep(200);
                            attempts++;
                        }

                        if (attempts < 50)
                        {
                            AlbumsToDelete.Enqueue(Path.GetFileNameWithoutExtension(e.Name));
                            AlbumsToAdd.Enqueue(e.FullPath);
                            Logger.Msg($"HotReload: Queued for reload: {e.Name}", false);
                        }
                        else
                        {
                            Logger.Warning($"HotReload: Timed out waiting for file: {e.Name}");
                        }
                    });
                };

                AlbumManager.AlbumWatcher.Renamed += (_, e) =>
                {
                    var now = DateTime.Now;
                    if (LastFileEvent.TryGetValue(e.FullPath, out var lastTime) && (now - lastTime).TotalMilliseconds < 500) return;
                    LastFileEvent[e.FullPath] = now;

                    Logger.Msg($"HotReload: Detected rename: {e.OldName} -> {e.Name}", false);
                    var oldKey = $"album_{Path.GetFileNameWithoutExtension(e.OldName)}";
                    var newKey = $"album_{Path.GetFileNameWithoutExtension(e.Name)}";

                    if (AlbumManager.LoadedAlbums.Remove(oldKey, out var album))
                    {
                        AlbumManager.LoadedAlbums.TryAdd(newKey, album);
                        AssetPatch.ModifyCacheKey($"{oldKey}_demo", $"{newKey}_demo");
                        AssetPatch.ModifyCacheKey($"{oldKey}_music", $"{newKey}_music");
                        AssetPatch.ModifyCacheKey($"{oldKey}_cover", $"{newKey}_cover");
                        Logger.Msg($"HotReload: Renamed {oldKey} -> {newKey}", false);
                    }
                    else
                    {
                        // Old album was not loaded (e.g. didn't match 'test*.mdm'). Treat this rename as a new creation event.
                        Logger.Msg($"HotReload: Old file was not loaded, treating as new file: {e.Name}", false);
                        Task.Run(() =>
                        {
                            var attempts = 0;
                            while (!IsFileUnlocked(e.FullPath) && attempts < 50)
                            {
                                Thread.Sleep(200);
                                attempts++;
                            }

                            if (attempts < 50)
                            {
                                AlbumsToAdd.Enqueue(e.FullPath);
                                Logger.Msg($"HotReload: Queued for addition: {e.Name}", false);
                            }
                            else
                            {
                                Logger.Warning($"HotReload: Timed out waiting for file: {e.Name}");
                            }
                        });
                    }
                };

                AlbumManager.AlbumWatcher.EnableRaisingEvents = true;
                Logger.Msg("HotReload: FileSystemWatcher initialized!", false);
            }
            catch (Exception ex)
            {
                Logger.Warning($"HotReload: Init failed: {ex.Message}");
                Logger.Warning(ex.StackTrace);
            }
        }

        // ============================================================
        // Harmony Patches
        // ============================================================

        /// <summary>
        ///     Intercepts GetMusicInfoFromAll: Returns our manually created MusicInfo
        ///     when the game looks up a hot-loaded song.
        /// </summary>
        [HarmonyPatch(typeof(DBMusicTag), nameof(DBMusicTag.GetMusicInfoFromAll))]
        internal static class GetMusicInfoFromAllPatch
        {
            private static void Postfix(string musicUid, ref MusicInfo __result)
            {
                if (string.IsNullOrEmpty(musicUid)) return;
                
                if (HotLoadedMusicInfos.TryGetValue(musicUid, out var hotMusicInfo))
                {
                    __result = hotMusicInfo;
                }
            }
        }







        /// <summary>
        ///     Capture PnlStage instance reference.
        /// </summary>
        [HarmonyPatch(typeof(PnlStage), nameof(PnlStage.PreWarm))]
        internal static class StagePreWarmPatch
        {
            private static void Postfix(PnlStage __instance)
            {
                PnlStageInstance = __instance;
            }
        }
    }
}
