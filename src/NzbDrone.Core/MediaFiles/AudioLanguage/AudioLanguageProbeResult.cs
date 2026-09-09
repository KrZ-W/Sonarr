using NzbDrone.Core.Languages;

namespace NzbDrone.Core.MediaFiles.AudioLanguage
{
    public class AudioLanguageProbeResult
    {
        /// <summary>ISO 639-1 code as returned by the detector (e.g. "fr").</summary>
        public string LanguageCode { get; set; }

        /// <summary>Sonarr language for the code, null when the code is not one Sonarr knows.</summary>
        public Language Language { get; set; }

        public double Confidence { get; set; }
    }
}
