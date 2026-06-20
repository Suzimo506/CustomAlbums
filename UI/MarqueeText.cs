using UnityEngine;
using UnityEngine.UI;
using System;

namespace CustomAlbums.UI
{
    internal class MarqueeText : MonoBehaviour
    {
        private const float ScrollSpeed = 34f;
        private const float PauseSeconds = 0.8f;
        private const float Gap = 28f;

        private RectTransform _viewport;
        private RectTransform _textRect;
        private Text _text;
        private float _overflow;
        private float _pauseTimer;
        private int _settleFrames;

        public MarqueeText(IntPtr ptr) : base(ptr)
        {
        }

        internal void Initialize(RectTransform viewport, Text text)
        {
            _viewport = viewport;
            _text = text;
            _textRect = text.rectTransform;
            _settleFrames = 2;
            enabled = true;
        }

        internal void Refresh()
        {
            if (_viewport == null || _text == null || _textRect == null) return;

            _textRect.anchoredPosition = Vector2.zero;
            _pauseTimer = PauseSeconds;
            _settleFrames = 0;

            var preferredWidth = _text.preferredWidth;
            _overflow = Mathf.Max(0f, preferredWidth - _viewport.rect.width);
            _textRect.sizeDelta = new Vector2(preferredWidth + Gap, _textRect.sizeDelta.y);
            enabled = _overflow > 0.5f;
        }

        private void Update()
        {
            if (_settleFrames > 0)
            {
                _settleFrames--;
                if (_settleFrames == 0) Refresh();
                return;
            }

            if (_overflow <= 0f || _textRect == null) return;

            if (_pauseTimer > 0f)
            {
                _pauseTimer -= Time.unscaledDeltaTime;
                return;
            }

            var x = _textRect.anchoredPosition.x - ScrollSpeed * Time.unscaledDeltaTime;
            if (x <= -_overflow - Gap)
            {
                x = Gap;
                _pauseTimer = PauseSeconds;
            }

            _textRect.anchoredPosition = new Vector2(x, 0f);
        }
    }
}
