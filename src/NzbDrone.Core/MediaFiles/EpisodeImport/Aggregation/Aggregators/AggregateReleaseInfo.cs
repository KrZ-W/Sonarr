using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles.GrabbedReleaseTitles;  // krzw(grabbed-release-title)
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.EpisodeImport.Aggregation.Aggregators
{
    public class AggregateReleaseInfo : IAggregateLocalEpisode
    {
        // krzw(grabbed-release-title): must run before the Order-1 aggregators — AggregateLanguage's audio-probe
        // augmenter predicts rejection from GrabbedReleaseTitle, which only this aggregator populates. Nothing here
        // depends on another aggregator's output (only the download client item and grabbed history), so 0 is safe.
        public int Order => 0;

        private readonly IHistoryService _historyService;

        public AggregateReleaseInfo(IHistoryService historyService)
        {
            _historyService = historyService;
        }

        public LocalEpisode Aggregate(LocalEpisode localEpisode, DownloadClientItem downloadClientItem)
        {
            if (downloadClientItem == null)
            {
                return localEpisode;
            }

            var grabbedHistories = _historyService.FindByDownloadId(downloadClientItem.DownloadId)
                .Where(h => h.EventType == EpisodeHistoryEventType.Grabbed)
                .ToList();

            if (grabbedHistories.Empty())
            {
                return localEpisode;
            }

            localEpisode.Release = new GrabbedReleaseInfo(grabbedHistories);

            // krzw(grabbed-release-title): the title the grab was made under, sanitised once here so
            // every consumer (scoring, import capture) sees the same value.
            localEpisode.GrabbedReleaseTitle = GrabbedReleaseTitleSanitizer.Sanitize(localEpisode.Release.Title);

            return localEpisode;
        }
    }
}
