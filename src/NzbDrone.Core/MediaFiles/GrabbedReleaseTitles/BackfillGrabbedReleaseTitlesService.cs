using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.History;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MediaFiles.GrabbedReleaseTitles
{
    // krzw(grabbed-release-title)

    /// <summary>
    /// Fills <see cref="EpisodeFile.GrabbedReleaseTitle"/> for files that were imported before the
    /// feature existed.
    ///
    /// Matching is oracle-first: a downloadFolderImported history row carries the imported file's id
    /// in Data["fileId"], which is an exact link, not a heuristic. When such a row exists but carries
    /// no download id the file's real import was a manual import with no grab behind it, and the file
    /// is SKIPPED rather than matched by time — time proximity silently attributes a neighbouring
    /// torrent's title to those files. Only files with no fileId-bearing import row at all fall back
    /// to the closest import within six hours of DateAdded.
    /// </summary>
    public interface IBackfillGrabbedReleaseTitles
    {
        BackfillGrabbedReleaseTitlesResult Backfill();
    }

    public class BackfillGrabbedReleaseTitlesService : IBackfillGrabbedReleaseTitles, IExecute<BackfillGrabbedReleaseTitlesCommand>
    {
        private const int UpdateBatchSize = 500;

        private static readonly TimeSpan ImportMatchWindow = TimeSpan.FromHours(6);

        private readonly ISeriesService _seriesService;
        private readonly IEpisodeService _episodeService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IHistoryService _historyService;
        private readonly Logger _logger;

        public BackfillGrabbedReleaseTitlesService(ISeriesService seriesService,
                                                   IEpisodeService episodeService,
                                                   IMediaFileService mediaFileService,
                                                   IHistoryService historyService,
                                                   Logger logger)
        {
            _seriesService = seriesService;
            _episodeService = episodeService;
            _mediaFileService = mediaFileService;
            _historyService = historyService;
            _logger = logger;
        }

        public BackfillGrabbedReleaseTitlesResult Backfill()
        {
            var result = new BackfillGrabbedReleaseTitlesResult();

            // One pass over history, then dictionaries. Per-file queries would be tens of thousands
            // of round trips over a couple of hundred thousand rows.
            var importEvents = _historyService.GetByEventType(EpisodeHistoryEventType.DownloadFolderImported);
            var grabEvents = _historyService.GetByEventType(EpisodeHistoryEventType.Grabbed);

            var importsByFileId = BuildImportsByFileId(importEvents);
            var importsByEpisodeId = BuildImportsByEpisodeId(importEvents);
            var grabsByDownloadId = BuildGrabsByDownloadId(grabEvents);

            _logger.Debug("Backfilling grabbed release titles from {0} import and {1} grab history records",
                importEvents.Count,
                grabEvents.Count);

            foreach (var series in _seriesService.GetAllSeries())
            {
                var files = _mediaFileService.GetFilesBySeries(series.Id);

                if (files.Empty())
                {
                    continue;
                }

                var episodeIdsByFileId = _episodeService.GetEpisodeBySeries(series.Id)
                    .Where(e => e.EpisodeFileId > 0)
                    .GroupBy(e => e.EpisodeFileId)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.Id).ToList());

                var updated = new List<EpisodeFile>();

                foreach (var file in files)
                {
                    result.Scanned++;

                    var downloadId = FindDownloadId(file, episodeIdsByFileId, importsByFileId, importsByEpisodeId, result);

                    if (downloadId == null)
                    {
                        continue;
                    }

                    if (!grabsByDownloadId.TryGetValue(downloadId.ToUpperInvariant(), out var grab))
                    {
                        result.NoGrab++;
                        continue;
                    }

                    var title = GrabbedReleaseTitleSanitizer.Sanitize(grab.SourceTitle);

                    if (title == null)
                    {
                        result.NoGrab++;
                        continue;
                    }

                    if (title == file.GrabbedReleaseTitle)
                    {
                        result.Unchanged++;
                        continue;
                    }

                    file.GrabbedReleaseTitle = title;
                    updated.Add(file);
                    result.Set++;

                    if (updated.Count >= UpdateBatchSize)
                    {
                        _mediaFileService.Update(updated);
                        updated.Clear();
                    }
                }

                if (updated.Any())
                {
                    _mediaFileService.Update(updated);
                }
            }

            _logger.Info("Grabbed release title backfill finished. scanned={0} set={1} no-import-event={2} no-download-id={3} no-grab={4} unchanged={5}",
                result.Scanned,
                result.Set,
                result.NoImportEvent,
                result.NoDownloadId,
                result.NoGrab,
                result.Unchanged);

            return result;
        }

        public void Execute(BackfillGrabbedReleaseTitlesCommand message)
        {
            _logger.ProgressInfo("Backfilling grabbed release titles");

            Backfill();
        }

        private string FindDownloadId(EpisodeFile file,
                                      Dictionary<int, List<int>> episodeIdsByFileId,
                                      Dictionary<int, EpisodeHistory> importsByFileId,
                                      Dictionary<int, List<EpisodeHistory>> importsByEpisodeId,
                                      BackfillGrabbedReleaseTitlesResult result)
        {
            // The fileId oracle is exact: trust it, including when it says there was no grab.
            if (importsByFileId.TryGetValue(file.Id, out var exactImport))
            {
                if (exactImport.DownloadId.IsNullOrWhiteSpace())
                {
                    result.NoDownloadId++;
                    return null;
                }

                return exactImport.DownloadId;
            }

            if (!episodeIdsByFileId.TryGetValue(file.Id, out var episodeIds))
            {
                result.NoImportEvent++;
                return null;
            }

            EpisodeHistory closest = null;
            var closestDistance = TimeSpan.MaxValue;

            foreach (var episodeId in episodeIds)
            {
                if (!importsByEpisodeId.TryGetValue(episodeId, out var candidates))
                {
                    continue;
                }

                foreach (var candidate in candidates)
                {
                    var distance = (candidate.Date - file.DateAdded).Duration();

                    if (distance > ImportMatchWindow || distance >= closestDistance)
                    {
                        continue;
                    }

                    closest = candidate;
                    closestDistance = distance;
                }
            }

            if (closest == null)
            {
                result.NoImportEvent++;
                return null;
            }

            return closest.DownloadId;
        }

        private static Dictionary<int, EpisodeHistory> BuildImportsByFileId(List<EpisodeHistory> importEvents)
        {
            var byFileId = new Dictionary<int, EpisodeHistory>();

            foreach (var import in importEvents)
            {
                if (!int.TryParse(import.Data.GetValueOrDefault("fileId"), out var fileId) || fileId <= 0)
                {
                    continue;
                }

                // A season pack raises one row per episode; they share the download id, so the newest
                // row for a file id is as good as any and keeps the pick deterministic.
                if (!byFileId.TryGetValue(fileId, out var existing) || import.Date > existing.Date)
                {
                    byFileId[fileId] = import;
                }
            }

            return byFileId;
        }

        private static Dictionary<int, List<EpisodeHistory>> BuildImportsByEpisodeId(List<EpisodeHistory> importEvents)
        {
            // Only download-id bearing rows can lead to a grab, and admitting the others is exactly
            // the manual-import misattribution this matcher exists to avoid.
            return importEvents
                .Where(h => h.DownloadId.IsNotNullOrWhiteSpace() && h.EpisodeId > 0)
                .GroupBy(h => h.EpisodeId)
                .ToDictionary(g => g.Key, g => g.ToList());
        }

        private static Dictionary<string, EpisodeHistory> BuildGrabsByDownloadId(List<EpisodeHistory> grabEvents)
        {
            var byDownloadId = new Dictionary<string, EpisodeHistory>();

            foreach (var grab in grabEvents)
            {
                if (grab.DownloadId.IsNullOrWhiteSpace())
                {
                    continue;
                }

                var key = grab.DownloadId.ToUpperInvariant();

                if (!byDownloadId.TryGetValue(key, out var existing) || grab.Date > existing.Date)
                {
                    byDownloadId[key] = grab;
                }
            }

            return byDownloadId;
        }
    }

    public class BackfillGrabbedReleaseTitlesResult
    {
        public int Scanned { get; set; }
        public int Set { get; set; }
        public int NoImportEvent { get; set; }
        public int NoDownloadId { get; set; }
        public int NoGrab { get; set; }
        public int Unchanged { get; set; }
    }
}
