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

            // If it is a directory, check if the file exists directly on disk
            if (System.IO.Directory.Exists(Path))
            {
                return System.IO.File.Exists(System.IO.Path.Combine(Path, name));
            }

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

            // If it is a directory, read the file stream from disk
            if (System.IO.Directory.Exists(Path))
            {
                var filePath = System.IO.Path.Combine(Path, file);
                if (System.IO.File.Exists(filePath))
                {
                    return System.IO.File.OpenRead(filePath).ToMemoryStream();
                }
                return null;
            }

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
