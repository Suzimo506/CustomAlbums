using System.Text.Json.Serialization;

namespace CustomAlbums.Data
{
    public class LibraryAlbumEntry
    {
        public string RelativePath { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DateTime LastWriteTimeUtc { get; set; }
        public string ActiveFileName { get; set; } = string.Empty;
        public AlbumInfo Info { get; set; } = new();
        public bool HasPng { get; set; }
        public bool HasGif { get; set; }

        [JsonIgnore]
        public bool IsActive => !string.IsNullOrEmpty(ActiveFileName) &&
                                File.Exists(Path.Combine(Managers.AlbumManager.SearchPath, ActiveFileName));
    }
}
