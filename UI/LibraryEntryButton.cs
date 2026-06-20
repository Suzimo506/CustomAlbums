using CustomAlbums.Managers;
using CustomAlbums.Utilities;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CustomAlbums.UI
{
    internal static class LibraryEntryButton
    {
        private const float MinWidth = 156f;
        private const float MaxWidth = 260f;
        private const float Height = 38f;
        private const float HorizontalPadding = 38f;
        private const float LeftOffset = 500f;
        private const float YOffset = -14f;

        private static readonly CustomAlbums.Utilities.Logger Logger = new(nameof(LibraryEntryButton));
        private static GameObject _root;
        private static Text _label;
        private static Sprite _roundedSprite;
        private static Font _font;
        private static float _nextCreateAttemptTime;

        public static void CreateOrRefresh()
        {
            if (LibraryWindow.IsOpen) return;

            Recover();
            if (_root == null) Create();
            Position();
        }

        public static void Reset()
        {
            _root = null;
            _label = null;
        }

        private static void Create()
        {
            if (Time.unscaledTime < _nextCreateAttemptTime) return;
            _nextCreateAttemptTime = Time.unscaledTime + 1f;

            var topPanel = GameObject.Find("UI/Standerd/PnlNavigation/Top")?.transform;
            var source = GameObject.Find("UI/Standerd/PnlNavigation/Top/BtnOption");
            if (topPanel == null || source == null)
            {
                return;
            }

            _root = GameObject.Instantiate(source, topPanel);
            _root.name = "BtnCustomAlbumsLibrary";
            _root.SetActive(true);
            RemoveNativeBindings(_root);

            var rect = _root.GetComponent<RectTransform>();
            var image = _root.GetComponent<Image>() ?? _root.AddComponent<Image>();
            image.sprite = GetRoundedSprite();
            image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            image.color = new Color(0.55f, 0.38f, 0.88f, 0.92f);
            image.raycastTarget = true;

            var shadow = _root.GetComponent<Shadow>() ?? _root.AddComponent<Shadow>();
            shadow.effectDistance = new Vector2(1f, -1f);
            shadow.effectColor = new Color(0.08f, 0.02f, 0.18f, 0.42f);

            var icon = _root.transform.Find("ImgIcon");
            if (icon != null) GameObject.Destroy(icon.gameObject);

            var button = _root.GetComponent<Button>() ?? _root.AddComponent<Button>();
            button.targetGraphic = image;
            button.colors = new ColorBlock
            {
                normalColor = new Color(0.55f, 0.38f, 0.88f, 0.92f),
                highlightedColor = new Color(0.66f, 0.46f, 1f, 0.96f),
                pressedColor = new Color(0.42f, 0.26f, 0.76f, 0.98f),
                selectedColor = new Color(0.60f, 0.42f, 0.94f, 0.96f),
                disabledColor = new Color(0.34f, 0.28f, 0.42f, 0.72f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f
            };
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener((UnityAction)new Action(() =>
            {
                LibraryUiSoundManager.Play(LibraryUiSound.Yes);
                LibraryWindow.Show();
            }));

            var textObj = new GameObject("TxtLabel");
            var textRect = textObj.AddComponent<RectTransform>();
            textRect.SetParent(rect, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(12f, 0f);
            textRect.offsetMax = new Vector2(-12f, 0f);

            _label = textObj.AddComponent<Text>();
            ApplyFont(_label);
            _label.text = "导入自制谱";
            _label.fontSize = 18;
            _label.alignment = TextAnchor.MiddleCenter;
            _label.horizontalOverflow = HorizontalWrapMode.Overflow;
            _label.verticalOverflow = VerticalWrapMode.Overflow;
            _label.supportRichText = true;
            _label.raycastTarget = false;
            _label.color = new Color(1f, 0.92f, 0.14f, 1f);

            Position();
            Logger.Msg("Library entrance button injected.", false);
        }

        private static void Position()
        {
            if (_root == null || _label == null) return;

            var rect = _root.GetComponent<RectTransform>();
            var sourceRect = GameObject.Find("UI/Standerd/PnlNavigation/Top/BtnOption")?.GetComponent<RectTransform>();
            if (rect == null || sourceRect == null) return;

            rect.anchorMin = new Vector2(0f, sourceRect.anchorMin.y);
            rect.anchorMax = new Vector2(0f, sourceRect.anchorMax.y);
            rect.pivot = new Vector2(0f, sourceRect.pivot.y);
            rect.anchoredPosition = new Vector2(
                LeftOffset,
                sourceRect.anchoredPosition.y + YOffset);
            rect.sizeDelta = new Vector2(GetWidth(), Height);
            rect.localScale = Vector3.one;
            rect.SetAsLastSibling();
        }

        private static float GetWidth()
        {
            if (_label == null) return MinWidth;
            return Mathf.Clamp(_label.preferredWidth + HorizontalPadding, MinWidth, MaxWidth);
        }

        private static void Recover()
        {
            if (_root != null) return;

            _root = GameObject.Find("UI/Standerd/PnlNavigation/Top/BtnCustomAlbumsLibrary");
            if (_root == null) return;

            _label = _root.transform.Find("TxtLabel")?.GetComponent<Text>();
        }

        private static void ApplyFont(Text text)
        {
            if (text == null) return;
            text.font = GetFont();
        }

        private static void RemoveNativeBindings(GameObject target)
        {
            var keyBinding = target.GetComponent("InputKeyBinding");
            if (keyBinding != null) GameObject.Destroy(keyBinding);

            var eventTrigger = target.GetComponent<UnityEngine.EventSystems.EventTrigger>();
            if (eventTrigger != null) GameObject.Destroy(eventTrigger);
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
    }
}
