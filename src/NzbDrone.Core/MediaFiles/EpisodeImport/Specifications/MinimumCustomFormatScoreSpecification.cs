using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.EpisodeImport.Specifications
{
    public class MinimumCustomFormatScoreSpecification : IImportDecisionEngineSpecification
    {
        private readonly Logger _logger;

        public MinimumCustomFormatScoreSpecification(Logger logger)
        {
            _logger = logger;
        }

        public ImportSpecDecision IsSatisfiedBy(LocalEpisode localEpisode, DownloadClientItem downloadClientItem)
        {
            if (localEpisode.ExistingFile)
            {
                _logger.Debug("Existing file, skipping minimum custom format score check");
                return ImportSpecDecision.Accept();
            }

            var minScore = localEpisode.Series.QualityProfile.Value.MinFormatScore;
            var score = localEpisode.CustomFormatScore;

            if (score < minScore)
            {
                _logger.Debug("File's custom format score {0} below profile minimum {1}. CFs: [{2}]. Skipping {3}",
                    score,
                    minScore,
                    localEpisode.CustomFormats.ConcatToString(),
                    localEpisode.Path);
                return ImportSpecDecision.Reject(ImportRejectionReason.CustomFormatMinimumScore,
                    "Custom Formats [{0}] have score {1} below profile minimum {2}",
                    localEpisode.CustomFormats.ConcatToString(),
                    score,
                    minScore);
            }

            return ImportSpecDecision.Accept();
        }
    }
}
