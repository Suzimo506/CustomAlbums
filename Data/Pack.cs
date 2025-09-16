using CustomAlbums.Managers;
using CustomAlbums.Utilities;
using System.IO.Compression;

namespace CustomAlbums.Data
{
    public class Pack
    {
        public string Title { get; set; } = AlbumManager.GetCustomAlbumsTitle();
        public string TitleColorHex { get; set; } = "#ffffff";
        public bool LongTextScroll { get; set; } = false;

        public string Path { get; set; } = string.Empty;
        public List<Album> Albums { get; set; } = new();

        internal int StartIndex;
        internal int Length;

        public bool HasFile(string name)
        {
            if (string.IsNullOrEmpty(Path)) return false;

            try
            {
                using var zip = ZipFile.OpenRead(Path);
                return zip.GetEntry(name) != null;
            }
            catch
            {
                return false;
            }
        }

        public Stream OpenNullableStream(string file)
        {
            if (string.IsNullOrEmpty(Path)) return null;

            try
            {
                using var zip = ZipFile.OpenRead(Path);
                var entry = zip.GetEntry(file);

                if (entry != null)
                {
                    return entry.Open().ToMemoryStream();
                }

                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
