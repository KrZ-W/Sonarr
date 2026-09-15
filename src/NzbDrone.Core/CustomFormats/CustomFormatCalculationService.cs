using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.Configuration;  // krzw(grabbed-release-title)
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;  // krzw(grabbed-release-title)
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.CustomFormats
{
    public interface ICustomFormatCalculationService
    {
        List<CustomFormat> ParseCustomFormat(RemoteEpisode remoteEpisode, long size);
        List<CustomFormat> ParseCustomFormat(EpisodeFile episodeFile, Series series);
        List<CustomFormat> ParseCustomFormat(EpisodeFile episodeFile);
        List<CustomFormat> ParseCustomFormat(Blocklist blocklist, Series series);
        List<CustomFormat> ParseCustomFormat(EpisodeHistory history, Series series);
        List<CustomFormat> ParseCustomFormat(LocalEpisode localEpisode);

        // krzw(grabbed-release-title): scoring-only ladder; naming keeps using the ParseCustomFormat overloads above
        List<CustomFormat> ParseCustomFormatForScoring(EpisodeFile episodeFile, Series series);
        List<CustomFormat> ParseCustomFormatForScoring(EpisodeFile episodeFile);
        List<CustomFormat> ParseCustomFormatForScoring(LocalEpisode localEpisode);
    }

    public class CustomFormatCalculationService : ICustomFormatCalculationService
    {
        private readonly ICustomFormatService _formatService;
        private readonly IConfigService _configService;  // krzw(grabbed-release-title)
        private readonly Logger _logger;

        public CustomFormatCalculationService(ICustomFormatService formatService,
                                              IConfigService configService,  // krzw(grabbed-release-title)
                                              Logger logger)
        {
            _formatService = formatService;
            _configService = configService;  // krzw(grabbed-release-title)
            _logger = logger;
        }

        public List<CustomFormat> ParseCustomFormat(RemoteEpisode remoteEpisode, long size)
        {
            var input = new CustomFormatInput
            {
                EpisodeInfo = remoteEpisode.ParsedEpisodeInfo,
                Series = remoteEpisode.Series,
                Size = size,
                Languages = remoteEpisode.Languages,
                IndexerFlags = remoteEpisode.Release?.IndexerFlags ?? 0,
                ReleaseType = remoteEpisode.ParsedEpisodeInfo.ReleaseType
            };

            return ParseCustomFormat(input);
        }

        public List<CustomFormat> ParseCustomFormat(EpisodeFile episodeFile, Series series)
        {
            return ParseCustomFormat(episodeFile, series, _formatService.All());
        }

        public List<CustomFormat> ParseCustomFormat(EpisodeFile episodeFile)
        {
            return ParseCustomFormat(episodeFile, episodeFile.Series.Value, _formatService.All());
        }

        public List<CustomFormat> ParseCustomFormat(Blocklist blocklist, Series series)
        {
            var parsed = Parser.Parser.ParseTitle(blocklist.SourceTitle);

            var episodeInfo = new ParsedEpisodeInfo
            {
                SeriesTitle = series.Title,
                ReleaseTitle = parsed?.ReleaseTitle ?? blocklist.SourceTitle,
                Quality = blocklist.Quality,
                Languages = blocklist.Languages,
                ReleaseGroup = parsed?.ReleaseGroup
            };

            var input = new CustomFormatInput
            {
                EpisodeInfo = episodeInfo,
                Series = series,
                Size = blocklist.Size ?? 0,
                Languages = blocklist.Languages,
                IndexerFlags = blocklist.IndexerFlags,
                ReleaseType = blocklist.ReleaseType
            };

            return ParseCustomFormat(input);
        }

        public List<CustomFormat> ParseCustomFormat(EpisodeHistory history, Series series)
        {
            var parsed = Parser.Parser.ParseTitle(history.SourceTitle);

            long.TryParse(history.Data.GetValueOrDefault("size"), out var size);
            Enum.TryParse(history.Data.GetValueOrDefault("indexerFlags"), true, out IndexerFlags indexerFlags);
            Enum.TryParse(history.Data.GetValueOrDefault("releaseType"), out ReleaseType releaseType);

            var episodeInfo = new ParsedEpisodeInfo
            {
                SeriesTitle = series.Title,
                ReleaseTitle = parsed?.ReleaseTitle ?? history.SourceTitle,
                Quality = history.Quality,
                Languages = history.Languages,
                ReleaseGroup = parsed?.ReleaseGroup,
            };

            var input = new CustomFormatInput
            {
                EpisodeInfo = episodeInfo,
                Series = series,
                Size = size,
                Languages = history.Languages,
                IndexerFlags = indexerFlags,
                ReleaseType = releaseType
            };

            return ParseCustomFormat(input);
        }

        public List<CustomFormat> ParseCustomFormat(LocalEpisode localEpisode)
        {
            // krzw(grabbed-release-title): body extracted to BuildInput, behaviour unchanged
            var releaseTitle = localEpisode.SceneName.IsNotNullOrWhiteSpace() ? localEpisode.SceneName : Path.GetFileName(localEpisode.Path);

            return ParseCustomFormat(BuildInput(localEpisode, releaseTitle));
        }

        private List<CustomFormat> ParseCustomFormat(CustomFormatInput input)
        {
            return ParseCustomFormat(input, _formatService.All());
        }

        private static List<CustomFormat> ParseCustomFormat(CustomFormatInput input, List<CustomFormat> allCustomFormats)
        {
            var matches = new List<CustomFormat>();

            foreach (var customFormat in allCustomFormats)
            {
                var specificationMatches = customFormat.Specifications
                    .GroupBy(t => t.GetType())
                    .Select(g => new SpecificationMatchesGroup
                    {
                        Matches = g.ToDictionary(t => t, t => t.IsSatisfiedBy(input))
                    })
                    .ToList();

                if (specificationMatches.All(x => x.DidMatch))
                {
                    matches.Add(customFormat);
                }
            }

            return matches.OrderBy(x => x.Name).ToList();
        }

        private List<CustomFormat> ParseCustomFormat(EpisodeFile episodeFile, Series series, List<CustomFormat> allCustomFormats)
        {
            return ParseCustomFormat(BuildInput(episodeFile, series, GetLadderReleaseTitle(episodeFile)), allCustomFormats);
        }

        // krzw(grabbed-release-title): upstream's release-title ladder, extracted verbatim so the
        // scoring pass can reuse it as the incumbent candidate.
        private string GetLadderReleaseTitle(EpisodeFile episodeFile)
        {
            if (episodeFile.SceneName.IsNotNullOrWhiteSpace())
            {
                _logger.Trace("Using scene name for release title: {0}", episodeFile.SceneName);
                return episodeFile.SceneName;
            }

            if (episodeFile.OriginalFilePath.IsNotNullOrWhiteSpace())
            {
                _logger.Trace("Using original file path for release title: {0}", Path.GetFileName(episodeFile.OriginalFilePath));
                return Path.GetFileName(episodeFile.OriginalFilePath);
            }

            if (episodeFile.RelativePath.IsNotNullOrWhiteSpace())
            {
                _logger.Trace("Using relative path for release title: {0}", Path.GetFileName(episodeFile.RelativePath));
                return Path.GetFileName(episodeFile.RelativePath);
            }

            return string.Empty;
        }

        // krzw(grabbed-release-title): everything except the release title is frozen across candidates
        private static CustomFormatInput BuildInput(EpisodeFile episodeFile, Series series, string releaseTitle)
        {
            var episodeInfo = new ParsedEpisodeInfo
            {
                SeriesTitle = series.Title,
                ReleaseTitle = releaseTitle,
                Quality = episodeFile.Quality,
                Languages = episodeFile.Languages,
                ReleaseGroup = episodeFile.ReleaseGroup,
            };

            return new CustomFormatInput
            {
                EpisodeInfo = episodeInfo,
                Series = series,
                Size = episodeFile.Size,
                Languages = episodeFile.Languages,
                AudioTitles = episodeFile.MediaInfo?.AudioTitles,  // krzw(audio-title)
                IndexerFlags = episodeFile.IndexerFlags,
                ReleaseType = episodeFile.ReleaseType,
                Filename = Path.GetFileName(episodeFile.RelativePath),
            };
        }

        // krzw(grabbed-release-title): everything except the release title is frozen across candidates
        private static CustomFormatInput BuildInput(LocalEpisode localEpisode, string releaseTitle)
        {
            var episodeInfo = new ParsedEpisodeInfo
            {
                SeriesTitle = localEpisode.Series.Title,
                ReleaseTitle = releaseTitle,
                Quality = localEpisode.Quality,
                Languages = localEpisode.Languages,
                ReleaseGroup = localEpisode.ReleaseGroup
            };

            return new CustomFormatInput
            {
                EpisodeInfo = episodeInfo,
                Series = localEpisode.Series,
                Size = localEpisode.Size,
                Languages = localEpisode.Languages,
                AudioTitles = localEpisode.MediaInfo?.AudioTitles,  // krzw(audio-title)
                IndexerFlags = localEpisode.IndexerFlags,
                ReleaseType = localEpisode.ReleaseType,
                Filename = Path.GetFileName(localEpisode.Path)
            };
        }

        // krzw(grabbed-release-title)
        // Pareto selection over the release titles this file could be scored under. The incumbent is
        // whatever the legacy ladder picks; a candidate is only eligible when it lowers NEITHER the
        // total score NOR the priority score (the fork's Priority Mode compares the priority score
        // before quality, so a plain max-by-total could weaken the file on the axis that is read
        // first and trigger the very re-grab this feature exists to prevent). The incumbent is always
        // eligible, so neither score can ever decrease and ties keep the incumbent.
        //
        // RelativePath is deliberately not a candidate: ReleaseTitleSpecification already ORs
        // CustomFormatInput.Filename, which is the relative path's file name, for every candidate.
        private List<CustomFormat> SelectBestScoringFormats(QualityProfile qualityProfile,
                                                            List<CustomFormat> incumbentFormats,
                                                            IEnumerable<string> candidateTitles,
                                                            Func<string, List<CustomFormat>> parse)
        {
            var incumbentTotal = qualityProfile.CalculateCustomFormatScore(incumbentFormats);
            var incumbentPriority = qualityProfile.CalculatePriorityFormatScore(incumbentFormats);

            var bestFormats = incumbentFormats;
            var bestTotal = incumbentTotal;
            var bestPriority = incumbentPriority;
            var bestTitle = (string)null;

            foreach (var candidateTitle in candidateTitles)
            {
                var candidateFormats = parse(candidateTitle);
                var candidateTotal = qualityProfile.CalculateCustomFormatScore(candidateFormats);
                var candidatePriority = qualityProfile.CalculatePriorityFormatScore(candidateFormats);

                if (candidateTotal < incumbentTotal || candidatePriority < incumbentPriority)
                {
                    _logger.Trace("Release title '{0}' is not eligible for scoring: ({1}, {2}) would lower ({3}, {4})",
                        candidateTitle,
                        candidateTotal,
                        candidatePriority,
                        incumbentTotal,
                        incumbentPriority);

                    continue;
                }

                if (candidateTotal > bestTotal || (candidateTotal == bestTotal && candidatePriority > bestPriority))
                {
                    bestFormats = candidateFormats;
                    bestTotal = candidateTotal;
                    bestPriority = candidatePriority;
                    bestTitle = candidateTitle;
                }
            }

            if (bestTitle != null)
            {
                _logger.Debug("Scoring using release title '{0}': [{1}] ({2}, priority {3}) improves on ({4}, priority {5})",
                    bestTitle,
                    bestFormats.ConcatToString(),
                    bestTotal,
                    bestPriority,
                    incumbentTotal,
                    incumbentPriority);
            }

            return bestFormats;
        }

        // krzw(grabbed-release-title): distinct, non-empty, incumbent excluded (it is already the baseline)
        private static IEnumerable<string> ScoringCandidateTitles(string incumbentTitle, params string[] candidates)
        {
            var seen = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

            if (incumbentTitle.IsNotNullOrWhiteSpace())
            {
                seen.Add(incumbentTitle);
            }

            foreach (var candidate in candidates)
            {
                if (candidate.IsNullOrWhiteSpace() || !seen.Add(candidate))
                {
                    continue;
                }

                yield return candidate;
            }
        }

        // krzw(grabbed-release-title)
        public List<CustomFormat> ParseCustomFormatForScoring(EpisodeFile episodeFile, Series series)
        {
            return ParseCustomFormatForScoring(episodeFile, series, _formatService.All());
        }

        // krzw(grabbed-release-title)
        public List<CustomFormat> ParseCustomFormatForScoring(EpisodeFile episodeFile)
        {
            return ParseCustomFormatForScoring(episodeFile, episodeFile.Series?.Value, _formatService.All());
        }

        // krzw(grabbed-release-title)
        public List<CustomFormat> ParseCustomFormatForScoring(LocalEpisode localEpisode)
        {
            var allCustomFormats = _formatService.All();
            var incumbentTitle = localEpisode.SceneName.IsNotNullOrWhiteSpace()
                ? localEpisode.SceneName
                : Path.GetFileName(localEpisode.Path);

            var incumbentFormats = ParseCustomFormat(BuildInput(localEpisode, incumbentTitle), allCustomFormats);

            var qualityProfile = localEpisode.Series?.QualityProfile?.Value;

            if (!_configService.ScoreFilesByGrabbedReleaseTitle ||
                localEpisode.GrabbedReleaseTitle.IsNullOrWhiteSpace() ||
                qualityProfile == null)
            {
                return incumbentFormats;
            }

            var candidateTitles = ScoringCandidateTitles(incumbentTitle,
                localEpisode.GrabbedReleaseTitle,
                localEpisode.SceneName,
                Path.GetFileName(localEpisode.Path));

            return SelectBestScoringFormats(qualityProfile,
                incumbentFormats,
                candidateTitles,
                title => ParseCustomFormat(BuildInput(localEpisode, title), allCustomFormats));
        }

        // krzw(grabbed-release-title)
        private List<CustomFormat> ParseCustomFormatForScoring(EpisodeFile episodeFile, Series series, List<CustomFormat> allCustomFormats)
        {
            var incumbentTitle = GetLadderReleaseTitle(episodeFile);
            var incumbentFormats = ParseCustomFormat(BuildInput(episodeFile, series, incumbentTitle), allCustomFormats);

            var qualityProfile = series?.QualityProfile?.Value;

            if (!_configService.ScoreFilesByGrabbedReleaseTitle ||
                episodeFile.GrabbedReleaseTitle.IsNullOrWhiteSpace() ||
                qualityProfile == null)
            {
                return incumbentFormats;
            }

            var candidateTitles = ScoringCandidateTitles(incumbentTitle,
                episodeFile.GrabbedReleaseTitle,
                episodeFile.SceneName,
                episodeFile.OriginalFilePath.IsNotNullOrWhiteSpace() ? Path.GetFileName(episodeFile.OriginalFilePath) : null);

            return SelectBestScoringFormats(qualityProfile,
                incumbentFormats,
                candidateTitles,
                title => ParseCustomFormat(BuildInput(episodeFile, series, title), allCustomFormats));
        }
    }
}
