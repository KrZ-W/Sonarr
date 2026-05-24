using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.MediaFiles.EpisodeImport.Specifications
{
    public class UpgradeSpecification : IImportDecisionEngineSpecification
    {
        private readonly IConfigService _configService;
        private readonly ICustomFormatCalculationService _formatService;
        private readonly Logger _logger;

        public UpgradeSpecification(IConfigService configService,
                                    ICustomFormatCalculationService formatService,
                                    Logger logger)
        {
            _configService = configService;
            _formatService = formatService;
            _logger = logger;
        }

        public ImportSpecDecision IsSatisfiedBy(LocalEpisode localEpisode, DownloadClientItem downloadClientItem)
        {
            var downloadPropersAndRepacks = _configService.DownloadPropersAndRepacks;
            var qualityProfile = localEpisode.Series.QualityProfile.Value;
            var qualityComparer = new QualityModelComparer(qualityProfile);

            foreach (var episode in localEpisode.Episodes.Where(e => e.EpisodeFileId > 0))
            {
                var episodeFile = episode.EpisodeFile.Value;

                if (episodeFile == null)
                {
                    _logger.Trace("Unable to get episode file details from the DB. EpisodeId: {0} EpisodeFileId: {1}", episode.Id, episode.EpisodeFileId);
                    continue;
                }

                var currentFormats = _formatService.ParseCustomFormat(episodeFile);
                var currentPriorityScore = qualityProfile.CalculatePriorityFormatScore(currentFormats);
                var newPriorityScore = qualityProfile.CalculatePriorityFormatScore(localEpisode.CustomFormats);
                var currentFormatScore = qualityProfile.CalculateCustomFormatScore(currentFormats);
                var newFormatScore = localEpisode.CustomFormatScore;
                var newFormats = localEpisode.CustomFormats;

                // Priority CFs are compared BEFORE quality, matching grab-side UpgradableSpecification.
                // A higher priority score wins even if the new file is a quality downgrade.
                if (newPriorityScore > currentPriorityScore)
                {
                    if (currentFormatScore >= qualityProfile.CutoffFormatScore)
                    {
                        _logger.Debug("Priority CF upgrade blocked at import: existing meets CF cutoff. Existing: [{0}] ({1}). Cutoff: {2}",
                            currentFormats.ConcatToString(),
                            currentFormatScore,
                            qualityProfile.CutoffFormatScore);
                        return ImportSpecDecision.Reject(ImportRejectionReason.NotCustomFormatUpgrade,
                            "Existing episode file meets custom format cutoff. Existing: [{0}] ({1}). Cutoff: {2}",
                            currentFormats.ConcatToString(),
                            currentFormatScore,
                            qualityProfile.CutoffFormatScore);
                    }

                    if (newFormatScore < currentFormatScore + qualityProfile.MinUpgradeFormatScore)
                    {
                        _logger.Debug("Priority CF upgrade blocked at import: score increment {0} < minimum {1}. New: [{2}] ({3}). Existing: [{4}] ({5}).",
                            newFormatScore - currentFormatScore,
                            qualityProfile.MinUpgradeFormatScore,
                            newFormats.ConcatToString(),
                            newFormatScore,
                            currentFormats.ConcatToString(),
                            currentFormatScore);
                        return ImportSpecDecision.Reject(ImportRejectionReason.NotCustomFormatUpgrade,
                            "Custom format score increment {0} below minimum {1}. New: [{2}] ({3}). Existing: [{4}] ({5}).",
                            newFormatScore - currentFormatScore,
                            qualityProfile.MinUpgradeFormatScore,
                            newFormats.ConcatToString(),
                            newFormatScore,
                            currentFormats.ConcatToString(),
                            currentFormatScore);
                    }

                    _logger.Debug("Priority CF upgrade at import: [{0}] ({1}) > [{2}] ({3}), accepting regardless of quality",
                        newFormats.ConcatToString(),
                        newPriorityScore,
                        currentFormats.ConcatToString(),
                        currentPriorityScore);
                    continue;
                }

                if (newPriorityScore < currentPriorityScore)
                {
                    _logger.Debug("Priority CF downgrade at import: [{0}] ({1}) < [{2}] ({3}), rejecting regardless of quality",
                        newFormats.ConcatToString(),
                        newPriorityScore,
                        currentFormats.ConcatToString(),
                        currentPriorityScore);
                    return ImportSpecDecision.Reject(ImportRejectionReason.NotCustomFormatUpgrade,
                        "Priority custom format downgrade. Existing: [{0}] ({1}). New: [{2}] ({3}).",
                        currentFormats.ConcatToString(),
                        currentPriorityScore,
                        newFormats.ConcatToString(),
                        newPriorityScore);
                }

                // Priority scores equal — fall through to standard quality/revision/CF checks.
                var qualityCompare = qualityComparer.Compare(localEpisode.Quality.Quality, episodeFile.Quality.Quality);

                if (qualityCompare < 0)
                {
                    _logger.Debug("This file isn't a quality upgrade for all episodes. Existing quality: {0}. New Quality {1}. Skipping {2}", episodeFile.Quality.Quality, localEpisode.Quality.Quality, localEpisode.Path);
                    return ImportSpecDecision.Reject(ImportRejectionReason.NotQualityUpgrade, "Not an upgrade for existing episode file(s). Existing quality: {0}. New Quality {1}.", episodeFile.Quality.Quality, localEpisode.Quality.Quality);
                }

                // Same quality, propers/repacks are preferred and it is not a revision update. Reject revision downgrade.

                if (qualityCompare == 0 &&
                    downloadPropersAndRepacks != ProperDownloadTypes.DoNotPrefer &&
                    localEpisode.Quality.Revision.CompareTo(episodeFile.Quality.Revision) < 0)
                {
                    _logger.Debug("This file isn't a quality revision upgrade for all episodes. Skipping {0}", localEpisode.Path);
                    return ImportSpecDecision.Reject(ImportRejectionReason.NotRevisionUpgrade, "Not a quality revision upgrade for existing episode file(s)");
                }

                if (qualityCompare == 0 && newFormatScore < currentFormatScore)
                {
                    _logger.Debug("New item's custom formats [{0}] ({1}) do not improve on [{2}] ({3}), skipping",
                        newFormats != null ? newFormats.ConcatToString() : "",
                        newFormatScore,
                        currentFormats != null ? currentFormats.ConcatToString() : "",
                        currentFormatScore);

                    return ImportSpecDecision.Reject(ImportRejectionReason.NotCustomFormatUpgrade,
                        "Not a Custom Format upgrade for existing episode file(s). New: [{0}] ({1}) do not improve on Existing: [{2}] ({3})",
                        newFormats != null ? newFormats.ConcatToString() : "",
                        newFormatScore,
                        currentFormats != null ? currentFormats.ConcatToString() : "",
                        currentFormatScore);
                }

                _logger.Debug("New item's custom formats [{0}] ({1}) improve on [{2}] ({3}), accepting",
                    newFormats != null ? newFormats.ConcatToString() : "",
                    newFormatScore,
                    currentFormats != null ? currentFormats.ConcatToString() : "",
                    currentFormatScore);
            }

            return ImportSpecDecision.Accept();
        }
    }
}
