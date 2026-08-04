using System.Collections.Generic;

namespace Sonarr.Api.V3.SceneMappings
{
    public class UserSceneMappingImportResource
    {
        public int TvdbId { get; set; }
        public string ImdbId { get; set; }
        public string SeriesTitle { get; set; }
        public int Year { get; set; }
        public List<UserSceneMappingImportEntryResource> MissingFrenchTitles { get; set; }
    }

    public class UserSceneMappingImportEntryResource
    {
        public string Title { get; set; }
        public string Region { get; set; }
    }

    public class UserSceneMappingImportSummaryResource
    {
        public int SeriesProcessed { get; set; }
        public int TitlesAdded { get; set; }
        public int TitlesSkipped { get; set; }
        public List<string> SeriesNotFound { get; set; } = new List<string>();
    }
}
