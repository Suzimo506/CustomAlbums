using CustomAlbums.Data;
using CustomAlbums.Managers;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using PeroInputManager = Il2CppAssets.Scripts.PeroTools.Managers.InputManager;

namespace CustomAlbums.UI
{
    internal enum DifficultyWheelKind
    {
        None,
        Min,
        Max
    }

    internal static class LibraryWindow
    {
        private const int SortingOrder = 32762;
        private const float PanelWidth = 1460f;
        private const float PanelHeight = 760f;
        private const float PreviewWidth = 360f;
        private const float RowHeight = 58f;
        private const float DifficultyStep = 1f;
        private const float DefaultMinDifficulty = 6f;
        private const float DefaultMaxDifficulty = 11f;
        private const int RowAuthorMaxLength = 22;
        private const int RowCharterMaxLength = 18;
        private const int RowCategoryMaxLength = 12;

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
        private static Text _volumeLabel;
        private static Slider _volumeSlider;
        private static RectTransform _minDifficultyWheelRect;
        private static RectTransform _maxDifficultyWheelRect;
        private static Text[] _minDifficultyWheelTexts;
        private static Text[] _maxDifficultyWheelTexts;
        private static string _selectedCategory = "All";
        private static LibraryAlbumEntry _selectedEntry;
        private static float _pendingMinDifficulty = DefaultMinDifficulty;
        private static float _pendingMaxDifficulty = DefaultMaxDifficulty;
        private static float _activeMinDifficulty = DefaultMinDifficulty;
        private static float _activeMaxDifficulty = DefaultMaxDifficulty;
        private static DifficultyWheelKind _hoveredDifficultyWheel = DifficultyWheelKind.None;
        private static DifficultyWheelKind _draggingDifficultyWheel = DifficultyWheelKind.None;
        private static float _dragStartDifficultyValue;
        private static Vector2 _dragStartPointerPosition;
        private static bool _nativeInputBlocked;
        private static Font _font;
        private static Sprite _roundedSprite;

        public static bool IsOpen => _root != null;
        public static bool IsSearchInputFocused => _searchInput != null && _searchInput.isFocused;

        public static void Update()
        {
            UpdateNativeInputBlock();
            UpdateDifficultyWheelInput();
        }

        public static void Show()
        {
            if (_root != null) return;

            LibraryPreviewManager.MuteGameDemo();
            LibraryManager.RefreshIndex();
            ResetDifficultyFilters();
            _root = CreateRoot();
            var panel = CreatePanel(_root.transform);

            CreateHeader(panel);
            CreateSearch(panel);
            CreateCategories(panel);
            CreatePreview(panel);
            CreateList(panel);
            CreateVolumeControl(panel);
            RebuildCategories();
            RebuildList();
            SelectEntry(null, false, false);
        }

        private static void ResetDifficultyFilters()
        {
            _pendingMinDifficulty = DefaultMinDifficulty;
            _pendingMaxDifficulty = DefaultMaxDifficulty;
            _activeMinDifficulty = DefaultMinDifficulty;
            _activeMaxDifficulty = DefaultMaxDifficulty;
        }

        public static void Close()
        {
            if (_root == null) return;
            RestoreNativeInput();
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
            _volumeLabel = null;
            _volumeSlider = null;
            _minDifficultyWheelRect = null;
            _maxDifficultyWheelRect = null;
            _minDifficultyWheelTexts = null;
            _maxDifficultyWheelTexts = null;
            _selectedEntry = null;
            _hoveredDifficultyWheel = DifficultyWheelKind.None;
            _draggingDifficultyWheel = DifficultyWheelKind.None;
        }

        private static void UpdateNativeInputBlock()
        {
            var shouldBlock = IsOpen;
            if (_nativeInputBlocked == shouldBlock) return;

            _nativeInputBlocked = shouldBlock;
            SetNativeInputBlocked(shouldBlock);
        }

        private static void RestoreNativeInput()
        {
            if (!_nativeInputBlocked) return;

            _nativeInputBlocked = false;
            SetNativeInputBlocked(false);
        }

