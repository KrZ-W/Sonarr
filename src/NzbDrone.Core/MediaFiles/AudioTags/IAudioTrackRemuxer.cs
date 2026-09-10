using System;
using System.Collections.Generic;

namespace NzbDrone.Core.MediaFiles.AudioTags
{
    public interface IAudioTrackRemuxer
    {
        /// <summary>
        /// Stream-copies <paramref name="sourcePath"/> into a new Matroska file at <paramref name="targetPath"/>
        /// with the language of the given audio tracks set in the same pass. Never re-encodes. Returns which
        /// streams were carried over and which ffmpeg cannot put in Matroska (dropped). Throws on failure with
        /// the ffmpeg stderr tail; the caller owns the target file's cleanup.
        /// </summary>
        AudioTrackRemuxStreamMap Remux(string sourcePath, string targetPath, string sourceRawStreamData, IReadOnlyList<AudioTrackRetagTrack> edits, TimeSpan timeout);
    }

    /// <summary>Which source streams a remux carries into the Matroska output, by absolute ffprobe stream index.</summary>
    public class AudioTrackRemuxStreamMap
    {
        /// <summary>Absolute indexes of the streams mapped into the output, in source order (the output has exactly this many streams).</summary>
        public List<int> Kept { get; set; } = new List<int>();

        /// <summary>Streams left out because Matroska cannot carry them, as "#index type codec".</summary>
        public List<string> Dropped { get; set; } = new List<string>();

        /// <summary>True when the source layout was unknown and every stream is mapped blindly (-map 0 -ignore_unknown).</summary>
        public bool MapAll { get; set; }
    }
}
