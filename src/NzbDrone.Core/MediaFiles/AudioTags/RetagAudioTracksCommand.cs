using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MediaFiles.AudioTags
{
    /// <summary>
    /// Manual one-file retag: POST /api/v3/command {"name":"RetagAudioTracks","episodeFileId":N}.
    /// Same rules as the post-import trigger (container, threshold, hardlink mode, idempotence),
    /// for files imported before the feature existed or skipped by the hardlink mode at the time.
    /// </summary>
    public class RetagAudioTracksCommand : Command
    {
        public int EpisodeFileId { get; set; }

        public override bool SendUpdatesToClient => true;

        public RetagAudioTracksCommand()
        {
        }

        public RetagAudioTracksCommand(int episodeFileId)
        {
            EpisodeFileId = episodeFileId;
        }
    }
}
