using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CustomAlbums.Data;
using CustomAlbums.Utilities;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using UnityEngine;
using Logger = CustomAlbums.Utilities.Logger;

namespace CustomAlbums.Managers
{
    public readonly struct RawFrame
    {
        public readonly int Width;
        public readonly int Height;
        public readonly byte[] Buffer;

        public RawFrame(int width, int height, byte[] buffer)
        {
            Width = width;
            Height = height;
            Buffer = buffer;
        }
    } 

    public class GifAlbumData
    {
        public readonly Album Album;
        public readonly RawFrame[] Frames;
        public readonly int FramesPerSecond;
        public readonly int OriginalFrameCount;
        public GifAlbumData(Album album, RawFrame[] frames, int fps, int originalFrameCount)
        {
            Album = album;  
            Frames = frames;
            FramesPerSecond = fps;
            OriginalFrameCount = originalFrameCount;
        }
    }
    public static class CoverManager
    {
        internal static readonly ConcurrentQueue<GifAlbumData> GifAlbumDatas = new();
        internal static readonly Dictionary<int, Sprite> CachedCovers = new();
        internal static readonly Dictionary<int, AnimatedCover> CachedAnimatedCovers = new();
        private static readonly Logger Logger = new(nameof(CoverManager));
        private const int MaxGifFrames = 96;
        private const int MaxGifDimension = 1024;
        private const long MaxGifTotalPixels = 64L * 1024L * 1024L;
        private const long MaxGifBytes = 32L * 1024L * 1024L;

        private static readonly Configuration Config = Configuration.Default;
        private static readonly ConcurrentDictionary<int, byte> PendingGifAlbums = new();
        private static readonly ConcurrentDictionary<int, byte> FailedGifAlbums = new();

        public static Sprite GetCover(this Album album)
        {
            if (album == null || (!album.HasPng && !album.HasGif && !album.HasWebp)) return null;
            if (CachedCovers.TryGetValue(album.Index, out var cached)) return cached;

            if (album.HasPng) return LoadPngCover(album);
            if (album.HasWebp) return LoadWebpCover(album);

            EnqueueGifToLoad(album);
            return GetGifPlaceholderCover(album);
        }

        private static Sprite LoadPngCover(Album album)
        {
            using var stream = album.OpenNullableStream("cover.png")?.ToMemoryStream();
            if (stream is null) return null;

            var bytes = stream.ReadFully();

            // Create the textures
            var texture = new Texture2D(2, 2, TextureFormat.ARGB32, false)
            {
                wrapMode = TextureWrapMode.MirrorOnce
            };
            texture.LoadImage(bytes.CopyFromManaged());

            var cover = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            CachedCovers.Add(album.Index, cover);

            return cover;
        }

        private static Sprite LoadWebpCover(Album album)
        {
            using var stream = album.OpenNullableStream("cover.webp");
            if (stream is null) return null;

            Config.PreferContiguousImageBuffers = true;
            using var image = Image.Load<Rgba32>(new DecoderOptions { Configuration = Config }, stream);
            image.Mutate(context => context.Flip(FlipMode.Vertical));

            if (!image.DangerousTryGetSinglePixelMemory(out var memory)) return null;

            var buffer = new byte[memory.Length * Unsafe.SizeOf<Rgba32>()];
            memory.Span.CopyTo(MemoryMarshal.Cast<byte, Rgba32>(buffer.AsSpan()));

            var texture = new Texture2D(image.Width, image.Height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.MirrorOnce
            };
            texture.LoadRawTextureData(buffer.CopyFromManaged());
            texture.Apply(false, true);

            var cover = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            CachedCovers.Add(album.Index, cover);

            return cover;
        }

        private static Sprite GetGifPlaceholderCover(Album album)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.MirrorOnce
            };
            texture.LoadRawTextureData(new byte[16].CopyFromManaged());
            texture.Apply(false, true);

