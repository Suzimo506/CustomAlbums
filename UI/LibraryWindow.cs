using CustomAlbums.Data;
using CustomAlbums.Managers;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CustomAlbums.UI
{
    internal static class LibraryWindow
    {
        private const int SortingOrder = 32762;
        private const float PanelWidth = 1460f;
        private const float PanelHeight = 760f;
        private const float PreviewWidth = 360f;
        private const float RowHeight = 58f;

        private static readonly Color ColorPanel = new(0.12f, 0.045f, 0.24f, 0.88f);
        private static readonly Color ColorPanelBorder = new(0.95f, 0.28f, 0.86f, 0.34f);
        private static readonly Color ColorPanelDeep = new(0.055f, 0.018f, 0.13f, 0.72f);
        private static readonly Color ColorGlassLight = new(0.75f, 0.45f, 1f, 0.12f);
        private static readonly Color ColorAccent = new(0.92f, 0.12f, 0.72f, 0.92f);
        private static readonly Color ColorAccentSoft = new(0.48f, 0.18f, 0.72f, 0.42f);
        private static readonly Color ColorTextDim = new(0.78f, 0.72f, 0.92f, 0.78f);

        private static GameObject _root;
        private static RectTransform _categoryContent;
        private static RectTransform _listContent;
        private static RectTransform _previewPanel;
        private static InputField _searchInput;
        private static Image _coverFrameImage;
        private static Image _coverImage;
        private static Text _coverPlaceholder;
        private static Text _previewTitle;
        private static Text _previewMeta;
        private static Text _previewDetails;
        private static Text _previewStatus;
        private static string _selectedCategory = "All";
        private static LibraryAlbumEntry _selectedEntry;
        private static Font _font;
        private static Sprite _roundedSprite;

        public static bool IsOpen => _root != null;

        public static void Show()
        {
            if (_root != null) return;

            LibraryPreviewManager.MuteGameDemo();
            LibraryManager.RefreshIndex();
            _root = CreateRoot();
            var panel = CreatePanel(_root.transform);

            CreateHeader(panel);
            CreateSearch(panel);
            CreateCategories(panel);
            CreatePreview(panel);
            CreateList(panel);
            RebuildCategories();
            RebuildList();
            SelectEntry(null, false, false);
        }

        public static void Close()
        {
            if (_root == null) return;
            LibraryPreviewManager.Cleanup();
            UnityEngine.Object.Destroy(_root);
            _root = null;
            _categoryContent = null;
            _listContent = null;
            _previewPanel = null;
            _searchInput = null;
            _coverImage = null;
            _coverPlaceholder = null;
            _coverFrameImage = null;
            _previewTitle = null;
            _previewMeta = null;
            _previewDetails = null;
            _previewStatus = null;
            _selectedEntry = null;
        }

        private static GameObject CreateRoot()
        {
            var root = new GameObject("CustomAlbumsLibraryWindow");
            UnityEngine.Object.DontDestroyOnLoad(root);

            var rect = root.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = SortingOrder;

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            root.AddComponent<GraphicRaycaster>();

            var shade = new GameObject("Shade");
            shade.transform.SetParent(root.transform, false);
            var shadeRect = shade.AddComponent<RectTransform>();
            shadeRect.anchorMin = Vector2.zero;
            shadeRect.anchorMax = Vector2.one;
            shadeRect.offsetMin = Vector2.zero;
            shadeRect.offsetMax = Vector2.zero;

            var shadeImage = shade.AddComponent<Image>();
            shadeImage.color = new Color(0f, 0f, 0f, 0.86f);
            shadeImage.raycastTarget = true;

            return root;
        }

        private static RectTransform CreatePanel(Transform root)
        {
            var border = CreateImage(root, "PanelBorder", ColorPanelBorder, true);
            border.anchorMin = new Vector2(0.5f, 0.5f);
            border.anchorMax = new Vector2(0.5f, 0.5f);
            border.pivot = new Vector2(0.5f, 0.5f);
            border.sizeDelta = new Vector2(PanelWidth + 10f, PanelHeight + 10f);

            var panel = CreateImage(border, "Panel", ColorPanel, true);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(PanelWidth, PanelHeight);

            var topGlow = CreateImage(panel, "TopGlass", ColorGlassLight, false);
            topGlow.anchorMin = new Vector2(0f, 1f);
            topGlow.anchorMax = new Vector2(1f, 1f);
            topGlow.pivot = new Vector2(0.5f, 1f);
            topGlow.sizeDelta = new Vector2(-34f, 92f);
            topGlow.anchoredPosition = new Vector2(0f, -18f);

            return panel;
        }

        private static void CreateHeader(RectTransform panel)
        {
            var title = CreateText(panel, "Title", "导入自制谱", 42, TextAnchor.MiddleLeft);
            title.color = Color.white;
            title.fontStyle = FontStyle.Bold;
            SetFixedTop(title.rectTransform, 42f, 28f, 260f, 52f);

            var count = CreateText(panel, "Count", $"{LibraryManager.Entries.Count} 张索引谱面", 20, TextAnchor.MiddleLeft);
            count.color = ColorTextDim;
            SetFixedTop(count.rectTransform, 306f, 44f, 180f, 28f);

            var close = CreateButton(panel, "CloseButton", "关闭", ColorAccent, Close);
            close.anchorMin = new Vector2(1f, 1f);
            close.anchorMax = new Vector2(1f, 1f);
            close.pivot = new Vector2(1f, 1f);
            close.sizeDelta = new Vector2(132f, 46f);
            close.anchoredPosition = new Vector2(-34f, -41f);
        }

        private static void CreateSearch(RectTransform panel)
        {
            var root = CreateImage(panel, "SearchBox", new Color(0.07f, 0.025f, 0.16f, 0.64f), true);
            root.anchorMin = new Vector2(0f, 1f);
            root.anchorMax = new Vector2(0f, 1f);
            root.pivot = new Vector2(0f, 1f);
            root.sizeDelta = new Vector2(760f, 46f);
            root.anchoredPosition = new Vector2(500f, -41f);

            _searchInput = root.gameObject.AddComponent<InputField>();
            _searchInput.targetGraphic = root.GetComponent<Image>();
            _searchInput.onValueChanged.AddListener((UnityAction<string>)(_ =>
            {
                RebuildList();
                ClearSelectionIfHidden();
            }));

            var text = CreateText(root, "Text", string.Empty, 23, TextAnchor.MiddleLeft);
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            SetStretch(text.rectTransform, 26f, 0f, -26f, 0f);

            var placeholder = CreateText(root, "Placeholder", "搜索谱名、作者、标签...", 23, TextAnchor.MiddleLeft);
            placeholder.color = new Color(1f, 1f, 1f, 0.34f);
            placeholder.fontStyle = FontStyle.Italic;
            SetStretch(placeholder.rectTransform, 26f, 0f, -26f, 0f);

            _searchInput.textComponent = text;
            _searchInput.placeholder = placeholder;
        }

        private static void CreateCategories(RectTransform panel)
        {
            var viewport = CreateScrollViewport(panel, "Categories", 42f, 126f, PanelWidth - 84f, 50f, true);
            _categoryContent = CreateContent(viewport, false);
        }

        private static void CreatePreview(RectTransform panel)
        {
            _previewPanel = CreateImage(panel, "Preview", ColorPanelDeep, true);
            _previewPanel.anchorMin = new Vector2(0f, 1f);
            _previewPanel.anchorMax = new Vector2(0f, 1f);
            _previewPanel.pivot = new Vector2(0f, 1f);
            _previewPanel.sizeDelta = new Vector2(PreviewWidth, 500f);
            _previewPanel.anchoredPosition = new Vector2(42f, -196f);

            var coverFrame = CreateImage(_previewPanel, "CoverFrame", new Color(0.30f, 0.13f, 0.46f, 0.20f), false);
            _coverFrameImage = coverFrame.GetComponent<Image>();
            coverFrame.anchorMin = new Vector2(0.5f, 1f);
            coverFrame.anchorMax = new Vector2(0.5f, 1f);
            coverFrame.pivot = new Vector2(0.5f, 1f);
            coverFrame.sizeDelta = new Vector2(190f, 190f);
            coverFrame.anchoredPosition = new Vector2(0f, -24f);

            var coverObj = new GameObject("Cover");
            coverObj.transform.SetParent(coverFrame, false);
            var coverRect = coverObj.AddComponent<RectTransform>();
            coverRect.anchorMin = Vector2.zero;
            coverRect.anchorMax = Vector2.one;
            coverRect.offsetMin = new Vector2(10f, 10f);
            coverRect.offsetMax = new Vector2(-10f, -10f);
            _coverImage = coverObj.AddComponent<Image>();
            _coverImage.color = new Color(1f, 1f, 1f, 0f);
            _coverImage.preserveAspect = true;

            _coverPlaceholder = CreateText(coverFrame, "CoverPlaceholder", "NO COVER", 20, TextAnchor.MiddleCenter);
            _coverPlaceholder.color = new Color(1f, 1f, 1f, 0.28f);
            SetStretch(_coverPlaceholder.rectTransform, 0f, 0f, 0f, 0f);

            _previewTitle = CreateText(_previewPanel, "PreviewTitle", "选择一张谱面", 26, TextAnchor.UpperLeft);
            _previewTitle.color = Color.white;
            _previewTitle.fontStyle = FontStyle.Bold;
            SetFixedTop(_previewTitle.rectTransform, 28f, 234f, PreviewWidth - 56f, 50f);

            _previewMeta = CreateText(_previewPanel, "PreviewMeta", string.Empty, 19, TextAnchor.UpperLeft);
            _previewMeta.color = ColorTextDim;
            SetFixedTop(_previewMeta.rectTransform, 28f, 286f, PreviewWidth - 56f, 32f);

            _previewDetails = CreateText(_previewPanel, "PreviewDetails", string.Empty, 18, TextAnchor.UpperLeft);
            _previewDetails.color = new Color(1f, 1f, 1f, 0.72f);
            _previewDetails.lineSpacing = 1.18f;
            SetFixedTop(_previewDetails.rectTransform, 28f, 326f, PreviewWidth - 56f, 106f);

            _previewStatus = CreateText(_previewPanel, "PreviewStatus", "demo 会在选中后播放", 16, TextAnchor.LowerLeft);
            _previewStatus.color = new Color(1f, 1f, 1f, 0.46f);
            _previewStatus.rectTransform.anchorMin = new Vector2(0f, 0f);
            _previewStatus.rectTransform.anchorMax = new Vector2(0f, 0f);
            _previewStatus.rectTransform.pivot = new Vector2(0f, 0f);
            _previewStatus.rectTransform.sizeDelta = new Vector2(PreviewWidth - 56f, 24f);
            _previewStatus.rectTransform.anchoredPosition = new Vector2(28f, 12f);
        }

        private static void CreateList(RectTransform panel)
        {
            var listLeft = 42f + PreviewWidth + 24f;
            var listWidth = PanelWidth - listLeft - 42f;
            var viewport = CreateScrollViewport(panel, "AlbumList", listLeft, 196f, listWidth, 500f, false);
            _listContent = CreateContent(viewport, true);
        }

        private static RectTransform CreateScrollViewport(RectTransform panel, string name, float x, float top, float width, float height, bool horizontal)
        {
            var viewport = CreateImage(panel, name, ColorPanelDeep, true);
            viewport.anchorMin = new Vector2(0f, 1f);
            viewport.anchorMax = new Vector2(0f, 1f);
            viewport.pivot = new Vector2(0f, 1f);
            viewport.sizeDelta = new Vector2(width, height);
            viewport.anchoredPosition = new Vector2(x, -top);

            var mask = viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = true;

            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.horizontal = horizontal;
            scroll.vertical = !horizontal;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 34f;

            return viewport;
        }

        private static RectTransform CreateContent(RectTransform viewport, bool vertical)
        {
            var content = new GameObject("Content");
            content.transform.SetParent(viewport, false);
            var rect = content.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = vertical ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.offsetMin = new Vector2(10f, 0f);
            rect.offsetMax = vertical ? new Vector2(-10f, 0f) : new Vector2(0f, 0f);
            viewport.GetComponent<ScrollRect>().content = rect;
            return rect;
        }

        private static void RebuildCategories()
        {
            Clear(_categoryContent);

            var categories = new List<string> { "All", "Active" };
            categories.AddRange(LibraryManager.GetCategories());

            var x = 8f;
            for (var i = 0; i < categories.Count; i++)
            {
                var category = categories[i];
                var selected = category == _selectedCategory;
                var label = GetCategoryLabel(category);
                var width = Mathf.Clamp(92f + label.Length * 18f, 116f, 260f);
                var row = CreateButton(_categoryContent, "Category_" + i, label, selected ? ColorAccent : ColorAccentSoft, () =>
                {
                    _selectedCategory = category;
                    RebuildCategories();
                    RebuildList();
                    ClearSelectionIfHidden();
                });
                row.anchorMin = new Vector2(0f, 1f);
                row.anchorMax = new Vector2(0f, 1f);
                row.pivot = new Vector2(0f, 1f);
                row.sizeDelta = new Vector2(width, 36f);
                row.anchoredPosition = new Vector2(x, -8f);
                x += width + 10f;
            }

            _categoryContent.sizeDelta = new Vector2(Mathf.Max(x + 8f, PanelWidth - 84f), 0f);
        }

        private static void RebuildList()
        {
            Clear(_listContent);

            var query = _searchInput?.text ?? string.Empty;
            var albums = GetVisibleAlbums(query).Take(300).ToList();

            for (var i = 0; i < albums.Count; i++)
            {
                CreateAlbumRow(albums[i], i);
            }

            if (albums.Count == 0)
            {
                var empty = CreateText(_listContent, "Empty", "没有找到谱面", 28, TextAnchor.MiddleCenter);
                empty.color = new Color(1f, 1f, 1f, 0.68f);
                empty.rectTransform.anchorMin = new Vector2(0f, 1f);
                empty.rectTransform.anchorMax = new Vector2(1f, 1f);
                empty.rectTransform.sizeDelta = new Vector2(0f, 120f);
                empty.rectTransform.anchoredPosition = new Vector2(0f, -40f);
            }

            _listContent.sizeDelta = new Vector2(0f, Mathf.Max(0f, 20f + albums.Count * RowHeight));
        }

        private static void ClearSelectionIfHidden()
        {
            if (_selectedEntry != null && GetVisibleAlbums(_searchInput?.text ?? string.Empty).Any(album => album == _selectedEntry))
                return;

            SelectEntry(null, false, false);
        }

        private static IEnumerable<LibraryAlbumEntry> GetVisibleAlbums(string query)
        {
            return _selectedCategory == "Active"
                ? LibraryManager.Search(query).Where(album => album.IsActive)
                : LibraryManager.Search(query, _selectedCategory);
        }

        private static void CreateAlbumRow(LibraryAlbumEntry entry, int index)
        {
            var row = CreateImage(_listContent, GetRowName(entry), GetRowColor(entry == _selectedEntry, index), true);
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.sizeDelta = new Vector2(0f, 50f);
            row.anchoredPosition = new Vector2(0f, -10f - index * RowHeight);

            var selectButton = row.gameObject.AddComponent<Button>();
            selectButton.targetGraphic = row.GetComponent<Image>();
            selectButton.transition = Selectable.Transition.ColorTint;
            selectButton.colors = CreateButtonColors(row.GetComponent<Image>().color);
            selectButton.navigation = new Navigation { mode = Navigation.Mode.None };
            selectButton.onClick.AddListener((UnityAction)(() =>
            {
                ClearSelectedObject();
                SelectEntry(entry, true, false);
            }));

            var accent = CreateImage(row, "Accent", entry.IsActive ? ColorAccent : new Color(1f, 1f, 1f, 0.16f), false);
            accent.anchorMin = Vector2.zero;
            accent.anchorMax = new Vector2(0f, 1f);
            accent.pivot = new Vector2(0f, 0.5f);
            accent.sizeDelta = new Vector2(6f, 0f);

            var title = CreateText(row, "Title", Escape(entry.Info.Name), 22, TextAnchor.MiddleLeft);
            title.color = Color.white;
            title.fontStyle = FontStyle.Bold;
            SetStretch(title.rectTransform, 20f, -2f, -156f, -2f);
            title.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);

            var meta = CreateText(row, "Meta", $"{Escape(entry.Info.Author)}  /  {Escape(GetMainCharter(entry.Info))}  /  {Escape(GetCategoryLabel(entry.Category))}", 17, TextAnchor.MiddleLeft);
            meta.color = ColorTextDim;
            SetStretch(meta.rectTransform, 20f, 1f, -156f, 1f);
            meta.rectTransform.anchorMin = new Vector2(0f, 0f);
            meta.rectTransform.anchorMax = new Vector2(1f, 0.5f);

            var action = CreateButton(row, "Action", entry.IsActive ? "移除" : "导入",
                entry.IsActive ? new Color(0.46f, 0.16f, 0.42f, 0.92f) : ColorAccent, () =>
                {
                    SelectEntry(entry, false, false);
                    if (entry.IsActive)
                        LibraryManager.Deactivate(entry);
                    else
                        LibraryManager.Activate(entry);

                    RefreshPreview();
                    RebuildList();
                });
            action.anchorMin = new Vector2(1f, 0.5f);
            action.anchorMax = new Vector2(1f, 0.5f);
            action.pivot = new Vector2(1f, 0.5f);
            action.sizeDelta = new Vector2(108f, 34f);
            action.anchoredPosition = new Vector2(-16f, 0f);
        }

        private static void SelectEntry(LibraryAlbumEntry entry, bool loadResources, bool rebuildRows = true)
        {
            var sameEntry = _selectedEntry == entry;
            _selectedEntry = entry;
            RefreshPreview();
            if (rebuildRows)
                RebuildList();
            else
                RefreshListSelectionVisuals();

            if (entry == null) return;
            if (!loadResources)
            {
                if (!sameEntry) ClearPreviewMedia(true);
                return;
            }
            if (sameEntry) return;

            _coverImage.sprite = LibraryPreviewManager.LoadCover(entry);
            _coverImage.color = _coverImage.sprite == null ? new Color(1f, 1f, 1f, 0f) : Color.white;
            if (_coverPlaceholder != null) _coverPlaceholder.gameObject.SetActive(_coverImage.sprite == null);
            LibraryPreviewManager.PlayDemo(entry);
        }

        private static void RefreshListSelectionVisuals()
        {
            if (_listContent == null) return;

            var rowIndex = 0;
            var selectedName = GetRowName(_selectedEntry);
            for (var i = 0; i < _listContent.childCount; i++)
            {
                var child = _listContent.GetChild(i);
                if (child == null || !child.name.StartsWith("Album_", StringComparison.Ordinal)) continue;

                var image = child.GetComponent<Image>();
                if (image != null) image.color = GetRowColor(child.name == selectedName, rowIndex);

                var button = child.GetComponent<Button>();
                if (button != null) button.colors = CreateButtonColors(image != null ? image.color : GetRowColor(false, rowIndex));

                rowIndex++;
            }
        }

        private static string GetRowName(LibraryAlbumEntry entry)
        {
            return entry == null ? string.Empty : "Album_" + entry.RelativePath;
        }

        private static Color GetRowColor(bool selected, int index)
        {
            if (selected) return new Color(0.36f, 0.12f, 0.48f, 0.58f);
            return index % 2 == 0
                ? new Color(0.17f, 0.065f, 0.31f, 0.46f)
                : new Color(0.11f, 0.035f, 0.22f, 0.46f);
        }

        private static void RefreshPreview()
        {
            if (_previewTitle == null) return;

            if (_selectedEntry == null)
            {
                ClearPreviewMedia(true);
                _previewTitle.text = "未选择谱面";
                _previewMeta.text = string.Empty;
                _previewDetails.text = string.Empty;
                _previewStatus.text = "从右侧列表选择一张谱面";
                if (_coverPlaceholder != null) _coverPlaceholder.gameObject.SetActive(false);
                if (_coverFrameImage != null) _coverFrameImage.color = new Color(0.30f, 0.13f, 0.46f, 0.20f);
                return;
            }

            var info = _selectedEntry.Info;
            _previewTitle.text = Escape(info.Name);
            _previewMeta.text = Escape(info.Author);
            _previewDetails.text =
                $"<color=#ffffff>BPM</color>    {Escape(info.Bpm)}\n" +
                $"<color=#ffffff>难度</color>   {FormatDifficulties(info)}\n" +
                $"<color=#ffffff>谱师</color>   <color=#ff63d6>{Escape(FormatCharters(info))}</color>\n" +
                $"<color=#ffffff>分类</color>   {Escape(GetCategoryLabel(_selectedEntry.Category))}";
            _previewStatus.text = _selectedEntry.IsActive
                ? "<color=#47f58d>已导入</color>"
                : "<color=#ff5b7d>未导入</color>";
            if (_coverFrameImage != null) _coverFrameImage.color = new Color(0.30f, 0.13f, 0.46f, 0.20f);
        }

        private static void ClearPreviewMedia(bool stopDemo)
        {
            if (stopDemo) LibraryPreviewManager.StopDemo();
            if (_coverImage != null)
            {
                _coverImage.sprite = null;
                _coverImage.color = new Color(1f, 1f, 1f, 0f);
            }
            if (_coverPlaceholder != null) _coverPlaceholder.gameObject.SetActive(true);
        }

        private static string FormatDifficulties(AlbumInfo info)
        {
            var values = new[] { info.Difficulty1, info.Difficulty2, info.Difficulty3, info.Difficulty4, info.Difficulty5 }
                .Where(value => !string.IsNullOrEmpty(value) && value != "0");
            return string.Join(" / ", values);
        }

        private static string GetMainCharter(AlbumInfo info)
        {
            return FirstNonEmpty(
                info.LevelDesigner,
                info.LevelDesigner1,
                info.LevelDesigner2,
                info.LevelDesigner3,
                info.LevelDesigner4,
                info.LevelDesigner5);
        }

        private static string FormatCharters(AlbumInfo info)
        {
            var charters = new[]
            {
                info.LevelDesigner,
                info.LevelDesigner1,
                info.LevelDesigner2,
                info.LevelDesigner3,
                info.LevelDesigner4,
                info.LevelDesigner5
            };

            var unique = charters
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            return string.Join(" / ", unique);
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "-";
        }

        private static string GetCategoryLabel(string category)
        {
            return category switch
            {
                "All" => "全部",
                "Active" => "已导入",
                "Unsorted" => "未分类",
                _ => category
            };
        }

        private static RectTransform CreateButton(Transform parent, string name, string label, Color color, Action action)
        {
            var rect = CreateImage(parent, name, color, true);
            var image = rect.GetComponent<Image>();
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.colors = CreateButtonColors(color);
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener((UnityAction)(() =>
            {
                ClearSelectedObject();
                action?.Invoke();
            }));

            var text = CreateText(rect, "Label", label, 20, TextAnchor.MiddleCenter);
            text.color = Color.white;
            text.fontStyle = FontStyle.Bold;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            SetStretch(text.rectTransform, 0f, 0f, 0f, 0f);
            return rect;
        }

        private static ColorBlock CreateButtonColors(Color color)
        {
            return new ColorBlock
            {
                normalColor = color,
                highlightedColor = Color.Lerp(color, Color.white, 0.04f),
                pressedColor = Color.Lerp(color, Color.black, 0.18f),
                selectedColor = color,
                disabledColor = new Color(0.35f, 0.30f, 0.40f, 0.7f),
                colorMultiplier = 1f,
                fadeDuration = 0.03f
            };
        }

        private static void ClearSelectedObject()
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        private static Text CreateText(Transform parent, string name, string value, int fontSize, TextAnchor anchor)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            var text = obj.AddComponent<Text>();
            text.font = GetFont();
            text.text = value;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static RectTransform CreateImage(Transform parent, string name, Color color, bool raycastTarget)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            var rect = obj.AddComponent<RectTransform>();
            var image = obj.AddComponent<Image>();
            image.sprite = GetRoundedSprite();
            image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            image.color = color;
            image.raycastTarget = raycastTarget;
            return rect;
        }

        private static void SetStretch(RectTransform rect, float left, float top, float right, float bottom)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(right, top);
        }

        private static void SetFixedTop(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, -y);
        }

        private static void Clear(Transform parent)
        {
            if (parent == null) return;
            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(parent.GetChild(i).gameObject);
            }
        }

        private static Font GetFont()
        {
            if (_font != null) return _font;

            foreach (var text in Resources.FindObjectsOfTypeAll<Text>())
            {
                if (text == null || text.font == null) continue;
                _font = text.font;
                return _font;
            }

            _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return _font;
        }

        private static Sprite GetRoundedSprite()
        {
            if (_roundedSprite != null) return _roundedSprite;

            try
            {
                _roundedSprite = Addressables.LoadAssetAsync<Sprite>("SprRoundedsquare").WaitForCompletion();
            }
            catch
            {
                _roundedSprite = null;
            }

            return _roundedSprite;
        }

        private static string Escape(string value)
        {
            return string.IsNullOrEmpty(value)
                ? "-"
                : value.Replace("<", "＜").Replace(">", "＞");
        }
    }
}
