using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MediaFiles.GrabbedReleaseTitles
{
    // krzw(grabbed-release-title)

    /// <summary>
    /// Manual one-shot backfill: POST /api/v3/command {"name":"BackfillGrabbedReleaseTitles"}.
    /// Fills GrabbedReleaseTitle on episode files imported before the feature existed, from the
    /// grab history behind each file's import event. Idempotent and safe to re-run; never scheduled.
    /// </summary>
    public class BackfillGrabbedReleaseTitlesCommand : Command
    {
        public override bool SendUpdatesToClient => true;

        public override bool IsLongRunning => true;
    }
}
