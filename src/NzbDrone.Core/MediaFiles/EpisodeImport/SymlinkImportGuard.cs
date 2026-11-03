using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MediaFiles.EpisodeImport
{
    // krzw(symlink-import-guard): one answer to "is this import source a symbolic link, and what does it point at?",
    // shared by the decision spec (first line), the import funnel (second line, catches entry points that skip the
    // specs such as the final Manual Import step) and the file mover (last line, runs right before the bytes are
    // touched so a link created between the decision and the transfer is still refused).
    public class SymlinkImportCheck
    {
        public string Target { get; set; }
        public EpisodeFile LibraryFile { get; set; }
        public ImportRejectionReason Reason { get; set; }
        public string Message { get; set; }

        public bool IsCurrentFile => Reason == ImportRejectionReason.SourceIsSymlinkToCurrentFile;
    }

    public interface ISymlinkImportGuard
    {
        // Null when the setting is off or the source is not a symbolic link.
        SymlinkImportCheck Check(LocalEpisode localEpisode);
    }

    public class SymlinkImportGuard : ISymlinkImportGuard
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IConfigService _configService;
        private readonly ISeriesService _seriesService;
        private readonly IEpisodeService _episodeService;
        private readonly IMediaFileService _mediaFileService;
        private readonly Logger _logger;

        public SymlinkImportGuard(IDiskProvider diskProvider,
                                  IConfigService configService,
                                  ISeriesService seriesService,
                                  IEpisodeService episodeService,
                                  IMediaFileService mediaFileService,
                                  Logger logger)
        {
            _diskProvider = diskProvider;
            _configService = configService;
            _seriesService = seriesService;
            _episodeService = episodeService;
            _mediaFileService = mediaFileService;
            _logger = logger;
        }

        public SymlinkImportCheck Check(LocalEpisode localEpisode)
        {
            if (!_configService.RejectSymlinkImportSources || localEpisode?.Path.IsNullOrWhiteSpace() != false)
            {
                return null;
            }

            var target = _diskProvider.GetSymbolicLinkTarget(localEpisode.Path);

            if (target == null)
            {
                return null;
            }

            var check = new SymlinkImportCheck
            {
                Target = target,
                Reason = ImportRejectionReason.SourceIsSymlink,
                Message = $"Source is a symbolic link → {target}"
            };

            var libraryFile = FindOwnFile(localEpisode, target) ?? FindLibraryFile(target);

            if (libraryFile == null)
            {
                return check;
            }

            check.LibraryFile = libraryFile;

            var episodes = localEpisode.Episodes ?? new List<Episode>();

            if (episodes.Any() && episodes.All(e => e.EpisodeFileId == libraryFile.Id))
            {
                check.Reason = ImportRejectionReason.SourceIsSymlinkToCurrentFile;
                check.Message = $"Source is a symbolic link → {target}, which is already the current file of {(episodes.Count > 1 ? "these episodes" : "this episode")}";
            }
            else
            {
                check.Reason = ImportRejectionReason.SourceIsSymlinkToLibraryFile;
                check.Message = $"Source is a symbolic link → {target}, which is already the library file of {Describe(localEpisode, libraryFile)}";
            }

            _logger.Trace("{0} resolves to library file [{1}] ({2})", localEpisode.Path, libraryFile.Id, check.Reason);

            return check;
        }

        // The current file(s) of the episodes this candidate maps to: cheap, and the case completed download
        // handling needs to recognise (a seed payload relinked to the library copy it was imported as).
        private EpisodeFile FindOwnFile(LocalEpisode localEpisode, string target)
        {
            var series = localEpisode.Series;
            var fileIds = (localEpisode.Episodes ?? new List<Episode>())
                          .Where(e => e.EpisodeFileId > 0)
                          .Select(e => e.EpisodeFileId)
                          .Distinct()
                          .ToList();

            if (series?.Path == null || fileIds.Empty())
            {
                return null;
            }

            return _mediaFileService.GetFiles(fileIds)
                                    .FirstOrDefault(f => FullPath(series.Path, f).PathEquals(target));
        }

        // Any tracked file of any series: the link points into the library, so importing it could only ever
        // duplicate (or, as an upgrade, destroy) something Sonarr already has.
        private EpisodeFile FindLibraryFile(string target)
        {
            foreach (var (seriesId, seriesPath) in _seriesService.GetAllSeriesPaths())
            {
                if (seriesPath.IsNullOrWhiteSpace() || !seriesPath.IsParentPath(target))
                {
                    continue;
                }

                var relativePath = seriesPath.GetRelativePath(target);
                var file = _mediaFileService.GetFilesWithRelativePath(seriesId, relativePath).FirstOrDefault();

                if (file != null)
                {
                    return file;
                }
            }

            return null;
        }

        private string Describe(LocalEpisode localEpisode, EpisodeFile libraryFile)
        {
            var series = localEpisode.Series?.Id == libraryFile.SeriesId ? localEpisode.Series : _seriesService.GetSeries(libraryFile.SeriesId);
            var episodes = _episodeService.GetEpisodesByFileId(libraryFile.Id) ?? new List<Episode>();
            var title = series?.Title ?? $"series {libraryFile.SeriesId}";

            if (episodes.Empty())
            {
                return $"{title} ({libraryFile.RelativePath ?? libraryFile.Path})";
            }

            var numbers = string.Join(", ", episodes.Select(e => $"S{e.SeasonNumber:00}E{e.EpisodeNumber:00}"));

            return $"{title} {numbers}";
        }

        private static string FullPath(string seriesPath, EpisodeFile file)
        {
            return file.Path.IsNotNullOrWhiteSpace() ? file.Path : Path.Combine(seriesPath, file.RelativePath ?? string.Empty);
        }
    }
}
