using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.DataAugmentation.Scene
{
    // krzw(scene-mappings): outcome of one import request, with skips broken down by reason.
    public class UserSceneMappingImportResult
    {
        public int SeriesProcessed { get; set; }
        public int TitlesAdded { get; set; }

        // Rejected by the library guard: a parse term of the title is another library series' title.
        public int TitlesGuardedLibrary { get; set; }

        // Rejected by the mapping-table guard: a parse term already maps to a different tvdbId.
        public int TitlesConflictingMapping { get; set; }

        // Folds to something the scene-name search filter would discard; storing it would be a dead row.
        public int TitlesUnsearchable { get; set; }

        // Equal to the series' own title, or every parse term is already mapped for this series.
        public int TitlesAlreadyPresent { get; set; }

        public int TitlesSkipped => TitlesGuardedLibrary + TitlesConflictingMapping + TitlesUnsearchable + TitlesAlreadyPresent;

        // "<title> (<year>) [tvdb:<id>]" for rows whose series is not in the library.
        public List<string> SeriesNotFound { get; } = new List<string>();

        // "<title> (<year>) [tvdb:<id>]: <error>" for rows whose import threw. The rest of the
        // request still completes; nothing is rolled back for the failed series.
        public List<string> SeriesFailed { get; } = new List<string>();
    }

    // Per-series outcome of SceneMappingService.UpsertUserMappingsDetailed.
    public class UserSceneMappingUpsertResult
    {
        public List<SceneMapping> Added { get; } = new List<SceneMapping>();
        public int TitlesUnsearchable { get; set; }
        public int TitlesConflictingMapping { get; set; }
        public int TitlesAlreadyPresent { get; set; }

        public int TitlesAdded => Added.Select(m => m.Title).Distinct().Count();
    }
}
