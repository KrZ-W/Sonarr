using System;

namespace NzbDrone.Core.MediaFiles.AudioLanguage
{
    public interface IAudioClipExtractor
    {
        /// <summary>
        /// Extracts <paramref name="length"/> of one audio stream starting at <paramref name="offset"/>
        /// as 16 kHz mono PCM WAV and returns its bytes. The temp file is always deleted.
        /// Returns null on any failure (missing ffmpeg, non-zero exit, timeout).
        /// </summary>
        byte[] Extract(string path, int audioStreamIndex, TimeSpan offset, TimeSpan length, TimeSpan timeout);
    }
}
