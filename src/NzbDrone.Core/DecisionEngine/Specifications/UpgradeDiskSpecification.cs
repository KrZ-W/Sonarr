using System;  // krzw(season-pack)
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;  // krzw(season-pack)
using NzbDrone.Core.Configuration;  // krzw(season-pack)
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.History;  // krzw(season-pack)
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.MediaFiles;  // krzw(season-pack)
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;  // krzw(season-pack)
using NzbDrone.Core.Tv;  // krzw(season-pack)

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    public class UpgradeDiskSpecification : IDownloadDecisionEngineSpecification
    {
        private readonly UpgradableSpecification _upgradableSpecification;
        private readonly IConfigService _configService;  // krzw(season-pack)
        private readonly ICustomFormatCalculationService _formatService;
        private readonly IHistoryService _historyService;  // krzw(season-pack)
        private readonly Logger _logger;

        public UpgradeDiskSpecification(UpgradableSpecification upgradableSpecification,
                                        IConfigService configService,  // krzw(season-pack)
                                        ICustomFormatCalculationService formatService,
                                        IHistoryService historyService,  // krzw(season-pack)
                                        Logger logger)
        {
            _upgradableSpecification = upgradableSpecification;
            _configService = configService;  // krzw(season-pack)
            _formatService = formatService;
            _historyService = historyService;  // krzw(season-pack)
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public virtual DownloadSpecDecision IsSatisfiedBy(RemoteEpisode subject, SearchCriteriaBase searchCriteria)
        {
            var qualityProfile = subject.Series.QualityProfile.Value;

            // krzw(season-pack): whole-pack evaluation; per-file loop extracted to CheckUpgradeSpecification
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

        // krzw(season-pack)
        private DownloadSpecDecision IsSeasonPackUpgrade(RemoteEpisode subject, QualityProfile qualityProfile)
        {
            var totalEpisodesInPack = subject.Episodes.Count;

            if (totalEpisodesInPack == 0)
            {
                return DownloadSpecDecision.Accept();
            }

            var seasonPackUpgrade = _configService.SeasonPackUpgrade;
            var seasonPackUpgradeThreshold = _configService.SeasonPackUpgradeThreshold;

            // Unmonitored episodes without a file were deliberately skipped by the user, so they
            // are neither slots the pack usefully fills nor a reason to reject it - they are left
            // out of both sides of the ratio.
            var missingEpisodes = subject.Episodes.Where(c => c.EpisodeFileId == 0 && c.Monitored).ToList();

            // In Any/Threshold mode a missing episode only counts if this release can actually
            // fill it. All mode keeps the original behavior (missing episodes always count).
            var fillableEpisodesCount = seasonPackUpgrade == SeasonPackUpgradeType.All
                ? missingEpisodes.Count
                : missingEpisodes.Count(c => !PackPreviouslyImportedWithoutEpisode(c, subject));

            var upgradedCount = fillableEpisodesCount;
            _logger.Debug("{0} monitored episodes are missing from disk and fillable by this release.", fillableEpisodesCount);

            var existingEpisodeFiles = subject.Episodes.Where(c => c.EpisodeFileId != 0)
                                                       .Select(c => c.EpisodeFile.Value)
                                                       .ToList();

            var consideredCount = missingEpisodes.Count + existingEpisodeFiles.Count;

            if (consideredCount == 0)
            {
                return DownloadSpecDecision.Reject(DownloadRejectionReason.DiskNotUpgrade, "Season pack has no monitored missing episodes to fill and no existing files to upgrade");
            }

            // Every existing file that is itself a genuine upgrade also adds to the pack's value.
            foreach (var file in existingEpisodeFiles)
            {
                if (CheckUpgradeSpecification(file, qualityProfile, subject) == null)
                {
                    upgradedCount++;
                }
            }

            var upgradablePercentage = (double)upgradedCount / consideredCount * 100;

            _logger.Debug("Season pack upgradable episodes: {0}/{1} ({2:0.##}%). Mode: {3}, Threshold: {4}%", upgradedCount, consideredCount, upgradablePercentage, seasonPackUpgrade, seasonPackUpgradeThreshold);

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

            return DownloadSpecDecision.Reject(DownloadRejectionReason.DiskNotUpgrade, "Season pack does not meet the upgrade criteria. Upgradable: {0}/{1} ({2:0.##}%), Mode: {3}, Threshold: {4}%", upgradedCount, consideredCount, upgradablePercentage, seasonPackUpgrade, seasonPackUpgradeThreshold);
        }

        // krzw(season-pack)
        private bool PackPreviouslyImportedWithoutEpisode(Episode episode, RemoteEpisode subject)
        {
            // If this same release was grabbed for this episode before and that download completed
            // (other episodes were imported from it) while this episode is still missing, the pack
            // simply does not contain the episode - grabbing the same release again can never fill
            // the slot and would loop forever once the history grace period expires. A grab with no
            // imports at all is left alone so failed downloads can still be retried.
            if (subject.Release?.Title == null)
            {
                return false;
            }

            var mostRecent = _historyService.MostRecentForEpisode(episode.Id);

            if (mostRecent == null || mostRecent.EventType != EpisodeHistoryEventType.Grabbed)
            {
                return false;
            }

            if (!subject.Release.Title.Equals(mostRecent.SourceTitle, StringComparison.InvariantCultureIgnoreCase))
            {
                return false;
            }

            if (mostRecent.DownloadId.IsNullOrWhiteSpace())
            {
                return false;
            }

            var wasImported = _historyService.FindByDownloadId(mostRecent.DownloadId)
                                             .Any(h => h.EventType == EpisodeHistoryEventType.DownloadFolderImported);

            if (wasImported)
            {
                _logger.Debug("Episode [{0}] is missing but the same release was previously imported without it, not counting it as fillable.", episode.Id);
            }

            return wasImported;
        }

        // krzw(season-pack): upstream's per-file body, unchanged, now returns null for 'not rejected'
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
