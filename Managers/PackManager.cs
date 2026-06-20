using CustomAlbums.Data;
using CustomAlbums.Utilities;
using System.IO.Compression;

namespace CustomAlbums.Managers
{
    public class PackManager
    {
        private static readonly List<Pack> Packs = new();
        internal static void Clear()
        {
            Packs.Clear();
        }

        public static Pack GetPackFromUid(string uid)
        {
            // If the uid is not custom or parsing the index fails
            if (string.IsNullOrEmpty(uid) || !uid.StartsWith($"{AlbumManager.Uid}-") ||
                !uid[4..].TryParseAsInt(out var uidIndex)) return null;

            var exactPack = Packs.FirstOrDefault(pack =>
                pack.Albums.Any(album => album.Index == uidIndex));
            if (exactPack != null) return exactPack;

            // Retrieve the pack that the uid belongs to
            var retrievedPack = Packs.FirstOrDefault(pack =>
                uidIndex >= pack.StartIndex && uidIndex < pack.StartIndex + pack.Length);

            // If the pack has no albums in it return null, otherwise return pack (will be null if it doesn't exist)
            return retrievedPack?.Length is 0 ? null : retrievedPack;
        }

        internal static Pack CreatePack(ZipArchiveEntry json, string path)
        {
            if (json == null) return new Pack { Path = path, Title = System.IO.Path.GetFileNameWithoutExtension(path) };

            var pack = Json.Deserialize<Pack>(json.Open());
            if (pack == null) pack = new Pack();
            pack.Path = path;
            return pack;
        }

        internal static void AddPack(Pack pack)
        {
            if (pack == null) return;
            Packs.Add(pack);
        }

        internal static void AddOrUpdatePack(Pack pack)
        {
            if (pack == null) return;

            var existing = Packs.FirstOrDefault(item =>
                !string.IsNullOrEmpty(item.Path) &&
                !string.IsNullOrEmpty(pack.Path) &&
                string.Equals(System.IO.Path.GetFullPath(item.Path), System.IO.Path.GetFullPath(pack.Path),
                    StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                Packs.Add(pack);
                return;
            }

            existing.Title = pack.Title;
            existing.TitleColorHex = pack.TitleColorHex;
            existing.LongTextScroll = pack.LongTextScroll;
            existing.StartIndex = pack.StartIndex;
            existing.Length = pack.Length;
            existing.Albums = pack.Albums;
        }

        internal static void RemoveAlbum(Album album)
        {
            if (album == null) return;

            foreach (var pack in Packs)
            {
                var removed = pack.Albums.RemoveAll(item => item.AlbumName == album.AlbumName);
                if (removed == 0) continue;

                pack.Length = pack.Albums.Count;
                if (pack.Albums.Count > 0)
                    pack.StartIndex = pack.Albums.Min(item => item.Index);
                return;
            }
        }
    }
}
