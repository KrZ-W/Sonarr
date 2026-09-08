using NzbDrone.Core.Configuration;  // krzw(indexer-cooldown)
using NzbDrone.Core.Indexers;

namespace NzbDrone.Core.Housekeeping.Housekeepers
{
    public class FixFutureIndexerStatusTimes : FixFutureProviderStatusTimes<IndexerStatus>, IHousekeepingTask
    {
        private readonly IConfigService _configService;  // krzw(indexer-cooldown)

        // krzw(indexer-cooldown): ctor gains IConfigService
        public FixFutureIndexerStatusTimes(IIndexerStatusRepository indexerStatusRepository, IConfigService configService)
            : base(indexerStatusRepository)
        {
            _configService = configService;
        }

        // krzw(indexer-cooldown): bound with the configured schedule, not the default table
        protected override int[] GetEscalationPeriods()
        {
            return IndexerCooldownPeriods.ToSecondsTable(_configService.IndexerCooldownPeriods);
        }
    }
}
