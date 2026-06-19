using System.IO.Compression;
using CustomAlbums.Data;
using CustomAlbums.Utilities;
using UnityEngine;

namespace CustomAlbums.Managers
{
    internal static class LibraryPreviewManager
    {
        private static readonly CustomAlbums.Utilities.Logger Logger = new(nameof(LibraryPreviewManager));
        private static readonly Dictionary<AudioSource, float> MutedSources = new();
        private const float PreviewDemoVolume = 0.15f;
        private static AudioSource _previewSource;
        private static AudioClip _previewClip;
        private static Sprite _previewCover;

        public static void MuteGameDemo()
        {
            MutedSources.Clear();
            foreach (var source in UnityEngine.Object.FindObjectsOfType<AudioSource>())
            {
                if (source == null || !source.isPlaying || source.clip == null) continue;
                MutedSources[source] = source.volume;
                source.volume = 0f;
            }
        }

        public static void RestoreGameDemo()
        {
            foreach (var (source, volume) in MutedSources)
            {
                if (source != null) source.volume = volume;
            }
            MutedSources.Clear();
        }

        public static Sprite LoadCover(LibraryAlbumEntry entry)
        {
            DestroyCover();
            if (entry == null || !entry.HasPng) return null;

            try
            {
                using var zip = ZipFile.OpenRead(GetPath(entry));
                var cover = zip.GetEntry("cover.png");
                if (cover == null) return null;

                using var stream = cover.Open().ToMemoryStream();
                var texture = new Texture2D(2, 2, TextureFormat.ARGB32, false)
                {
                    wrapMode = TextureWrapMode.Clamp
                };
                texture.LoadImage(stream.ReadFully().CopyFromManaged());
                _previewCover = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                return _previewCover;
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to load library cover {entry.RelativePath}: {ex.Message}");
                return null;
            }
        }

        public static void PlayDemo(LibraryAlbumEntry entry)
        {
            StopDemo();
            if (entry == null) return;

            try
            {
                using var zip = ZipFile.OpenRead(GetPath(entry));
                var demo = zip.GetEntry("demo.ogg");
                var extension = "ogg";
                if (demo == null)
                {
                    demo = zip.GetEntry("demo.mp3");
                    extension = "mp3";
                }

                if (demo == null) return;

                var stream = demo.Open().ToMemoryStream();
                var key = $"library_preview_{Path.GetFileNameWithoutExtension(entry.FileName)}";
                _previewClip = extension == "ogg"
                    ? AudioManager.LoadClipFromOgg(stream, key)
                    : AudioManager.LoadClipFromMp3(stream, key);

                EnsureSource();
                _previewSource.clip = _previewClip;
                _previewSource.volume = PreviewDemoVolume;
                _previewSource.loop = true;
                _previewSource.Play();
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to play library demo {entry.RelativePath}: {ex.Message}");
            }
        }

        public static void StopDemo()
        {
            if (_previewSource != null)
            {
                _previewSource.Stop();
                _previewSource.clip = null;
            }

            if (_previewClip != null)
            {
                UnityEngine.Object.Destroy(_previewClip);
                _previewClip = null;
            }
        }

        public static void Cleanup()
        {
            StopDemo();
            DestroyCover();
            RestoreGameDemo();

            if (_previewSource != null)
            {
                UnityEngine.Object.Destroy(_previewSource.gameObject);
                _previewSource = null;
            }
        }

        private static void EnsureSource()
        {
            if (_previewSource != null) return;

            var obj = new GameObject("CustomAlbumsLibraryPreviewAudio");
            UnityEngine.Object.DontDestroyOnLoad(obj);
            _previewSource = obj.AddComponent<AudioSource>();
            _previewSource.playOnAwake = false;
        }

        private static void DestroyCover()
        {
            if (_previewCover == null) return;
            var texture = _previewCover.texture;
            UnityEngine.Object.Destroy(_previewCover);
            if (texture != null) UnityEngine.Object.Destroy(texture);
            _previewCover = null;
        }

        private static string GetPath(LibraryAlbumEntry entry)
        {
            return Path.Combine(LibraryManager.LibraryPath, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
