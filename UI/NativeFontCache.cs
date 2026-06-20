using UnityEngine;
using UnityEngine.UI;

namespace CustomAlbums.UI
{
    internal static class NativeFontCache
    {
        private static Font _font;
        private static Material _material;

        public static void ApplyTo(Text text)
        {
            if (text == null) return;

            if (TryResolveNativeFont())
            {
                text.font = _font;
                if (_material != null) text.material = _material;
                return;
            }

            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        private static bool TryResolveNativeFont()
        {
            if (_font != null) return true;

            foreach (var text in Resources.FindObjectsOfTypeAll<Text>())
            {
                if (!IsCandidate(text)) continue;

                var fontName = text.font.name ?? string.Empty;
                if (fontName.IndexOf("Arial", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                _font = text.font;
                _material = text.material;
                return true;
            }

            return false;
        }

        private static bool IsCandidate(Text text)
        {
            if (text == null || text.font == null || text.transform == null) return false;

            for (var current = text.transform; current != null; current = current.parent)
            {
                var name = current.name ?? string.Empty;
                if (name == "CustomAlbumsLibraryWindow" || name == "BtnCustomAlbumsLibrary")
                    return false;
            }

            return true;
        }
    }
}
