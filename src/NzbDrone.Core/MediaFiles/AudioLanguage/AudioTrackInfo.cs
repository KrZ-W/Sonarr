using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.MediaFiles.AudioLanguage
{
    /// <summary>One audio stream of a file as ffprobe reported it.</summary>
    public class AudioTrackInfo
    {
        /// <summary>Audio-relative index (0 = first audio stream), i.e. ffmpeg "0:a:N".</summary>
        public int StreamIndex { get; set; }
        public string Codec { get; set; }
        public int Channels { get; set; }

        /// <summary>Raw language tag ("eng", "fra", "und" or null when the stream has none).</summary>
        public string Language { get; set; }
        public string Title { get; set; }

        /// <summary>The Sonarr language the tag maps to, or null for und / missing / unknown tags.</summary>
        public Languages.Language TaggedLanguage
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Language))
                {
                    return null;
                }

                var language = IsoLanguages.Find(Language)?.Language;

                if (language == null || language == Languages.Language.Unknown)
                {
                    return null;
                }

                return language;
            }
        }
    }

    public static class AudioTrackLayout
    {
        /// <summary>
        /// Ordered (codec, channels, tag language, title) list rendered as one string. Files of a
        /// pack that share a signature are assumed to share track semantics, so one probe per
        /// signature per pack is enough (same rule as the reference script).
        /// </summary>
        public static string Signature(IEnumerable<AudioTrackInfo> tracks)
        {
            return string.Join("|", tracks.Select(t => $"{t.Codec}/{t.Channels}/{(t.Language ?? string.Empty).ToLowerInvariant()}/{t.Title}"));
        }
    }
}
