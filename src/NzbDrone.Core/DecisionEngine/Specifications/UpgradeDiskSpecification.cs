using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    public class UpgradeDiskSpecification : IDownloadDecisionEngineSpecification
    {
        private readonly UpgradableSpecification _upgradableSpecification;
        private readonly IConfigService _configService;
        private readonly ICustomFormatCalculationService _formatService;
        private readonly Logger _logger;

        public UpgradeDiskSpecification(UpgradableSpecification upgradableSpecification,
                                        IConfigService configService,
                                        ICustomFormatCalculationService formatService,
                                        Logger logger)
        {
            _upgradableSpecification = upgradableSpecification;
            _configService = configService;
            _formatService = formatService;
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public virtual DownloadSpecDecision IsSatisfiedBy(RemoteEpisode subject, SearchCriteriaBase searchCriteria)
        {
            var qualityProfile = subject.Series.QualityProfile.Value;

            // A season pack fills the whole season at once. Instead of rejecting it outright when a
            // single already-present episode isn't an upgrade, evaluate the pack as a whole: missing
            // episodes are slots it fills and existing files are weighed for upgrades. Whether a
            // partial pack is accepted is governed by the opt-in SeasonPackUpgrade setting (default
            // All, which preserves the original "every episode must be missing or upgradable" guard).
            if (subject.ParsedEpisodeInfo.FullSeason)
            {
                return IsSeasonPackUpgrade(subject, qualityProfile);
            }

            foreach (var file in subject.Episodes.Where(c => c.EpisodeFileId != 0).Select(c => c.EpisodeFile.Value))
            {
                var rejection = CheckUpgradeSpecification(file, qualityProfile, subject);

                if (rejection != null)
                {
                    return rejection;
                }
            }

            return DownloadSpecDecision.Accept();
        }

        private DownloadSpecDecision IsSeasonPackUpgrade(RemoteEpisode subject, QualityProfile qualityProfile)
        {
            var totalEpisodesInPack = subject.Episodes.Count;

            if (totalEpisodesInPack == 0)
            {
                return DownloadSpecDecision.Accept();
            }

            // Missing episodes have no file on disk to compare against, so they are always fillable.
            var missingEpisodesCount = subject.Episodes.Count(c => c.EpisodeFileId == 0);
            var upgradedCount = missingEpisodesCount;
            _logger.Debug("{0} episodes are missing from disk and are considered upgradable.", missingEpisodesCount);

            var existingEpisodeFiles = subject.Episodes.Where(c => c.EpisodeFileId != 0)
                                                       .Select(c => c.EpisodeFile.Value)
                                                       .ToList();

            // Every existing file that is itself a genuine upgrade also adds to the pack's value.
            foreach (var file in existingEpisodeFiles)
            {
                if (CheckUpgradeSpecification(file, qualityProfile, subject) == null)
                {
                    upgradedCount++;
                }
            }

            var seasonPackUpgrade = _configService.SeasonPackUpgrade;
            var seasonPackUpgradeThreshold = _configService.SeasonPackUpgradeThreshold;
            var upgradablePercentage = (double)upgradedCount / totalEpisodesInPack * 100;

            _logger.Debug("Season pack upgradable episodes: {0}/{1} ({2:0.##}%). Mode: {3}, Threshold: {4}%", upgradedCount, totalEpisodesInPack, upgradablePercentage, seasonPackUpgrade, seasonPackUpgradeThreshold);

            if (seasonPackUpgrade == SeasonPackUpgradeType.Any)
            {
                if (upgradedCount > 0)
                {
                    return DownloadSpecDecision.Accept();
                }
            }
            else
            {
                var threshold = seasonPackUpgrade == SeasonPackUpgradeType.All ? 100.0 : seasonPackUpgradeThreshold;

                if (upgradablePercentage >= threshold)
                {
                    return DownloadSpecDecision.Accept();
                }
            }

            return DownloadSpecDecision.Reject(DownloadRejectionReason.DiskNotUpgrade, "Season pack does not meet the upgrade criteria. Upgradable: {0}/{1} ({2:0.##}%), Mode: {3}, Threshold: {4}%", upgradedCount, totalEpisodesInPack, upgradablePercentage, seasonPackUpgrade, seasonPackUpgradeThreshold);
        }

        private DownloadSpecDecision CheckUpgradeSpecification(EpisodeFile file, QualityProfile qualityProfile, RemoteEpisode subject)
        {
            if (file == null)
            {
                _logger.Debug("File is no longer available, skipping this file.");
                return null;
            }

            _logger.Debug("Comparing file quality with report. Existing file is {0}.", file.Quality);

            if (!_upgradableSpecification.CutoffNotMet(qualityProfile,
                    file.Quality,
                    _formatService.ParseCustomFormat(file),
                    subject.ParsedEpisodeInfo.Quality))
            {
                _logger.Debug("Cutoff already met, rejecting.");

                var cutoff = qualityProfile.UpgradeAllowed ? qualityProfile.Cutoff : qualityProfile.FirststAllowedQuality().Id;
                var qualityCutoff = qualityProfile.Items[qualityProfile.GetIndex(cutoff).Index];

                return DownloadSpecDecision.Reject(DownloadRejectionReason.DiskCutoffMet, "Existing file meets cutoff: {0}", qualityCutoff);
            }

            var customFormats = _formatService.ParseCustomFormat(file);

            var upgradeableRejectReason = _upgradableSpecification.IsUpgradable(qualityProfile,
                file.Quality,
                customFormats,
                subject.ParsedEpisodeInfo.Quality,
                subject.CustomFormats);

            switch (upgradeableRejectReason)
            {
                case UpgradeableRejectReason.None:
                    return null;

                case UpgradeableRejectReason.BetterQuality:
                    return DownloadSpecDecision.Reject(DownloadRejectionReason.DiskHigherPreference, "Existing file on disk is of equal or higher preference: {0}", file.Quality);

                case UpgradeableRejectReason.BetterRevision:
                    return DownloadSpecDecision.Reject(DownloadRejectionReason.DiskHigherRevision, "Existing file on disk is of equal or higher revision: {0}", file.Quality.Revision);

                case UpgradeableRejectReason.QualityCutoff:
                    return DownloadSpecDecision.Reject(DownloadRejectionReason.DiskCutoffMet, "Existing file on disk meets quality cutoff: {0}", qualityProfile.Items[qualityProfile.GetIndex(qualityProfile.Cutoff).Index]);

                case UpgradeableRejectReason.CustomFormatCutoff:
                    return DownloadSpecDecision.Reject(DownloadRejectionReason.DiskCustomFormatCutoffMet, "Existing file on disk meets Custom Format cutoff: {0}", qualityProfile.CutoffFormatScore);

                case UpgradeableRejectReason.CustomFormatScore:
                    return DownloadSpecDecision.Reject(DownloadRejectionReason.DiskCustomFormatScore, "Existing file on disk has a equal or higher Custom Format score: {0}", qualityProfile.CalculateCustomFormatScore(customFormats));

                case UpgradeableRejectReason.MinCustomFormatScore:
                    return DownloadSpecDecision.Reject(DownloadRejectionReason.DiskCustomFormatScoreIncrement, "Existing file on disk has Custom Format score within Custom Format score increment: {0}", qualityProfile.MinUpgradeFormatScore);

                case UpgradeableRejectReason.UpgradesNotAllowed:
                    return DownloadSpecDecision.Reject(DownloadRejectionReason.DiskUpgradesNotAllowed, "Existing file on disk and Quality Profile '{0}' does not allow upgrades", qualityProfile.Name);
            }

            return null;
        }
    }
}
