using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Specifications.Search
{
    public class SingleEpisodeSearchMatchSpecification : IDownloadDecisionEngineSpecification
    {
        private readonly Logger _logger;
        private readonly ISceneMappingService _sceneMappingService;
        private readonly IConfigService _configService;

        public SingleEpisodeSearchMatchSpecification(ISceneMappingService sceneMappingService, IConfigService configService, Logger logger)
        {
            _logger = logger;
            _sceneMappingService = sceneMappingService;
            _configService = configService;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public DownloadSpecDecision IsSatisfiedBy(RemoteEpisode remoteEpisode, SearchCriteriaBase searchCriteria)
        {
            if (searchCriteria == null)
            {
                return DownloadSpecDecision.Accept();
            }

            var singleEpisodeSpec = searchCriteria as SingleEpisodeSearchCriteria;
            if (singleEpisodeSpec != null)
            {
                return IsSatisfiedBy(remoteEpisode, singleEpisodeSpec);
            }

            var animeEpisodeSpec = searchCriteria as AnimeEpisodeSearchCriteria;
            if (animeEpisodeSpec != null)
            {
                return IsSatisfiedBy(remoteEpisode, animeEpisodeSpec);
            }

            return DownloadSpecDecision.Accept();
        }

        private DownloadSpecDecision IsSatisfiedBy(RemoteEpisode remoteEpisode, SingleEpisodeSearchCriteria singleEpisodeSpec)
        {
            if (singleEpisodeSpec.SeasonNumber != remoteEpisode.ParsedEpisodeInfo.SeasonNumber)
            {
                _logger.Debug("Season number does not match searched season number, skipping.");
                return DownloadSpecDecision.Reject(DownloadRejectionReason.WrongSeason, "Wrong season");
            }

            if (!remoteEpisode.ParsedEpisodeInfo.EpisodeNumbers.Any())
            {
                if (_configService.SeasonPackUpgrade != SeasonPackUpgradeType.All)
                {
                    _logger.Debug("Full season result during single episode search, but season pack upgrades are enabled; allowing for upgrade evaluation.");
                    return DownloadSpecDecision.Accept();
                }

                _logger.Debug("Full season result during single episode search, skipping.");
                return DownloadSpecDecision.Reject(DownloadRejectionReason.FullSeason, "Full season pack");
            }

            if (!remoteEpisode.ParsedEpisodeInfo.EpisodeNumbers.Contains(singleEpisodeSpec.EpisodeNumber))
            {
                _logger.Debug("Episode number does not match searched episode number, skipping.");
                return DownloadSpecDecision.Reject(DownloadRejectionReason.WrongEpisode, "Wrong episode");
            }

            return DownloadSpecDecision.Accept();
        }

        private DownloadSpecDecision IsSatisfiedBy(RemoteEpisode remoteEpisode, AnimeEpisodeSearchCriteria animeEpisodeSpec)
        {
            if (remoteEpisode.ParsedEpisodeInfo.FullSeason && !animeEpisodeSpec.IsSeasonSearch)
            {
                if (_configService.SeasonPackUpgrade != SeasonPackUpgradeType.All)
                {
                    _logger.Debug("Full season result during single episode search, but season pack upgrades are enabled; allowing for upgrade evaluation.");
                    return DownloadSpecDecision.Accept();
                }

                _logger.Debug("Full season result during single episode search, skipping.");
                return DownloadSpecDecision.Reject(DownloadRejectionReason.FullSeason, "Full season pack");
            }

            return DownloadSpecDecision.Accept();
        }
    }
}
