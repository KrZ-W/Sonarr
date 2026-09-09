using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Tv.ImdbTitles
{
    // krzw(imdb-title-provider): scheduled + manual (POST /api/v3/command {"name":"ImdbTitleDatasetRefresh"}).
    public class ImdbTitleDatasetRefreshCommand : Command
    {
        public override bool SendUpdatesToClient => true;

        public override bool UpdateScheduledTask => true;

        public override bool IsLongRunning => true;

        public override string CompletionMessage => "Completed";
    }
}
