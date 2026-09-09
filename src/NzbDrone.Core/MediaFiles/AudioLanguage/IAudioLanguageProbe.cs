using System;

namespace NzbDrone.Core.MediaFiles.AudioLanguage
{
    public interface IAudioLanguageProbe
    {
        /// <summary>
        /// Detects the spoken language of one audio stream. Never throws: any failure (endpoint down,
        /// timeout, ffmpeg error, unparsable reply) is logged as a warning and yields null so the
        /// import proceeds on the existing evidence.
        /// </summary>
        AudioLanguageProbeResult Probe(string path, int audioStreamIndex, TimeSpan? runtime);
    }
}
