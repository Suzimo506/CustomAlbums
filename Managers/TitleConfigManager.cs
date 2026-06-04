using System.IO;
using System.Text.Json;

namespace CustomAlbums.Managers
{
    public class TitleConfig
    {
        public string Color { get; set; } = "#00FFFF";
        public int Size { get; set; } = 20;
        public bool IsBold { get; set; } = true;
        public bool IsItalic { get; set; } = false;
    }

    public static class TitleConfigManager
    {
        private const string ConfigLocation = "UserData";
        private const string ConfigFile = "CustomAlbums_TitleConfig.json";
        public static TitleConfig Config { get; private set; } = new TitleConfig();

        public static void Load()
        {
            var path = Path.Join(ConfigLocation, ConfigFile);
            if (File.Exists(path))
            {
                try
                {
                    var json = File.ReadAllText(path);
                    Config = JsonSerializer.Deserialize<TitleConfig>(json);
                }
                catch
                {
                    // If parsing fails, overwrite with default
                    Save();
                }
            }
            else
            {
                Save();
            }
        }

        public static void Save()
        {
            var path = Path.Join(ConfigLocation, ConfigFile);
            try
            {
                if (!Directory.Exists(ConfigLocation))
                    Directory.CreateDirectory(ConfigLocation);
                
                var json = JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch
            {
                // Ignore
            }
        }
    }
}
