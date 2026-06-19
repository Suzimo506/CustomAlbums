# CustomAlbums 崩溃优化计划

## 背景

CustomAlbums 的崩溃风险主要来自 Harmony/native hook、运行时改写 IL2CPP 数据结构、资源生命周期管理、坏谱面文件容错不足。下面计划只作为审批清单，未审批前不实施。

## 建议实施顺序

### 1. Native Hook 防崩保护层

目标：防止 C# 异常穿过 unmanaged 边界导致游戏直接崩溃。

范围：
- `Patches/AssetPatch.cs`
- `Patches/SavePatch.cs`

内容：
- 给 `LoadFromNamePatch` 和 `RecordBattleArgsPatch` 增加最外层 `try/catch`。
- 失败时记录日志，并安全回退到原始 trampoline 或空返回。
- 避免 `Environment.Exit` 作为常规失败路径。

风险：低。

优先级：最高。

### 2. 空引用和越界防护

目标：修掉场景切换、热重载、坏数据下容易触发的直接异常。

范围：
- `Patches/ChartDialogPatch.cs`
- `Patches/DifficultyGradeIconPatch.cs`
- `Patches/ReportCardPatch.cs`
- `Patches/SceneEggPatch.cs`
- `Patches/AnalyticsPatch.cs`
- `Patches/WebApiPatch.cs`
- `Patches/MikuSceneSupportPatch.cs`
- `Utilities/DataExtensions.cs`
- `Utilities/Il2CppExtensions.cs`

内容：
- `dialogEvents` 无当前语言或无 `English` 时跳过。
- `DialogEvents[Index++]` 增加越界判断。
- `musicInfo`、`uid`、`SaveData`、`SpecialSongManager.instance`、`datas["music_uid"]` 增加安全判断。
- `sceneFestivalName[6..]` 前检查长度。
- `CopyFromManaged` 对空数组做保护。

风险：低。

优先级：最高。

### 3. 资源生命周期清理

目标：减少长时间运行、频繁导入/删除、GIF 封面造成的内存压力和悬空引用。

范围：
- `Managers/CoverManager.cs`
- `Managers/AudioManager.cs`
- `Managers/LibraryPreviewManager.cs`
- `Patches/AssetPatch.cs`
- `Managers/HotReloadManager.cs`

内容：
- 删除谱面时销毁缓存里的 `Sprite`、`Texture2D`、`AudioClip`。
- 预览窗口关闭时确保 preview audio/cover 彻底释放。
- GIF 增加尺寸、帧数、总像素预算限制。
- GIF 队列启动标记改成线程安全写法，避免并发启动多个 worker。

风险：中低。

优先级：高。

### 4. 谱面扫描和坏文件隔离

目标：坏文件、坏文件夹、权限问题不应打断整个模组初始化。

范围：
- `Managers/AlbumManager.cs`
- `Managers/LibraryManager.cs`
- `Managers/PackManager.cs`
- `Data/Album.cs`

内容：
- 所有 `Directory.GetFiles/GetDirectories` 改为安全枚举。
- `File.GetAttributes` 放进 `try/catch`。
- 单个 `.mdm/.mdp/文件夹谱` 读取失败只跳过并记录路径。
- `PackManager` 增加 `Clear`，重载时清掉旧 pack 状态。
- 加载前规范化重复文件名，避免重复 album key 残留。

风险：中低。

优先级：高。

### 5. 热重载事务化重构

目标：减少热重载 add/delete 后 IL2CPP 数据库半更新、旧引用残留导致的延迟崩溃。

范围：
- `Managers/HotReloadManager.cs`
- `Patches/AssetPatch.cs`

内容：
- 把 add/delete 都改成同一个“从 `AlbumManager.LoadedAlbums` 重建运行时自制谱数据库”的方法。
- 文件事件先防抖合并，再一次性处理。
- 删除时同步更新 `DBConfigALBUM`、local dic、search tag、tag 列表、资源缓存。
- 热重载处理期间避免重复刷新 UI。

风险：中高。

优先级：中。建议前四项稳定后再做。

### 6. BMS 加载容错

目标：坏 BMS 或缺失资源不应直接崩游戏。

范围：
- `BmsLoader.cs`
- `Data/Sheet.cs`
- `Data/Bms.cs`

内容：
- `Sheet.GetStage` 外层兜底，失败时返回 null 并提示该难度加载失败。
- BMS 缺 `GENRE/BPM`、boss 动画、scene 资源时使用保守默认或跳过增强处理。
- boss/scene 字典访问改成 `TryGetValue`。
- 记录具体谱面、难度和失败步骤，便于定位坏谱。

风险：中。需要用多种谱面回归测试。

优先级：中。

## 推荐审批组合

第一批建议实施：1、2、3、4。

第二批再评估：5、6。
