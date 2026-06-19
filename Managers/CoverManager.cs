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
        public GifAlbumData(Album album, RawFrame[] frames, int fps)
        {
            Album = album;  
            Frames = frames;
            FramesPerSecond = fps;
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

        private static readonly Configuration Config = Configuration.Default;

        public static Sprite GetCover(this Album album)
        {
            if (!album.HasPng) return null;
            if (CachedCovers.TryGetValue(album.Index, out var cached)) return cached;

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

        private static readonly System.Collections.Concurrent.ConcurrentQueue<Album> GifQueue = new();
        private static int _isProcessingQueue;

        // Enqueue charts that need GIF loading to the serial queue
        public static void EnqueueGifToLoad(Album album)
        {
            if (album == null || !album.HasGif) return;
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
                
                Config.PreferContiguousImageBuffers = true;

                using var stream = album.OpenNullableStream("cover.gif");
                if (stream is null) return;

                using var gif = await Image.LoadAsync<Rgba32>(new DecoderOptions { Configuration = Config }, stream);
                if (!IsGifWithinBudget(album, gif))
                    return;

                gif.Mutate(c => c.Flip(FlipMode.Vertical));

                var rawFrames = new RawFrame[gif.Frames.Count];
                
                Parallel.For(0, gif.Frames.Count, i =>
                {
                    var frame = gif.Frames[i];
                    if (frame.DangerousTryGetSinglePixelMemory(out var memory))
                    {
                        var buffer = new byte[memory.Length * Unsafe.SizeOf<Rgba32>()];
                        memory.Span.CopyTo(MemoryMarshal.Cast<byte, Rgba32>(buffer.AsSpan()));
                        rawFrames[i] = new RawFrame(frame.Width, frame.Height, buffer);
                    }
                });

                GifAlbumDatas.Enqueue(new(album, rawFrames, gif.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay * 10));
            }
            catch (Exception ex)
            {
                // Catch exceptions to prevent crashing
                Logger.Warning($"Failed to load animated cover for {album.AlbumName}. Reason: {ex.Message}");
            }
        }

        private static bool IsGifWithinBudget(Album album, Image<Rgba32> gif)
        {
            var frameCount = gif.Frames.Count;
            if (frameCount <= 0) return false;
            if (frameCount > MaxGifFrames)
            {
                Logger.Warning($"Skipping animated cover for {album.AlbumName}: too many frames ({frameCount}).");
                return false;
            }

            long totalPixels = 0;
            foreach (var frame in gif.Frames)
            {
                if (frame.Width > MaxGifDimension || frame.Height > MaxGifDimension)
                {
                    Logger.Warning($"Skipping animated cover for {album.AlbumName}: frame too large ({frame.Width}x{frame.Height}).");
                    return false;
                }

                totalPixels += (long)frame.Width * frame.Height;
                if (totalPixels > MaxGifTotalPixels)
                {
                    Logger.Warning($"Skipping animated cover for {album.AlbumName}: total pixel budget exceeded.");
                    return false;
                }
            }

            return true;
        }

        public static AnimatedCover GetAnimatedCover(this Album album)
            => CachedAnimatedCovers.GetValueOrDefault(album.Index);

        public static AnimatedCover LoadAnimatedCover(GifAlbumData data)
        {
            if (CachedAnimatedCovers.TryGetValue(data.Album.Index, out var cached)) return cached;

            var rawFrames = data.Frames;
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
            CachedAnimatedCovers.Add(data.Album.Index, cover);

            return cover;
        }
    }
}
