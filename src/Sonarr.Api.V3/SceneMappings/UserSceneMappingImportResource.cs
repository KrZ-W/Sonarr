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

        // Neutral alias for MissingFrenchTitles (the curated dataset's field name); either may be sent.
        public List<UserSceneMappingImportEntryResource> Titles
        {
            get => MissingFrenchTitles;
            set => MissingFrenchTitles = value;
        }
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

        // Breakdown of TitlesSkipped.
        public int TitlesGuardedLibrary { get; set; }
        public int TitlesConflictingMapping { get; set; }
        public int TitlesUnsearchable { get; set; }
        public int TitlesAlreadyPresent { get; set; }

        public List<string> SeriesNotFound { get; set; } = new List<string>();

        // Rows whose import threw; the rest of the request still completed.
        public List<string> SeriesFailed { get; set; } = new List<string>();
    }
}