        private static void SetNativeInputBlocked(bool blocked)
        {
            try
            {
                if (PeroInputManager.instance != null)
                {
                    PeroInputManager.instance.isStopKeyAction = blocked;
                }
            }
            catch
            {
                // Native input manager may be unavailable during scene transitions.
            }
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
            var count = CreateText(panel, "Count", $"{LibraryManager.Entries.Count} 张谱面", 20, TextAnchor.MiddleLeft);
            count.color = ColorTextDim;
            count.fontStyle = FontStyle.Bold;
            SetFixedTop(count.rectTransform, 42f, 48f, 180f, 28f);

            CreateDifficultyFilters(panel);

            var close = CreateButton(panel, "CloseButton", "关闭", ColorAccent, () =>
            {
                LibraryUiSoundManager.Play(LibraryUiSound.Cancel);
                Close();
            });
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
            root.sizeDelta = new Vector2(470f, 46f);
            root.anchoredPosition = new Vector2(220f, -41f);

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

        private static void CreateDifficultyFilters(RectTransform panel)
        {
            _minDifficultyWheelRect = CreateDifficultyWheel(panel, "MinDifficultyWheel", "最低", () => _pendingMinDifficulty, new Vector2(720f, -24f), DifficultyWheelKind.Min);
            _minDifficultyWheelTexts = GetWheelTexts(_minDifficultyWheelRect);

            _maxDifficultyWheelRect = CreateDifficultyWheel(panel, "MaxDifficultyWheel", "最高", () => _pendingMaxDifficulty, new Vector2(720f, -70f), DifficultyWheelKind.Max);
            _maxDifficultyWheelTexts = GetWheelTexts(_maxDifficultyWheelRect);

            var apply = CreateButton(panel, "DifficultyApply", "筛选", ColorAccent, () =>
            {
                LibraryUiSoundManager.Play(LibraryUiSound.Yes);

                var minDifficulty = _pendingMinDifficulty;
                var maxDifficulty = _pendingMaxDifficulty;
                if (minDifficulty > maxDifficulty)
                {
                    (minDifficulty, maxDifficulty) = (maxDifficulty, minDifficulty);
                    _pendingMinDifficulty = minDifficulty;
                    _pendingMaxDifficulty = maxDifficulty;
                    RefreshDifficultyWheels();
                }

                _activeMinDifficulty = minDifficulty;
                _activeMaxDifficulty = maxDifficulty;
                RebuildList();
                ClearSelectionIfHidden();
            });
            apply.anchorMin = new Vector2(0f, 1f);
            apply.anchorMax = new Vector2(0f, 1f);
            apply.pivot = new Vector2(0f, 1f);
            apply.sizeDelta = new Vector2(86f, 38f);
            apply.anchoredPosition = new Vector2(1168f, -45f);

            RefreshDifficultyWheels();
        }

        private static RectTransform CreateDifficultyWheel(RectTransform panel, string name, string label, Func<float> getValue, Vector2 position, DifficultyWheelKind kind)
        {
            var root = CreateImage(panel, name, new Color(0.07f, 0.025f, 0.16f, 0.50f), true);
            root.anchorMin = new Vector2(0f, 1f);
            root.anchorMax = new Vector2(0f, 1f);
            root.pivot = new Vector2(0f, 1f);
            root.sizeDelta = new Vector2(420f, 38f);
            root.anchoredPosition = position;

            var labelText = CreateText(root, "Label", label, 16, TextAnchor.MiddleLeft);
            labelText.color = ColorTextDim;
            labelText.fontStyle = FontStyle.Bold;
            SetStretch(labelText.rectTransform, 16f, 0f, -342f, 0f);

            var valuesRoot = new GameObject("Values");
            valuesRoot.transform.SetParent(root, false);
            var valuesRect = valuesRoot.AddComponent<RectTransform>();
            valuesRect.anchorMin = new Vector2(0f, 0f);
            valuesRect.anchorMax = new Vector2(1f, 1f);
            valuesRect.offsetMin = new Vector2(82f, 0f);
            valuesRect.offsetMax = new Vector2(-12f, 0f);

            for (var i = 0; i < 5; i++)
            {
                var valueText = CreateText(valuesRect, "Value_" + i, FormatDifficultyValue(getValue()), 18, TextAnchor.MiddleCenter);
                valueText.fontStyle = FontStyle.Bold;
                valueText.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                valueText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                valueText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                valueText.rectTransform.sizeDelta = new Vector2(58f, 34f);
                valueText.rectTransform.anchoredPosition = new Vector2((i - 2) * 58f, 0f);
            }

            return root;
        }

        private static void UpdateDifficultyWheelInput()
        {
            if (!IsOpen) return;

            var pointerPosition = (Vector2)Input.mousePosition;
            var wheelUnderPointer = GetDifficultyWheelAt(pointerPosition);
            if (_draggingDifficultyWheel == DifficultyWheelKind.None)
                _hoveredDifficultyWheel = wheelUnderPointer;

            var scroll = Input.mouseScrollDelta.y;
            if (_hoveredDifficultyWheel != DifficultyWheelKind.None && Mathf.Abs(scroll) > 0.01f)
            {
                var steps = Mathf.RoundToInt(scroll);
                if (steps == 0) steps = scroll > 0f ? 1 : -1;
                SetPendingDifficulty(_hoveredDifficultyWheel, GetPendingDifficulty(_hoveredDifficultyWheel) + steps * DifficultyStep);
            }

            if (Input.GetMouseButtonDown(0) && wheelUnderPointer != DifficultyWheelKind.None)
            {
                ClearSelectedObject();
                SetPendingDifficulty(wheelUnderPointer, GetPendingDifficulty(wheelUnderPointer) + GetVisibleDifficultyOffset(wheelUnderPointer, pointerPosition) * DifficultyStep);
                _draggingDifficultyWheel = wheelUnderPointer;
                _dragStartDifficultyValue = GetPendingDifficulty(wheelUnderPointer);
                _dragStartPointerPosition = pointerPosition;
                return;
            }

            if (_draggingDifficultyWheel == DifficultyWheelKind.None) return;
            if (!Input.GetMouseButton(0))
            {
                _draggingDifficultyWheel = DifficultyWheelKind.None;
                return;
            }

            var delta = pointerPosition - _dragStartPointerPosition;
            var dominantDelta = Mathf.Abs(delta.x) > Mathf.Abs(delta.y) ? delta.x : delta.y * -1f;
            var dragSteps = Mathf.RoundToInt(dominantDelta / 26f);
            if (dragSteps == 0) return;

            SetPendingDifficulty(_draggingDifficultyWheel, _dragStartDifficultyValue + dragSteps * DifficultyStep);
        }

        private static DifficultyWheelKind GetDifficultyWheelAt(Vector2 screenPosition)
        {
            if (_minDifficultyWheelRect != null && RectTransformUtility.RectangleContainsScreenPoint(_minDifficultyWheelRect, screenPosition, null))
                return DifficultyWheelKind.Min;
            if (_maxDifficultyWheelRect != null && RectTransformUtility.RectangleContainsScreenPoint(_maxDifficultyWheelRect, screenPosition, null))
                return DifficultyWheelKind.Max;
            return DifficultyWheelKind.None;
        }

        private static float GetVisibleDifficultyOffset(DifficultyWheelKind kind, Vector2 screenPosition)
        {
            var rect = kind == DifficultyWheelKind.Min ? _minDifficultyWheelRect : _maxDifficultyWheelRect;
            if (rect == null) return 0f;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screenPosition, null, out var localPoint))
                return 0f;

