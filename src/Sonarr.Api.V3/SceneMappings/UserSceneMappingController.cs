using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.Tv;
using Sonarr.Http;

namespace Sonarr.Api.V3.SceneMappings
{
    [V3ApiController("scenemapping")]
    public class UserSceneMappingController : Controller
    {
        private readonly ISceneMappingService _sceneMappingService;
        private readonly ISeriesService _seriesService;

        public UserSceneMappingController(ISceneMappingService sceneMappingService, ISeriesService seriesService)
        {
            _sceneMappingService = sceneMappingService;
            _seriesService = seriesService;
        }

        [HttpPost("user/import")]
        [Consumes("application/json")]
        public UserSceneMappingImportSummaryResource ImportUserTitles([FromBody] List<UserSceneMappingImportResource> resources)
        {
            var summary = new UserSceneMappingImportSummaryResource();

            if (resources == null)
            {
                return summary;
            }

            // Built once per request rather than a lookup per title: FindByTitle also throws
            // MultipleSeriesFoundException when two library series share a clean title (The
            // Office UK/US), which would abort an import that has already inserted rows.
            var libraryTitles = _seriesService.GetAllSeries().ToLookup(s => s.CleanTitle, s => s.TvdbId);

            foreach (var resource in resources)
            {
                var series = _seriesService.FindByTvdbId(resource.TvdbId);

                if (series == null && resource.ImdbId.IsNotNullOrWhiteSpace())
                {
                    series = _seriesService.FindByImdbId(resource.ImdbId);
                }

                if (series == null)
                {
                    summary.SeriesNotFound.Add($"{resource.SeriesTitle} ({resource.Year}) [tvdb:{resource.TvdbId}]");
                    continue;
                }

                var candidates = (resource.MissingFrenchTitles ?? new List<UserSceneMappingImportEntryResource>())
                    .Where(t => t?.Title.IsNotNullOrWhiteSpace() == true)
                    .ToList();

                var titles = candidates
                    .Where(t => TitleIsNotAnotherSeries(libraryTitles, t.Title, series))
                    .Select(t => new SceneMapping { Title = t.Title, Comment = t.Region })
                    .ToList();

                var added = _sceneMappingService.UpsertUserMappings(titles, series);
                var addedTitles = added.Select(m => m.Title).Distinct().Count();

                summary.SeriesProcessed++;
                summary.TitlesAdded += addedTitles;
                summary.TitlesSkipped += candidates.Count - addedTitles;
            }

            return summary;
        }

        // A user mapping equal to another library series' title would hijack that series'
        // release parsing; the mapping table guard in UpsertUserMappings can't see series titles.
        // Compares the parse terms actually stored, so a title whose folded spelling collides is
        // caught too.
        private static bool TitleIsNotAnotherSeries(ILookup<string, int> libraryTitles, string title, NzbDrone.Core.Tv.Series series)
        {
            return SceneMappingService.GetParseTerms(title)
                .All(parseTerm => libraryTitles[parseTerm].All(tvdbId => tvdbId == series.TvdbId));
        }
    }
}
