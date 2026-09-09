using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Tv.ImdbTitles
{
    // krzw(imdb-title-provider): raised after a dataset refresh task finished (whether or not the
    // dump was re-downloaded), so the health check re-evaluates the DB age.
    public class ImdbTitleDatasetRefreshedEvent : IEvent
    {
        public bool Downloaded { get; }
        public long RowCount { get; }

        public ImdbTitleDatasetRefreshedEvent(bool downloaded, long rowCount)
        {
            Downloaded = downloaded;
            RowCount = rowCount;
        }
    }
}
