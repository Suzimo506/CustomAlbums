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
    public static class CoverManager
    {
        internal static readonly Dictionary<int, Sprite> CachedCovers = new();
        internal static readonly Dictionary<int, AnimatedCover> CachedAnimatedCovers = new();
        private static readonly Logger Logger = new(nameof(CoverManager));

        private static readonly Configuration Config = CreateImageSharpConfiguration();

        private static Configuration CreateImageSharpConfiguration()
        {
            var configuration = Configuration.Default.Clone();
            configuration.PreferContiguousImageBuffers = true;
            return configuration;
        }

        public static Sprite GetCover(this Album album)
        {
            if (album == null || (!album.HasPng && !album.HasGif && !album.HasWebp)) return null;
            if (CachedCovers.TryGetValue(album.Index, out var cached)) return cached;

            if (album.HasPng) return LoadPngCover(album);
            if (album.HasWebp) return LoadWebpCover(album);
            return null;
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

            using var image = Image.Load<Rgba32>(new DecoderOptions { Configuration = Config }, stream);
            image.Mutate(context => context.Flip(FlipMode.Vertical));

            var buffer = CopyImagePixels(image);

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

        public static AnimatedCover GetAnimatedCover(this Album album)
        {
            try
            {
                if (album == null || !album.HasGif) return null;
                if (CachedAnimatedCovers.TryGetValue(album.Index, out var cached)) return cached;

                using var stream = album.OpenNullableStream("cover.gif");
                if (stream is null) return null;

                using var gif = Image.Load<Rgba32>(new DecoderOptions { Configuration = Config }, stream);
                if (gif.Frames.Count == 0) return null;

                gif.Mutate(c => c.Flip(FlipMode.Vertical));

                var sprites = new Sprite[gif.Frames.Count];

                for (var i = 0; i < gif.Frames.Count; i++)
                {
                    var frame = gif.Frames[i];
                    var buffer = CopyFramePixels(frame);

                    var texture = new Texture2D(frame.Width, frame.Height, TextureFormat.RGBA32, false)
                    {
                        wrapMode = TextureWrapMode.MirrorOnce
                    };
                    texture.LoadRawTextureData(buffer.CopyFromManaged());
                    texture.Apply(false);

                    var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f));
                    sprite.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                    sprites[i] = sprite;
                }

                var frameDelay = gif.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay;
                var cover = new AnimatedCover(sprites, (frameDelay > 0 ? frameDelay : 10) * 10);
                CachedCovers[album.Index] = sprites[0];
                CachedAnimatedCovers[album.Index] = cover;
                return cover;
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to load animated cover for {album?.AlbumName}. Reason: {ex.Message}");
                return null;
            }
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

        internal static void ClearCache(int albumIndex)
        {
            CachedCovers.Remove(albumIndex);
            CachedAnimatedCovers.Remove(albumIndex);
        }
    }
}
