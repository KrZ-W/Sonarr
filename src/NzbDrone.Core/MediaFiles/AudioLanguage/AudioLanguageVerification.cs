using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.MediaFiles.AudioLanguage
{
    /// <summary>
    /// Outcome of one audio-track language probe, stored on the episode file at import time
    /// (EpisodeFile.AudioLanguageVerification, JSON list). Written once by the import pipeline;
    /// rescans and media-info refreshes never touch it. The follow-up "Audio Track Retag"
    /// feature consumes StreamIndex / DetectedLanguage / Confidence.
    /// </summary>
    public class AudioLanguageVerification : IEmbeddedDocument
    {
        public const string WhisperSource = "whisper";

        /// <summary>Audio-relative stream index (ffmpeg "0:a:N", mkvpropedit "track:aN+1").</summary>
        public int StreamIndex { get; set; }

        /// <summary>Language tag the track carried when probed (ffprobe tag, e.g. "eng", "und", null).</summary>
        public string TaggedLanguage { get; set; }

        /// <summary>ISO 639-1 code reported by the detector, null when the probe failed.</summary>
        public string DetectedLanguage { get; set; }

        public double Confidence { get; set; }

        public string Source { get; set; } = WhisperSource;

        public DateTime ProbedAt { get; set; }
    }
}
