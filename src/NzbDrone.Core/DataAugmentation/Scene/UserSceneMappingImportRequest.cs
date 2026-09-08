using System.Collections.Generic;

namespace NzbDrone.Core.DataAugmentation.Scene
{
    // krzw(scene-mappings): one curated-dataset row - a series identity plus the titles to map to
    // it. Neutral shape; the API layer maps its dataset-specific resource (missingFrenchTitles)
    // onto this.
    public class UserSceneMappingImportRequest
    {
        public int TvdbId { get; set; }
        public string ImdbId { get; set; }
        public string SeriesTitle { get; set; }
        public int Year { get; set; }
        public List<UserSceneMappingImportEntry> Titles { get; set; } = new List<UserSceneMappingImportEntry>();
    }

    public class UserSceneMappingImportEntry
    {
        public string Title { get; set; }

        // ISO 3166-1 alpha-2 region code, optional. Stored in the mapping's Comment.
        public string Region { get; set; }
    }
}
