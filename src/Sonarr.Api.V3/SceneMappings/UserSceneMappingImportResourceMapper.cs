using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.DataAugmentation.Scene;
using Sonarr.Http.REST;

namespace Sonarr.Api.V3.SceneMappings
{
    // krzw(scene-mappings): resource <-> Core request/result mapping plus shape validation. This
    // is the only place that knows the curated dataset's field names.
    public static class UserSceneMappingImportResourceMapper
    {
        public const int MaxSeriesPerRequest = 5000;
        public const int MaxTitlesPerSeries = 100;
        public const int MaxTitleLength = 500;

        public static List<UserSceneMappingImportRequest> ToImportRequests(this List<UserSceneMappingImportResource> resources)
        {
            if (resources == null)
            {
                return new List<UserSceneMappingImportRequest>();
            }

            if (resources.Count > MaxSeriesPerRequest)
            {
                throw new BadRequestException($"At most {MaxSeriesPerRequest} series per request (got {resources.Count})");
            }

            return resources.Select(ToImportRequest).ToList();
        }

        public static UserSceneMappingImportSummaryResource ToResource(this UserSceneMappingImportResult result)
        {
            return new UserSceneMappingImportSummaryResource
            {
                SeriesProcessed = result.SeriesProcessed,
                TitlesAdded = result.TitlesAdded,
                TitlesSkipped = result.TitlesSkipped,
                TitlesGuardedLibrary = result.TitlesGuardedLibrary,
                TitlesConflictingMapping = result.TitlesConflictingMapping,
                TitlesUnsearchable = result.TitlesUnsearchable,
                TitlesAlreadyPresent = result.TitlesAlreadyPresent,
                SeriesNotFound = result.SeriesNotFound.ToList(),
                SeriesFailed = result.SeriesFailed.ToList()
            };
        }

        private static UserSceneMappingImportRequest ToImportRequest(UserSceneMappingImportResource resource)
        {
            var titles = resource.MissingFrenchTitles ?? new List<UserSceneMappingImportEntryResource>();

            if (titles.Count > MaxTitlesPerSeries)
            {
                throw new BadRequestException($"At most {MaxTitlesPerSeries} titles per series (tvdb:{resource.TvdbId} has {titles.Count})");
            }

            if (titles.Any(t => t?.Title != null && t.Title.Length > MaxTitleLength))
            {
                throw new BadRequestException($"Title longer than {MaxTitleLength} characters for tvdb:{resource.TvdbId}");
            }

            return new UserSceneMappingImportRequest
            {
                TvdbId = resource.TvdbId,
                ImdbId = resource.ImdbId,
                SeriesTitle = resource.SeriesTitle,
                Year = resource.Year,
                Titles = titles.Where(t => t != null).Select(t => new UserSceneMappingImportEntry { Title = t.Title, Region = t.Region }).ToList()
            };
        }
    }
}