            const float valuesLeft = 82f;
            const float valuesRight = 12f;
            const float valueSpacing = 58f;
            if (localPoint.x < valuesLeft) return 0f;

            var centerX = valuesLeft + (rect.rect.width - valuesLeft - valuesRight) * 0.5f;
            return Mathf.Clamp(Mathf.Round((localPoint.x - centerX) / valueSpacing), -2f, 2f);
        }

        private static float GetPendingDifficulty(DifficultyWheelKind kind)
        {
            return kind == DifficultyWheelKind.Min ? _pendingMinDifficulty : _pendingMaxDifficulty;
        }

        private static void SetPendingDifficulty(DifficultyWheelKind kind, float value)
        {
            value = ClampDifficulty(value);
            if (kind == DifficultyWheelKind.Min)
                _pendingMinDifficulty = value;
            else if (kind == DifficultyWheelKind.Max)
                _pendingMaxDifficulty = value;

            RefreshDifficultyWheels();
        }

        private static Text[] GetWheelTexts(RectTransform wheel)
        {
            var values = wheel.Find("Values");
            if (values == null) return Array.Empty<Text>();

            var texts = new Text[5];
            for (var i = 0; i < texts.Length; i++)
            {
                var item = values.Find("Value_" + i);
                texts[i] = item == null ? null : item.GetComponent<Text>();
            }

            return texts;
        }

