namespace CustomAlbums.Data
{
    public class LibraryIndexCache
    {
        public int CacheVersion { get; set; } = 1;
        public List<LibraryAlbumEntry> Albums { get; set; } = new();
    }
}
