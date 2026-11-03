using NLog;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.EpisodeImport.Specifications
{
    // krzw(symlink-import-guard): a symbolic link is never a legitimate import source here. GetFileSize follows
    // the link (so it looks like a full-size video) and the Mono disk provider recreates the link at the
    // destination instead of copying bytes, so importing one leaves a link in the library or, when it is judged
    // an upgrade, deletes the very file the link points at. Hardlinks are regular files and are not affected.
    // The check itself lives in SymlinkImportGuard, shared with the import funnel and the file mover.
    public class NotSymlinkSpecification : IImportDecisionEngineSpecification
    {
        private readonly ISymlinkImportGuard _symlinkImportGuard;
        private readonly Logger _logger;

        public NotSymlinkSpecification(ISymlinkImportGuard symlinkImportGuard, Logger logger)
        {
            _symlinkImportGuard = symlinkImportGuard;
            _logger = logger;
        }

        public ImportSpecDecision IsSatisfiedBy(LocalEpisode localEpisode, DownloadClientItem downloadClientItem)
        {
            var check = _symlinkImportGuard.Check(localEpisode);

            if (check == null)
            {
                return ImportSpecDecision.Accept();
            }

            if (check.IsCurrentFile)
            {
                // Completed download handling treats this as already imported; no need to shout every pass.
                _logger.Debug("Rejecting {0} as an import source: {1}", localEpisode.Path, check.Message);
            }
            else
            {
                _logger.Warn("Rejecting {0} as an import source: {1}", localEpisode.Path, check.Message);
            }

            return ImportSpecDecision.Reject(check.Reason, check.Message);
        }
    }
}
