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

                var titles = (resource.MissingFrenchTitles ?? new List<UserSceneMappingImportEntryResource>())
                    .Where(t => t.Title.IsNotNullOrWhiteSpace())
                    .Where(t => TitleIsNotAnotherSeries(t.Title, series))
                    .Select(t => new SceneMapping { Title = t.Title, Comment = t.Region })
                    .ToList();

                var added = _sceneMappingService.UpsertUserMappings(titles, series);

                summary.SeriesProcessed++;
                summary.TitlesAdded += added.Count;
                summary.TitlesSkipped += titles.Count - added.Count;
            }

            return summary;
        }

        // A user mapping equal to another library series' title would hijack that series'
        // release parsing; the mapping table guard in UpsertUserMappings can't see series titles.
        private bool TitleIsNotAnotherSeries(string title, NzbDrone.Core.Tv.Series series)
        {
            var other = _seriesService.FindByTitle(title);

            return other == null || other.TvdbId == series.TvdbId;
        }
    }
}
