using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.DataAugmentation.Scene
{
    // krzw(scene-mappings): the curated-title import pipeline behind
    // POST /api/v3/scenemapping/user/import. Resolution (tvdb -> imdb), the library-title guard
    // and the summary live here; the controller only translates resources. Work is done per
    // series: a series that throws is reported in SeriesFailed and the request continues.
    public interface IUserSceneMappingImportService
    {
        UserSceneMappingImportResult Import(List<UserSceneMappingImportRequest> requests);
    }

    public class UserSceneMappingImportService : IUserSceneMappingImportService
    {
        private readonly ISceneMappingService _sceneMappingService;
        private readonly ISeriesService _seriesService;
        private readonly Logger _logger;

        public UserSceneMappingImportService(ISceneMappingService sceneMappingService, ISeriesService seriesService, Logger logger)
        {
            _sceneMappingService = sceneMappingService;
            _seriesService = seriesService;
            _logger = logger;
        }

        public UserSceneMappingImportResult Import(List<UserSceneMappingImportRequest> requests)
        {
            var result = new UserSceneMappingImportResult();

            if (requests == null || requests.Count == 0)
            {
                return result;
            }

            // Built once per request rather than a lookup per title: FindByTitle throws
            // MultipleSeriesFoundException when two library series share a clean title (The
            // Office UK/US), which would abort an import that has already inserted rows.
            var libraryTitles = _seriesService.GetAllSeries().ToLookup(s => s.CleanTitle, s => s.TvdbId);

            foreach (var request in requests)
            {
                var label = Describe(request);

                try
                {
                    var series = Resolve(request);

                    if (series == null)
                    {
                        result.SeriesNotFound.Add(label);
                        continue;
                    }

                    var entries = (request.Titles ?? new List<UserSceneMappingImportEntry>())
                        .Where(e => e != null && e.Title.IsNotNullOrWhiteSpace())
                        .ToList();

                    var safe = new List<SceneMapping>();

                    foreach (var entry in entries)
                    {
                        if (IsAnotherSeriesTitle(libraryTitles, entry.Title, series))
                        {
                            _logger.Warn("Skipping user scene mapping '{0}' for {1}: it is another library series' title", entry.Title, series.Title);
                            result.TitlesGuardedLibrary++;
                            continue;
                        }

                        safe.Add(new SceneMapping { Title = entry.Title, Comment = entry.Region });
                    }

                    var upsert = _sceneMappingService.UpsertUserMappingsDetailed(safe, series);

                    result.SeriesProcessed++;
                    result.TitlesAdded += upsert.TitlesAdded;
                    result.TitlesUnsearchable += upsert.TitlesUnsearchable;
                    result.TitlesConflictingMapping += upsert.TitlesConflictingMapping;
                    result.TitlesAlreadyPresent += upsert.TitlesAlreadyPresent;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "User scene mapping import failed for {0}", label);
                    result.SeriesFailed.Add($"{label}: {ex.Message}");
                }
            }

            return result;
        }

        // A user mapping equal to another library series' title would hijack that series'
        // release parsing; the mapping-table guard in UpsertUserMappings cannot see series
        // titles. Compares the parse terms actually stored, so a title whose folded spelling
        // collides is caught too.
        private static bool IsAnotherSeriesTitle(ILookup<string, int> libraryTitles, string title, Series series)
        {
            return SceneMappingService.GetParseTerms(title)
                .Any(parseTerm => libraryTitles[parseTerm].Any(tvdbId => tvdbId != series.TvdbId));
        }

        private Series Resolve(UserSceneMappingImportRequest request)
        {
            var series = request.TvdbId > 0 ? _seriesService.FindByTvdbId(request.TvdbId) : null;

            if (series == null && request.ImdbId.IsNotNullOrWhiteSpace())
            {
                series = _seriesService.FindByImdbId(request.ImdbId);
            }

            return series;
        }

        private static string Describe(UserSceneMappingImportRequest request)
        {
            return $"{request.SeriesTitle} ({request.Year}) [tvdb:{request.TvdbId}]";
        }
    }
}
