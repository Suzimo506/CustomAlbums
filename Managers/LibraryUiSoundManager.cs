using System.Reflection;
using CustomAlbums.Utilities;
using Il2CppAssets.Scripts.Database;
using Il2CppPeroPeroGames.GlobalDefines;
using NAudio.Vorbis;
using UnityEngine;
using Logger = CustomAlbums.Utilities.Logger;

namespace CustomAlbums.Managers
{
    internal enum LibraryUiSound
    {
        Yes,
        Cancel,
        Click
    }

    internal static class LibraryUiSoundManager
    {
        private const float DefaultVolumeScale = 2.4f;
        private const float SampleGain = 2.2f;
        private const string ResourcePrefix = "CustomAlbums.Assets.";

        private static readonly Dictionary<LibraryUiSound, AudioClip> Clips = new();
        private static readonly HashSet<LibraryUiSound> MissingLogged = new();
        private static readonly Logger Logger = new(nameof(LibraryUiSoundManager));
        private static AudioSource _source;

        public static void Play(LibraryUiSound sound, float volumeScale = DefaultVolumeScale)
        {
            var clip = GetClip(sound);
            if (clip == null) return;

            EnsureSource();
            if (_source == null) return;

            _source.PlayOneShot(clip, volumeScale * GetGameSfxVolume());
        }

        internal static bool IsSource(AudioSource source)
        {
            return source != null && source == _source;
        }

        private static AudioClip GetClip(LibraryUiSound sound)
        {
            if (Clips.TryGetValue(sound, out var clip)) return clip;

            clip = LoadClip(sound);
            if (clip != null)
                Clips[sound] = clip;
            else if (MissingLogged.Add(sound))
                Logger.Warning($"UI sound asset not found: {sound}");

            return clip;
        }

        private static AudioClip LoadClip(LibraryUiSound sound)
        {
            var assembly = typeof(LibraryUiSoundManager).Assembly;
            var resourceNames = assembly.GetManifestResourceNames();

            foreach (var baseName in GetBaseNames(sound))
            {
                var resourceName = resourceNames.FirstOrDefault(name =>
                    name.StartsWith(ResourcePrefix, StringComparison.OrdinalIgnoreCase) &&
                    name.Substring(ResourcePrefix.Length).Equals(baseName + ".ogg", StringComparison.OrdinalIgnoreCase));
                if (resourceName == null) continue;

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null) continue;

                try
                {
                    return LoadOgg(stream, baseName);
                }
                catch (Exception e)
                {
                    Logger.Warning($"Failed to load UI sound {resourceName}: {e.Message}");
                }
            }

            return null;
        }

        private static AudioClip LoadOgg(Stream stream, string name)
        {
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            memory.Position = 0;

            using var ogg = new VorbisWaveReader(memory);
            var channels = ogg.WaveFormat.Channels;
            var sampleRate = ogg.WaveFormat.SampleRate;
            var sampleCount = (int)(ogg.Length / (ogg.WaveFormat.BitsPerSample / 8));
            var samples = new float[sampleCount];
            var totalRead = 0;

            while (totalRead < samples.Length)
            {
                var read = ogg.Read(samples, totalRead, samples.Length - totalRead);
                if (read <= 0) break;
                totalRead += read;
            }

            for (var i = 0; i < totalRead; i++)
                samples[i] = Mathf.Clamp(samples[i] * SampleGain, -1f, 1f);

            if (totalRead != samples.Length)
                Array.Resize(ref samples, totalRead);

            var clip = AudioClip.Create("CustomAlbums_" + name, samples.Length / channels, channels, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static IEnumerable<string> GetBaseNames(LibraryUiSound sound)
        {
            return sound switch
            {
                LibraryUiSound.Yes => new[] { "Yes", "yes" },
                LibraryUiSound.Cancel => new[] { "Cancle", "cancle", "Cancel", "cancel" },
                LibraryUiSound.Click => new[] { "Click", "click" },
                _ => Array.Empty<string>()
            };
        }

        private static void EnsureSource()
        {
            if (_source != null) return;

            var obj = new GameObject("CustomAlbumsLibraryUiAudio");
            UnityEngine.Object.DontDestroyOnLoad(obj);
            _source = obj.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false;
            _source.spatialBlend = 0f;
            _source.volume = 1f;
        }

        private static float GetGameSfxVolume()
        {
            try
            {
                return Mathf.Clamp01(DataHelper.GetVolume(PeroAudioType.Sfx));
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to read game SFX volume: {ex.Message}");
                return 1f;
            }
        }
    }
}
