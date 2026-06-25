using System.IO.Compression;
using CustomAlbums.Data;
using CustomAlbums.Utilities;
using Il2CppAssets.Scripts.Database;
using Il2CppPeroPeroGames.GlobalDefines;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using UnityEngine;

namespace CustomAlbums.Managers
{
    internal static class LibraryPreviewManager
    {
        private static readonly CustomAlbums.Utilities.Logger Logger = new(nameof(LibraryPreviewManager));
        private static readonly Dictionary<AudioSource, float> MutedSources = new();
        private const float PreviewDemoVolumeScale = 1f;
        private const long MaxPreviewGifBytes = 32L * 1024L * 1024L;
        private const int MaxPreviewGifDimension = 1024;
        private static AudioSource _previewSource;
        private static AudioClip _previewClip;
        private static Sprite _previewCover;
        private static readonly Configuration ImageConfig = CreateImageSharpConfiguration();
        private const uint PreviewGifMaxFrames = 16;

        private static Configuration CreateImageSharpConfiguration()
        {
            var configuration = Configuration.Default.Clone();
            configuration.PreferContiguousImageBuffers = true;
            return configuration;
        }

        public static void MuteGameDemo()
        {
            MutedSources.Clear();
            EnforceGameDemoMute();
        }

        public static void Update()
        {
            EnforceGameDemoMute();
            ApplyPreviewDemoVolume();
        }

        private static void EnforceGameDemoMute()
        {
            foreach (var source in UnityEngine.Object.FindObjectsOfType<AudioSource>())
            {
                if (!ShouldMuteSource(source)) continue;
                if (!MutedSources.ContainsKey(source))
                    MutedSources[source] = source.volume;
                source.volume = 0f;
            }
        }

        private static bool ShouldMuteSource(AudioSource source)
        {
            return source != null &&
                   source != _previewSource &&
                   !LibraryUiSoundManager.IsSource(source) &&
                   source.isPlaying &&
                   source.gameObject.name != "CustomAlbumsLibraryPreviewAudio";
        }

        public static void RestoreGameDemo()
        {
            foreach (var (source, volume) in MutedSources)
            {
                if (source != null)
                    source.volume = volume > 0.001f ? volume : GetGameMusicVolume();
            }
            MutedSources.Clear();
        }

        public static Sprite LoadCover(LibraryAlbumEntry entry)
        {
            DestroyCover();
            if (entry == null || (!entry.HasPng && !entry.HasGif && !entry.HasWebp)) return null;

            try
            {
                using var zip = ZipFile.OpenRead(GetPath(entry));
                var pngCover = zip.GetEntry("cover.png");
                if (pngCover != null) return LoadPngCover(pngCover);

                var webpCover = zip.GetEntry("cover.webp");
                if (webpCover != null) return LoadWebpCover(webpCover);

                var gifCover = zip.GetEntry("cover.gif");
                return gifCover == null ? null : LoadGifCover(gifCover);
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to load library cover {entry.RelativePath}: {ex.Message}");
                return null;
            }
        }

        private static Sprite LoadPngCover(ZipArchiveEntry cover)
        {
            using var stream = cover.Open().ToMemoryStream();
            var texture = new Texture2D(2, 2, TextureFormat.ARGB32, false)
            {
                wrapMode = TextureWrapMode.Clamp
            };
            texture.LoadImage(stream.ReadFully().CopyFromManaged());
            return SetPreviewCover(texture);
        }

        private static Sprite LoadWebpCover(ZipArchiveEntry cover)
        {
            using var stream = cover.Open();
            using var image = Image.Load<Rgba32>(new DecoderOptions { Configuration = ImageConfig }, stream);
            image.Mutate(context => context.Flip(FlipMode.Vertical));

            var buffer = CopyImagePixels(image);

            var texture = new Texture2D(image.Width, image.Height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp
            };
            texture.LoadRawTextureData(buffer.CopyFromManaged());
            texture.Apply(false, true);
            return SetPreviewCover(texture);
        }

        private static Sprite LoadGifCover(ZipArchiveEntry cover)
        {
            if (cover.Length > MaxPreviewGifBytes)
            {
                Logger.Warning($"Skipping library preview GIF: file is too large ({cover.Length} bytes).");
                return null;
            }

            using var stream = cover.Open();
            var decodeOptions = new DecoderOptions
            {
                Configuration = ImageConfig,
                MaxFrames = PreviewGifMaxFrames,
                SkipMetadata = false
            };
            var info = Image.Identify(decodeOptions, stream);
            if (info == null ||
                info.Width > MaxPreviewGifDimension ||
                info.Height > MaxPreviewGifDimension)
            {
                Logger.Warning("Skipping library preview GIF: dimensions are too large.");
                return null;
            }
            stream.Position = 0;

            using var gif = Image.Load<Rgba32>(decodeOptions, stream);
            if (gif.Frames.Count == 0) return null;

            gif.Mutate(context => context.Flip(FlipMode.Vertical));
            var frame = FindBestPreviewFrame(gif);
            if (frame.Width > MaxPreviewGifDimension || frame.Height > MaxPreviewGifDimension)
            {
                Logger.Warning($"Skipping library preview GIF: frame too large ({frame.Width}x{frame.Height}).");
                return null;
            }
            var buffer = CopyFramePixels(frame);

            var texture = new Texture2D(frame.Width, frame.Height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp
            };
            texture.LoadRawTextureData(buffer.CopyFromManaged());
            texture.Apply(false, true);
            return SetPreviewCover(texture);
        }

        private static ImageFrame<Rgba32> FindBestPreviewFrame(Image<Rgba32> gif)
        {
            var bestFrame = gif.Frames.RootFrame;
            var bestScore = -1;
            foreach (var frame in gif.Frames)
            {
                var score = EstimateVisiblePixels(frame);
                if (score <= bestScore) continue;
                bestFrame = frame;
                bestScore = score;
            }

            return bestFrame;
        }

        private static int EstimateVisiblePixels(ImageFrame<Rgba32> frame)
        {
            var buffer = CopyFramePixels(frame);
            var score = 0;
            for (var i = 0; i + 3 < buffer.Length; i += 4)
            {
                if (buffer[i + 3] <= 16) continue;
                score++;
                if (buffer[i] + buffer[i + 1] + buffer[i + 2] > 30)
                    score += 2;
            }

            return score;
        }

        private static byte[] CopyImagePixels(Image<Rgba32> image)
        {
            var buffer = new byte[image.Width * image.Height * 4];
            image.CopyPixelDataTo(buffer);
            return buffer;
        }

        private static byte[] CopyFramePixels(ImageFrame<Rgba32> frame)
        {
            var buffer = new byte[frame.Width * frame.Height * 4];
            frame.CopyPixelDataTo(buffer);
            return buffer;
        }

        private static Sprite SetPreviewCover(Texture2D texture)
        {
            _previewCover = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            return _previewCover;
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
                ApplyPreviewDemoVolume();
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

        private static void ApplyPreviewDemoVolume()
        {
            if (_previewSource != null)
            {
                _previewSource.volume = PreviewDemoVolumeScale * GetGameMusicVolume();
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

        private static float GetGameMusicVolume()
        {
            try
            {
                return Mathf.Clamp01(DataHelper.GetVolume(PeroAudioType.Music));
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to read game music volume: {ex.Message}");
                return 1f;
            }
        }
    }
}
