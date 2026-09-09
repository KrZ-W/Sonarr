using System;
using System.Collections.Generic;

namespace NzbDrone.Core.MediaFiles.AudioLanguage
{
    public interface IAudioLanguageProbeCache
    {
        /// <summary>True (and the cached result, possibly null for a failed probe) when this track of this layout was already probed for the pack.</summary>
        bool TryGet(string packKey, string layoutSignature, int audioStreamIndex, out AudioLanguageProbeResult result);
        void Set(string packKey, string layoutSignature, int audioStreamIndex, AudioLanguageProbeResult result);
        void Clear();
    }

    /// <summary>
    /// One probe per distinct track layout per pack: every file of a season pack or multi-file
    /// download that shares (codec, channels, tag, title) per track reuses the first file's answer.
    /// Failed probes are cached too, so a down detector costs one attempt per layout, not per file.
    /// Entries expire so a re-import hours later probes again.
    /// </summary>
    public class AudioLanguageProbeCache : IAudioLanguageProbeCache
    {
        private static readonly TimeSpan Ttl = TimeSpan.FromHours(12);

        private readonly object _lock = new object();
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();

        public bool TryGet(string packKey, string layoutSignature, int audioStreamIndex, out AudioLanguageProbeResult result)
        {
            lock (_lock)
            {
                if (_entries.TryGetValue(Key(packKey, layoutSignature, audioStreamIndex), out var entry) && entry.Expires > DateTime.UtcNow)
                {
                    result = entry.Result;
                    return true;
                }
            }

            result = null;
            return false;
        }

        public void Set(string packKey, string layoutSignature, int audioStreamIndex, AudioLanguageProbeResult result)
        {
            lock (_lock)
            {
                Prune();
                _entries[Key(packKey, layoutSignature, audioStreamIndex)] = new Entry { Result = result, Expires = DateTime.UtcNow + Ttl };
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
            }
        }

        private void Prune()
        {
            var now = DateTime.UtcNow;
            var expired = new List<string>();

            foreach (var pair in _entries)
            {
                if (pair.Value.Expires <= now)
                {
                    expired.Add(pair.Key);
                }
            }

            foreach (var key in expired)
            {
                _entries.Remove(key);
            }
        }

        private static string Key(string packKey, string layoutSignature, int audioStreamIndex)
        {
            return $"{packKey}\n{layoutSignature}\n{audioStreamIndex}";
        }

        private class Entry
        {
            public AudioLanguageProbeResult Result { get; set; }
            public DateTime Expires { get; set; }
        }
    }
}
