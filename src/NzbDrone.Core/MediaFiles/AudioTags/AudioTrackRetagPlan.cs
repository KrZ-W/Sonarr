using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.MediaFiles.AudioTags
{
    /// <summary>
    /// What the planner decided for one file: either a reason not to touch it, or the list
    /// of track edits (plus the mismatched tracks that cannot be edited and why).
    /// </summary>
    public class AudioTrackRetagPlan
    {
        public AudioTrackRetagSkipReason SkipReason { get; set; }
        public List<AudioTrackRetagTrack> Edits { get; set; } = new List<AudioTrackRetagTrack>();
        public List<AudioTrackRetagSkippedTrack> SkippedTracks { get; set; } = new List<AudioTrackRetagSkippedTrack>();

        public bool ShouldRetag => SkipReason == AudioTrackRetagSkipReason.None && Edits.Any();

        public static AudioTrackRetagPlan Skip(AudioTrackRetagSkipReason reason)
        {
            return new AudioTrackRetagPlan { SkipReason = reason };
        }
    }

    public enum AudioTrackRetagSkipReason
    {
        None = 0,
        Disabled,
        NotNewDownload,
        AlreadyDone,
        NotMkv,
        NoVerificationRecord,
        NoMismatch
    }
}
