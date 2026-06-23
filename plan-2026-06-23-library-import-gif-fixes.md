# 谱库导入、缺谱自动导入与 GIF 封面修复计划 - 2026-06-23

## 用户反馈

1. 谱库弹窗右侧列表高度被缩小，原因是“导入分类”按钮占用了列表区域。
2. Ensemble 缺谱播报点击后，没有从 CustomAlbums 候选区自动导入，即使候选区存在同谱面。
3. GIF 封面在游戏里可能显示成白框。
4. 从候选区导入的 GIF 动图可能只显示静态封面。

## 初步结论

- 右侧列表被缩小是上次改动直接把 `AlbumList` 高度从 `500f` 改成 `446f`，这是错误取舍。按钮应浮在弹窗右下角或底部空白区，不应挤占列表尺寸。
- 缺谱自动导入当前只接受 `Info.Name`、`Info.NameRomanized`、文件名去扩展名的完全相等；实际缺谱播报文本可能带 TV Size、全角符号、富文本清理残留、分类/难度格式差异，导致候选区存在但匹配失败。
- 缺谱 `ExtraData` 的第一段是玩家名，不能参与候选区搜索；谱名中也可能包含 `#`，不能把每一段拆开当独立关键词，否则容易把唯一候选污染成多候选。
- 旧 Ensemble 服务端和 MDEN 服务端当前缺谱播报都只发送 `玩家名#谱面显示名`，没有发送 `ChartKey/MD5`。本轮客户端只能先把谱名匹配做稳；若以后服务端补带 key，可按 hash 精确导入。
- GIF 白框/静态化需要分别看两个路径：
  - 谱库预览 `LibraryPreviewManager.LoadCover` 只读 `cover.png`，GIF 候选没有 PNG 时预览会空白。
  - 游戏内热重载新增谱面时只预加载 cover sprite，没有对 `album.HasGif` 调用 GIF 解码队列；因此新导入的 GIF 可能无法生成 `AnimatedCover`。
  - GIF 解码使用 `RootFrame.FrameDelay * 10` 作为 FPS 传入，语义很可能错误；`AnimatedCoverPatch` 期待的是 frames-per-second。

## 修复顺序

1. 优先修复缺谱自动导入：
   - 增加更稳的规范化匹配：大小写、空白、富文本、首尾分类、末尾难度、常见标点归一。
   - 仍只在唯一候选时自动导入，避免导错谱。
   - 允许唯一“强规范化匹配”导入；多候选时降级复制/搜索。
   - 增加日志，记录候选数量、匹配状态、前几个候选名，便于后续排查。
2. 恢复谱库右侧列表高度：
   - `AlbumList` 恢复 `500f`。
   - 分类按钮保持右下角粉色，但不占用列表空间；位置放在列表右下方的面板空白区。
3. 修复 GIF：
   - 候选区预览如果没有 `cover.png` 但有 `cover.gif`，加载 GIF 第一帧作为预览，避免白框。
   - 热重载新增 GIF 谱面时调用 `CoverManager.EnqueueGifToLoad(album)`。
   - 修正 GIF 帧率计算，避免把 frame delay 当 FPS。
   - `LoadAnimatedCover` 跳过无效 raw frame，避免空白/异常帧进入缓存。
4. 验证：
   - `dotnet build CustomAlbums.csproj -p:WORKER=GitHub`
   - `dotnet build MuseDashEnsemble.csproj -p:WORKER=GitHub`
   - `git diff --check`

## 保持的边界

- 不改喵斯兔本体。
- 不让模糊搜索结果自动导入多个可能项。
- 不再牺牲谱库列表空间给右下角按钮。

## 执行结果

- Ensemble 缺谱点击入口已传入完整 `ExtraData`，自动导入只使用完整谱名和未来兼容的 chart key，不再把玩家名或被 `#` 拆碎的谱名当搜索 key。
- 自动导入增加规范化匹配，仍只在唯一候选时导入；多候选、未命中、异常时继续复制并尝试打开游戏内搜索，同时写入诊断日志。
- CustomAlbums 右侧列表已恢复 `500f` 高度，`导入分类/移除分类` 按钮下移到右下角底部空白区。
- GIF 预览改为无 PNG 时用 ImageSharp 读取 `cover.gif` 第一帧；热重载新增 GIF 谱面会进入 GIF 解码队列；GIF FPS 和无效帧处理已修正。
- 验证结果：
  - `dotnet build MuseDashEnsemble.csproj -p:WORKER=GitHub` 通过，并已复制到游戏 `Mods`。
  - `dotnet build CustomAlbums.csproj -p:WORKER=GitHub` 通过。
  - 普通 `dotnet build CustomAlbums.csproj` 覆盖游戏 `Mods` 失败，因为正在运行的 `MuseDash.exe` 锁住了 `CustomAlbums.dll`。