        private static void RefreshDifficultyWheels()
        {
            RefreshDifficultyWheel(_minDifficultyWheelTexts, _pendingMinDifficulty);
            RefreshDifficultyWheel(_maxDifficultyWheelTexts, _pendingMaxDifficulty);
        }

        private static void RefreshDifficultyWheel(Text[] texts, float centerValue)
        {
            if (texts == null) return;

            for (var i = 0; i < texts.Length; i++)
            {
                var text = texts[i];
                if (text == null) continue;

                var offset = i - 2;
                var value = centerValue + offset * DifficultyStep;
                var inRange = value >= 0f && value <= 15f;
                text.text = inRange ? FormatDifficultyValue(value) : string.Empty;
                text.fontSize = offset == 0 ? 21 : 17;
                text.color = offset switch
                {
                    0 => Color.white,
                    -1 or 1 => new Color(1f, 1f, 1f, 0.58f),
                    _ => new Color(1f, 1f, 1f, 0.24f)
                };
            }
        }

        private static float ClampDifficulty(float value)
        {
            return Mathf.Clamp(Mathf.Round(value / DifficultyStep) * DifficultyStep, 0f, 15f);
        }

        private static string FormatDifficultyValue(float value)
        {
            return Mathf.RoundToInt(value).ToString();
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
            coverRect.offsetMin = Vector2.zero;
            coverRect.offsetMax = Vector2.zero;
            _coverImage = coverObj.AddComponent<Image>();
            _coverImage.color = new Color(1f, 1f, 1f, 0f);
            _coverImage.preserveAspect = true;

            _coverPlaceholder = CreateText(coverFrame, "CoverPlaceholder", "NO COVER", 20, TextAnchor.MiddleCenter);
            _coverPlaceholder.color = new Color(1f, 1f, 1f, 0.28f);
            SetStretch(_coverPlaceholder.rectTransform, 0f, 0f, 0f, 0f);

            _previewTitle = CreateText(_previewPanel, "PreviewTitle", "选择一张谱面", 26, TextAnchor.UpperLeft);
            _previewTitle.color = Color.white;
            _previewTitle.fontStyle = FontStyle.Bold;
            _previewTitle.verticalOverflow = VerticalWrapMode.Truncate;
            SetFixedTop(_previewTitle.rectTransform, 28f, 230f, PreviewWidth - 56f, 72f);

            _previewMeta = CreateText(_previewPanel, "PreviewMeta", string.Empty, 19, TextAnchor.UpperLeft);
            _previewMeta.color = ColorTextDim;
            _previewMeta.verticalOverflow = VerticalWrapMode.Truncate;
            SetFixedTop(_previewMeta.rectTransform, 28f, 312f, PreviewWidth - 56f, 32f);

            _previewDetails = CreateText(_previewPanel, "PreviewDetails", string.Empty, 18, TextAnchor.UpperLeft);
            _previewDetails.color = new Color(1f, 1f, 1f, 0.72f);
            _previewDetails.lineSpacing = 1.18f;
            _previewDetails.verticalOverflow = VerticalWrapMode.Truncate;
            SetFixedTop(_previewDetails.rectTransform, 28f, 354f, PreviewWidth - 56f, 112f);

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

        private static void CreateVolumeControl(RectTransform panel)
        {
            var root = CreateImage(panel, "PreviewVolume", ColorAccentSoft, true);
            root.anchorMin = new Vector2(0f, 0f);
            root.anchorMax = new Vector2(0f, 0f);
            root.pivot = new Vector2(0f, 0f);
            root.sizeDelta = new Vector2(270f, 30f);
            root.anchoredPosition = new Vector2(42f, 22f);

            _volumeLabel = CreateText(root, "Label", GetVolumeLabel(), 15, TextAnchor.MiddleLeft);
            _volumeLabel.color = Color.white;
            _volumeLabel.fontStyle = FontStyle.Bold;
            _volumeLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            SetStretch(_volumeLabel.rectTransform, 18f, 0f, -164f, 0f);

            var sliderRoot = new GameObject("Slider");
            sliderRoot.transform.SetParent(root, false);
            var sliderRect = sliderRoot.AddComponent<RectTransform>();
            sliderRect.anchorMin = new Vector2(1f, 0.5f);
            sliderRect.anchorMax = new Vector2(1f, 0.5f);
            sliderRect.pivot = new Vector2(1f, 0.5f);
            sliderRect.sizeDelta = new Vector2(134f, 14f);
            sliderRect.anchoredPosition = new Vector2(-14f, 0f);

            _volumeSlider = sliderRoot.AddComponent<Slider>();
            _volumeSlider.minValue = 0f;
            _volumeSlider.maxValue = 1f;
            _volumeSlider.wholeNumbers = false;
            _volumeSlider.direction = Slider.Direction.LeftToRight;

            var background = CreateImage(sliderRect, "Background", new Color(0.07f, 0.025f, 0.16f, 0.62f), true);
            background.anchorMin = new Vector2(0f, 0.5f);
            background.anchorMax = new Vector2(1f, 0.5f);
            background.pivot = new Vector2(0.5f, 0.5f);
            background.sizeDelta = new Vector2(0f, 4f);
            background.anchoredPosition = Vector2.zero;

            var fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(sliderRect, false);
            var fillAreaRect = fillArea.AddComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0f, 0f);
            fillAreaRect.anchorMax = new Vector2(1f, 1f);
            fillAreaRect.offsetMin = new Vector2(3f, 0f);
            fillAreaRect.offsetMax = new Vector2(-3f, 0f);

            var fill = CreateImage(fillAreaRect, "Fill", ColorAccent, false);
            fill.anchorMin = new Vector2(0f, 0.5f);
            fill.anchorMax = new Vector2(1f, 0.5f);
            fill.pivot = new Vector2(0f, 0.5f);
            fill.sizeDelta = new Vector2(0f, 4f);
            fill.anchoredPosition = Vector2.zero;

            var handleArea = new GameObject("Handle Slide Area");
            handleArea.transform.SetParent(sliderRect, false);
            var handleAreaRect = handleArea.AddComponent<RectTransform>();
            handleAreaRect.anchorMin = new Vector2(0f, 0f);
            handleAreaRect.anchorMax = new Vector2(1f, 1f);
            handleAreaRect.offsetMin = new Vector2(5f, 0f);
            handleAreaRect.offsetMax = new Vector2(-5f, 0f);

            var handle = CreateImage(handleAreaRect, "Handle", Color.white, true);
            handle.anchorMin = new Vector2(0.5f, 0.5f);
            handle.anchorMax = new Vector2(0.5f, 0.5f);
            handle.pivot = new Vector2(0.5f, 0.5f);
            handle.sizeDelta = new Vector2(10f, 16f);
            handle.anchoredPosition = Vector2.zero;

            _volumeSlider.targetGraphic = handle.GetComponent<Image>();
            _volumeSlider.fillRect = fill;
            _volumeSlider.handleRect = handle;
            _volumeSlider.value = LibraryPreviewManager.PreviewDemoVolumeNormalized;
            _volumeSlider.onValueChanged.AddListener((UnityAction<float>)(value =>
            {
                LibraryPreviewManager.SetPreviewDemoVolumeNormalized(value);
                RefreshVolumeControl();
            }));
        }