            var cover = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            CachedCovers[album.Index] = cover;
            return cover;
        }

        private static readonly System.Collections.Concurrent.ConcurrentQueue<Album> GifQueue = new();
        private static int _isProcessingQueue;

        // Enqueue charts that need GIF loading to the serial queue
        public static void EnqueueGifToLoad(Album album)
        {
            if (album == null || !album.HasGif) return;
            if (CachedAnimatedCovers.ContainsKey(album.Index) || FailedGifAlbums.ContainsKey(album.Index)) return;
            if (!PendingGifAlbums.TryAdd(album.Index, 0)) return;
            GifQueue.Enqueue(album);
            if (Interlocked.Exchange(ref _isProcessingQueue, 1) == 0)
                Task.Run(ProcessGifQueue);
        }

        // Background single-thread loop to process GIF decoding in the queue
        private static async Task ProcessGifQueue()
        {
            try
            {
                while (GifQueue.TryDequeue(out var album))
                {
                    await LoadGif(album);
                }
            }
            finally
            {
                Interlocked.Exchange(ref _isProcessingQueue, 0);
                if (!GifQueue.IsEmpty && Interlocked.Exchange(ref _isProcessingQueue, 1) == 0)
                    _ = Task.Run(ProcessGifQueue);
            }
        }

        public static async Task LoadGif(this Album album)
        {
            try
            {
                if (!album.HasGif) return;
                using var stream = album.OpenNullableStream("cover.gif");
                if (stream is null)
                {
                    MarkGifFailed(album);
                    return;
                }
                if (stream.Length > MaxGifBytes)
                {
                    Logger.Warning($"Skipping animated cover for {album.AlbumName}: GIF is too large ({stream.Length} bytes).");
                    MarkGifFailed(album);
                    return;
                }
                
                Config.PreferContiguousImageBuffers = true;

                var info = Image.Identify(new DecoderOptions { Configuration = Config }, stream);
                if (info == null ||
                    info.Width > MaxGifDimension ||
                    info.Height > MaxGifDimension)
                {
                    Logger.Warning($"Skipping animated cover for {album.AlbumName}: GIF dimensions are too large.");
                    MarkGifFailed(album);
                    return;
                }
                stream.Position = 0;

                using var gif = await Image.LoadAsync<Rgba32>(new DecoderOptions { Configuration = Config }, stream);
                var sampledFrameIndexes = GetSampledFrameIndexes(album, gif);
                if (sampledFrameIndexes.Length == 0)
                {
                    MarkGifFailed(album);
                    return;
                }

                gif.Mutate(c => c.Flip(FlipMode.Vertical));

                var rawFrames = new RawFrame[sampledFrameIndexes.Length];

                Parallel.For(0, sampledFrameIndexes.Length, i =>
                {
                    var frame = gif.Frames[sampledFrameIndexes[i]];
                    if (frame.DangerousTryGetSinglePixelMemory(out var memory))
                    {
                        var buffer = new byte[memory.Length * Unsafe.SizeOf<Rgba32>()];
                        memory.Span.CopyTo(MemoryMarshal.Cast<byte, Rgba32>(buffer.AsSpan()));
                        rawFrames[i] = new RawFrame(frame.Width, frame.Height, buffer);
                    }
                });

                var framesPerSecond = CalculateSampledFramesPerSecond(gif, sampledFrameIndexes.Length);
                GifAlbumDatas.Enqueue(new(album, rawFrames, framesPerSecond, gif.Frames.Count));
            }
            catch (Exception ex)
            {
                // Catch exceptions to prevent crashing
                Logger.Warning($"Failed to load animated cover for {album.AlbumName}. Reason: {ex.Message}");
                MarkGifFailed(album);
            }
        }

        private static int[] GetSampledFrameIndexes(Album album, Image<Rgba32> gif)
        {
            var frameCount = gif.Frames.Count;
            if (frameCount <= 0) return Array.Empty<int>();

            long totalPixels = 0;
            var overPixelBudget = false;
            foreach (var frame in gif.Frames)
            {
                if (frame.Width > MaxGifDimension || frame.Height > MaxGifDimension)
                {
                    Logger.Warning($"Skipping animated cover for {album.AlbumName}: frame too large ({frame.Width}x{frame.Height}).");
                    return Array.Empty<int>();
                }

                totalPixels += (long)frame.Width * frame.Height;
                if (totalPixels > MaxGifTotalPixels)
                {
                    overPixelBudget = true;
                }
            }

            if (overPixelBudget)
                Logger.Msg($"Sampling animated cover for {album.AlbumName}: total pixel budget exceeded.", false);
            return SampleFrameIndexes(frameCount, MaxGifFrames);
        }

        private static int[] SampleFrameIndexes(int frameCount, int maxFrames)
        {
            if (frameCount <= 0) return Array.Empty<int>();
            if (frameCount <= maxFrames) return Enumerable.Range(0, frameCount).ToArray();

            var indexes = new int[maxFrames];
            for (var i = 0; i < maxFrames; i++)
                indexes[i] = Math.Min(frameCount - 1, (int)Math.Round(i * (frameCount - 1) / (double)(maxFrames - 1)));

            return indexes
                .Distinct()
                .ToArray();
        }

        private static int CalculateSampledFramesPerSecond(Image<Rgba32> gif, int sampledFrameCount)
        {
            if (sampledFrameCount <= 0) return 10;

            var totalDelay = 0;
            foreach (var frame in gif.Frames)
            {
                var delay = frame.Metadata.GetGifMetadata().FrameDelay;
                totalDelay += delay > 0 ? delay : 10;
            }

            if (totalDelay <= 0) return 10;
            return Math.Max(1, (int)Math.Round(sampledFrameCount * 100d / totalDelay));
        }

        public static AnimatedCover GetAnimatedCover(this Album album)
            => CachedAnimatedCovers.GetValueOrDefault(album.Index);

        public static bool HasAnimatedCoverFailed(Album album)
            => album != null && FailedGifAlbums.ContainsKey(album.Index);

        internal static void ClearCache(int albumIndex)
        {
            CachedCovers.Remove(albumIndex);
            CachedAnimatedCovers.Remove(albumIndex);
            PendingGifAlbums.TryRemove(albumIndex, out _);
            FailedGifAlbums.TryRemove(albumIndex, out _);
        }

        public static AnimatedCover LoadAnimatedCover(GifAlbumData data)
        {
            try
            {
                if (CachedAnimatedCovers.TryGetValue(data.Album.Index, out var cached))
                {
                    PendingGifAlbums.TryRemove(data.Album.Index, out _);
                    return cached;
                }

                var rawFrames = data.Frames
                    .Where(frame => frame.Buffer != null && frame.Width > 0 && frame.Height > 0)
                    .ToArray();
                if (rawFrames.Length == 0)
                {
                    MarkGifFailed(data.Album);
                    return null;
                }

                var sprites = new Sprite[rawFrames.Length];

                for (var i = 0; i < rawFrames.Length; i++)
                {
                    var frame = rawFrames[i];

                    // Create the textures
                    var texture = new Texture2D(frame.Width, frame.Height, TextureFormat.RGBA32, false)
                    {
                        wrapMode = TextureWrapMode.MirrorOnce
                    };
                    texture.LoadRawTextureData(frame.Buffer.CopyFromManaged());
                    texture.Apply(false, true);

                    // Create the sprite with the given texture and add it to the sprites array
                    var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f));
                    sprite.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                    sprites[i] = sprite;
                }

                // Create and add cover to cache
                var cover = new AnimatedCover(sprites, data.FramesPerSecond);
                CachedCovers[data.Album.Index] = sprites[0];
                CachedAnimatedCovers[data.Album.Index] = cover;
                if (data.OriginalFrameCount > data.Frames.Length)
                    Logger.Msg($"Sampled animated cover for {data.Album.AlbumName}: {data.OriginalFrameCount} -> {data.Frames.Length} frames.", false);
                PendingGifAlbums.TryRemove(data.Album.Index, out _);
                FailedGifAlbums.TryRemove(data.Album.Index, out _);

                return cover;
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to create animated cover for {data.Album.AlbumName}. Reason: {ex.Message}");
                MarkGifFailed(data.Album);
                return null;
            }
        }

        private static void MarkGifFailed(Album album)
        {
            if (album == null) return;
            PendingGifAlbums.TryRemove(album.Index, out _);
            FailedGifAlbums.TryAdd(album.Index, 0);
        }
    }
}
