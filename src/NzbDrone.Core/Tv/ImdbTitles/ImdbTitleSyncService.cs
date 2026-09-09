using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.Tv.ImdbTitles
{
    // krzw(imdb-title-provider): turns indexed akas rows into UserSceneMappingImportRequests and
    // hands them to the user scene-mapping pipeline. Owns nothing else: the library and
    // mapping-table guards, search-term folding, parse terms and the summary all belong to
    // IUserSceneMappingImportService / SceneMappingService.
    public interface IImdbTitleSyncService
    {
        ImdbTitleSyncSummary SyncAll();
        ImdbTitleSyncSummary SyncSeries(Series series);
        UserSceneMappingImportRequest BuildRequest(Series series);
    }

    public class ImdbTitleSyncService : IImdbTitleSyncService, IHandleAsync<SeriesAddedEvent>, IHandleAsync<SeriesUpdatedEvent>
    {
        private readonly IConfigService _configService;
        private readonly IImdbAkasDatabase _database;
        private readonly ISeriesService _seriesService;
        private readonly ISceneMappingService _sceneMappingService;
        private readonly IUserSceneMappingImportService _importService;
        private readonly Logger _logger;

        public ImdbTitleSyncService(IConfigService configService,
                                    IImdbAkasDatabase database,
                                    ISeriesService seriesService,
                                    ISceneMappingService sceneMappingService,
                                    IUserSceneMappingImportService importService,
                                    Logger logger)
        {
            _configService = configService;
            _database = database;
            _seriesService = seriesService;
            _sceneMappingService = sceneMappingService;
            _importService = importService;
            _logger = logger;
        }

        public ImdbTitleSyncSummary SyncAll()
        {
            if (!IsReady(logWhenDisabled: true))
            {
                return new ImdbTitleSyncSummary();
            }

            var series = _seriesService.GetAllSeries().Where(s => s.ImdbId.IsNotNullOrWhiteSpace()).ToList();

            _logger.Debug("Building IMDb title candidates for {0} series with an IMDb id", series.Count);

            return Import(series.Select(BuildRequest).ToList());
        }

        public ImdbTitleSyncSummary SyncSeries(Series series)
        {
            if (series == null || series.ImdbId.IsNullOrWhiteSpace() || !IsReady(logWhenDisabled: false))
            {
                return new ImdbTitleSyncSummary();
            }

            return Import(new List<UserSceneMappingImportRequest> { BuildRequest(series) });
        }

        // The per-series decision: which indexed titles the series does not have yet (its own
        // title and every scene mapping already stored for its tvdbId, compared loosely).
        // Region travels as-is into the mapping's Comment; Sonarr has no per-region search.
        public UserSceneMappingImportRequest BuildRequest(Series series)
        {
            var request = new UserSceneMappingImportRequest
            {
                TvdbId = series.TvdbId,
                ImdbId = series.ImdbId,
                SeriesTitle = series.Title,
                Year = series.Year
            };

            var rows = _database.GetTitles(series.ImdbId);

            if (rows.Count == 0)
            {
                return request;
            }

            var known = new HashSet<string>(StringComparer.Ordinal) { ImdbTitleNormalizer.Normalize(series.Title) };
            known.UnionWith(_sceneMappingService.FindByTvdbId(series.TvdbId).Select(m => ImdbTitleNormalizer.Normalize(m.Title)));
            known.Remove(string.Empty);

            foreach (var row in rows)
            {
                var key = ImdbTitleNormalizer.Normalize(row.Title);

                if (key.Length == 0 || !known.Add(key))
                {
                    continue;
                }

                request.Titles.Add(new UserSceneMappingImportEntry { Title = row.Title, Region = row.Region });
            }

            return request;
        }

        public void HandleAsync(SeriesAddedEvent message)
        {
            Sync(message.Series, "added");
        }

        public void HandleAsync(SeriesUpdatedEvent message)
        {
            Sync(message.Series, "refreshed");
        }

        private void Sync(Series series, string reason)
        {
            try
            {
                var summary = SyncSeries(series);

                if (summary.Result != null)
                {
                    _logger.Debug("IMDb titles for {0} ({1}): {2}", series, reason, summary);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "IMDb title sync failed for {0}", series);
            }
        }

        private bool IsReady(bool logWhenDisabled)
        {
            if (!_configService.ImdbTitleProviderEnabled)
            {
                if (logWhenDisabled)
                {
                    _logger.Debug("IMDb Title Provider is disabled");
                }

                return false;
            }

            if (!_database.Exists)
            {
                _logger.Debug("IMDb title index {0} does not exist yet; run the ImdbTitleDatasetRefresh task", _database.Path);
                return false;
            }

            return true;
        }

        private ImdbTitleSyncSummary Import(List<UserSceneMappingImportRequest> requests)
        {
            var summary = new ImdbTitleSyncSummary { SeriesChecked = requests.Count };
            var withCandidates = requests.Where(r => r.Titles.Count > 0).ToList();

            summary.SeriesWithCandidates = withCandidates.Count;

            if (withCandidates.Count > 0)
            {
                summary.Result = _importService.Import(withCandidates);
            }

            return summary;
        }
    }

    public class ImdbTitleSyncSummary
    {
        public int SeriesChecked { get; set; }
        public int SeriesWithCandidates { get; set; }
        public UserSceneMappingImportResult Result { get; set; }

        public override string ToString()
        {
            return $"{SeriesChecked} series checked, {SeriesWithCandidates} with candidates; " +
                   $"mappings added {Result?.TitlesAdded ?? 0} / skipped {Result?.TitlesSkipped ?? 0}";
        }
    }
}