        private static void RefreshVolumeControl()
        {
            if (_volumeLabel != null)
            {
                _volumeLabel.text = GetVolumeLabel();
            }
        }

        private static string GetVolumeLabel()
        {
            return LibraryPreviewManager.PreviewDemoVolumePercent <= 0
                ? "音量  静音"
                : $"音量  {LibraryPreviewManager.PreviewDemoVolumePercent}%";
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
                    LibraryUiSoundManager.Play(LibraryUiSound.Click);
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
            var albums = _selectedCategory == "Active"
                ? LibraryManager.Search(query).Where(album => album.IsActive)
                : LibraryManager.Search(query, _selectedCategory);
            return albums.Where(IsInActiveDifficultyRange);
        }

        private static bool IsInActiveDifficultyRange(LibraryAlbumEntry entry)
        {
            var difficulties = GetDifficultyValues(entry.Info).ToList();
            if (difficulties.Count == 0) return _activeMinDifficulty <= 0f;
            return difficulties.Any(value => value >= _activeMinDifficulty && value <= _activeMaxDifficulty);
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
                LibraryUiSoundManager.Play(LibraryUiSound.Click);
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
            SetStretch(title.rectTransform, 20f, -2f, -400f, -2f);
            title.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);

            var meta = CreateText(row, "Meta", FormatRowMeta(entry), 17, TextAnchor.MiddleLeft);
            meta.color = ColorTextDim;
            meta.horizontalOverflow = HorizontalWrapMode.Overflow;
            meta.verticalOverflow = VerticalWrapMode.Truncate;
            SetStretch(meta.rectTransform, 20f, 1f, -400f, 1f);
            meta.rectTransform.anchorMin = new Vector2(0f, 0f);
            meta.rectTransform.anchorMax = new Vector2(1f, 0.5f);

