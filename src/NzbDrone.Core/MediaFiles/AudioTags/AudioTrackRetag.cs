using System;
using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.MediaFiles.AudioTags
{
    /// <summary>
    /// Outcome of the post-import audio-track language retag, stored on the episode file
    /// (EpisodeFile.AudioTrackRetag, JSON). A file whose Result is "done" is never retagged again.
    /// </summary>
    public class AudioTrackRetag : IEmbeddedDocument
    {
        /// <summary>Hardlink mode in force when the outcome was recorded.</summary>
        public AudioTrackRetagHardlinkMode Mode { get; set; }

        /// <summary>One of <see cref="AudioTrackRetagResult"/>.</summary>
        public string Result { get; set; }

        /// <summary>The planned edits (streamIndex, from, to). Always recorded, so a skipped-hardlinked file says what a later retag would change; they were written to the file only when Result is "done".</summary>
        public List<AudioTrackRetagTrack> Tracks { get; set; } = new List<AudioTrackRetagTrack>();

        /// <summary>Mismatched tracks that were left alone, with the reason (e.g. no ISO 639-2 code).</summary>
        public List<AudioTrackRetagSkippedTrack> SkippedTracks { get; set; } = new List<AudioTrackRetagSkippedTrack>();

        public DateTime At { get; set; }

        public string Error { get; set; }

        public bool IsDone => Result == AudioTrackRetagResult.Done;
    }

    public class AudioTrackRetagTrack : IEmbeddedDocument
    {
        /// <summary>Audio-relative stream index (ffmpeg "0:a:N"; mkvpropedit "track:a{N+1}").</summary>
        public int StreamIndex { get; set; }

        /// <summary>Tag before the edit (ffprobe value, may be null/"und").</summary>
        public string From { get; set; }

        /// <summary>ISO 639-2 code written to the track.</summary>
        public string To { get; set; }
    }

    public class AudioTrackRetagSkippedTrack : IEmbeddedDocument
    {
        public int StreamIndex { get; set; }
        public string Reason { get; set; }
    }

    public static class AudioTrackRetagResult
    {
        public const string Done = "done";
        public const string SkippedHardlinked = "skipped-hardlinked";
        public const string SkippedContainer = "skipped-container";
        public const string Failed = "failed";
    }
}
