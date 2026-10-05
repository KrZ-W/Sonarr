using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.EpisodeImport.Specifications
{
    // krzw(symlink-import-guard): a symbolic link is never a legitimate import source here. GetFileSize follows
    // the link (so it looks like a full-size video) and the Mono disk provider recreates the link at the
    // destination instead of copying bytes, so importing one leaves a link in the library or, when it is judged
    // an upgrade, deletes the very file the link points at. Hardlinks are regular files and are not affected.
    public class NotSymlinkSpecification : IImportDecisionEngineSpecification
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public NotSymlinkSpecification(IDiskProvider diskProvider, IConfigService configService, Logger logger)
        {
            _diskProvider = diskProvider;
            _configService = configService;
            _logger = logger;
        }

        public ImportSpecDecision IsSatisfiedBy(LocalEpisode localEpisode, DownloadClientItem downloadClientItem)
        {
            if (!_configService.RejectSymlinkImportSources)
            {
                return ImportSpecDecision.Accept();
            }

            var target = _diskProvider.GetSymbolicLinkTarget(localEpisode.Path);

            if (target == null)
            {
                return ImportSpecDecision.Accept();
            }

            _logger.Warn("Rejecting {0} as an import source: it is a symbolic link to {1}", localEpisode.Path, target);

            return ImportSpecDecision.Reject(ImportRejectionReason.SourceIsSymlink, $"Source is a symbolic link → {target}");
        }
    }
}