            var difficulty = CreateText(row, "Difficulty", FormatDifficultyBadges(entry.Info), 17, TextAnchor.MiddleRight);
            difficulty.color = new Color(1f, 1f, 1f, 0.78f);
            difficulty.fontStyle = FontStyle.Bold;
            difficulty.horizontalOverflow = HorizontalWrapMode.Overflow;
            difficulty.rectTransform.anchorMin = new Vector2(1f, 0.5f);
            difficulty.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            difficulty.rectTransform.pivot = new Vector2(1f, 0.5f);
            difficulty.rectTransform.sizeDelta = new Vector2(230f, 34f);
            difficulty.rectTransform.anchoredPosition = new Vector2(-142f, 0f);

            var action = CreateButton(row, "Action", entry.IsActive ? "移除" : "导入",
                entry.IsActive ? new Color(0.46f, 0.16f, 0.42f, 0.92f) : ColorAccent, () =>
                {
                    LibraryUiSoundManager.Play(entry.IsActive ? LibraryUiSound.Cancel : LibraryUiSound.Yes);
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

        private static string FormatDifficultyBadges(AlbumInfo info)
        {
            var values = new[] { info.Difficulty1, info.Difficulty2, info.Difficulty3, info.Difficulty4, info.Difficulty5 }
                .Where(value => !string.IsNullOrWhiteSpace(value) && value != "0")
                .Take(3)
                .Select(value => $"<color={GetDifficultyColor(value)}>{Escape(value)}</color>");
            var text = string.Join(" / ", values);
            return string.IsNullOrEmpty(text) ? "<color=#ffffff66>-</color>" : $"难度  {text}";
        }

        private static IEnumerable<float> GetDifficultyValues(AlbumInfo info)
        {
            var values = new[] { info.Difficulty1, info.Difficulty2, info.Difficulty3, info.Difficulty4, info.Difficulty5 };
            foreach (var value in values)
            {
                if (string.IsNullOrWhiteSpace(value) || value == "0") continue;
                if (float.TryParse(value, out var difficulty))
                    yield return difficulty;
            }
        }

        private static string GetDifficultyColor(string value)
        {
            if (!float.TryParse(value, out var difficulty)) return "#ff63d6";
            if (difficulty < 5f) return "#47f58d";
            if (difficulty < 8f) return "#ffe36d";
            if (difficulty < 10f) return "#ff9a4d";
            return "#ff63d6";
        }

        private static string FormatRowMeta(LibraryAlbumEntry entry)
        {
            var author = TruncateDisplay(entry.Info.Author, RowAuthorMaxLength);
            var charter = TruncateDisplay(GetMainCharter(entry.Info), RowCharterMaxLength);
            var category = TruncateDisplay(GetCategoryLabel(entry.Category), RowCategoryMaxLength);
            return $"{Escape(author)}  /  {Escape(charter)}  /  {Escape(category)}";
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

        private static string TruncateDisplay(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return "-";
            value = value.Trim();
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
        }
    }
}
