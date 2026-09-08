using System.Linq;  // krzw(indexer-cooldown)
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;  // krzw(indexer-cooldown)
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.ThingiProvider.Status;

namespace NzbDrone.Core.Indexers
{
    public interface IIndexerStatusService : IProviderStatusServiceBase<IndexerStatus>
    {
        ReleaseInfo GetLastRssSyncReleaseInfo(int indexerId);

        void UpdateRssSyncStatus(int indexerId, ReleaseInfo releaseInfo);
    }

    public class IndexerStatusService : ProviderStatusServiceBase<IIndexer, IndexerStatus>, IIndexerStatusService
    {
        private readonly IConfigService _configService;  // krzw(indexer-cooldown)

        // krzw(indexer-cooldown): ctor gains IConfigService
        public IndexerStatusService(IIndexerStatusRepository providerStatusRepository, IEventAggregator eventAggregator, IRuntimeInfo runtimeInfo, IConfigService configService, Logger logger)
            : base(providerStatusRepository, eventAggregator, runtimeInfo, logger)
        {
            _configService = configService;
        }

        // krzw(indexer-cooldown): IndexerCooldownPeriods CSV (minutes) -> seconds table
        protected override int[] GetEscalationPeriods()
        {
            var configured = _configService.IndexerCooldownPeriods;
            if (string.IsNullOrWhiteSpace(configured))
            {
                return EscalationBackOff.Periods;
            }

            try
            {
                var parts = configured.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0)
                    .Select(s => int.Parse(s) * 60) // input is minutes, internal is seconds
                    .ToList();

                if (parts.Count == 0)
                {
                    return EscalationBackOff.Periods;
                }

                // Ensure first level is 0 (healthy = no cooldown)
                if (parts[0] != 0)
                {
                    parts.Insert(0, 0);
                }

                return parts.ToArray();
            }
            catch
            {
                return EscalationBackOff.Periods;
            }
        }

        public ReleaseInfo GetLastRssSyncReleaseInfo(int indexerId)
        {
            return GetProviderStatus(indexerId).LastRssSyncReleaseInfo;
        }

        public void UpdateRssSyncStatus(int indexerId, ReleaseInfo releaseInfo)
        {
            lock (_syncRoot)
            {
                var status = GetProviderStatus(indexerId);

                status.LastRssSyncReleaseInfo = releaseInfo;

                _providerStatusRepository.Upsert(status);
            }
        }
    }
}
