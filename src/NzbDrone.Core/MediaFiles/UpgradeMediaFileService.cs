using System;  // krzw(atomic-upgrade)
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles.EpisodeImport;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles
{
    public interface IUpgradeMediaFiles
    {
        EpisodeFileMoveResult UpgradeEpisodeFile(EpisodeFile episodeFile, LocalEpisode localEpisode, bool copyOnly = false);
        void FinalizeUpgrade(EpisodeFileMoveResult moveResult);  // krzw(atomic-upgrade)
        void RollbackUpgrade(EpisodeFileMoveResult moveResult);  // krzw(atomic-upgrade)
    }

    public class UpgradeMediaFileService : IUpgradeMediaFiles
    {
        // krzw(atomic-upgrade)
        // Suffix appended when parking an existing file aside. Not a recognised video extension, so a
        // library scan will never pick a parked file up as an importable media file.
        private const string ParkedFileSuffix = ".krzw-upgrade-bak";

        private readonly IRecycleBinProvider _recycleBinProvider;
        private readonly IMediaFileService _mediaFileService;
        private readonly IMoveEpisodeFiles _episodeFileMover;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public UpgradeMediaFileService(IRecycleBinProvider recycleBinProvider,
                                       IMediaFileService mediaFileService,
                                       IMoveEpisodeFiles episodeFileMover,
                                       IDiskProvider diskProvider,
                                       Logger logger)
        {
            _recycleBinProvider = recycleBinProvider;
            _mediaFileService = mediaFileService;
            _episodeFileMover = episodeFileMover;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public EpisodeFileMoveResult UpgradeEpisodeFile(EpisodeFile episodeFile, LocalEpisode localEpisode, bool copyOnly = false)
        {
            var moveFileResult = new EpisodeFileMoveResult();
            moveFileResult.SourcePath = localEpisode.Path;  // krzw(atomic-upgrade)

            var existingFiles = localEpisode.Episodes
                                            .Where(e => e.EpisodeFileId > 0)
                                            .Select(e => e.EpisodeFile.Value)
                                            .Where(e => e != null)
                                            .GroupBy(e => e.Id)
                                            .ToList();

            var rootFolder = _diskProvider.GetParentFolder(localEpisode.Series.Path);

            // If there are existing episode files and the root folder is missing, throw, so the old file isn't left behind during the import process.
            if (existingFiles.Any() && !_diskProvider.FolderExists(rootFolder))
            {
                throw new RootFolderNotFoundException($"Root folder '{rootFolder}' was not found.");
            }

            // krzw(atomic-upgrade): replaces upstream delete-before-move
            // Park each existing file aside (rename, do NOT delete). This frees the destination slot for
            // the incoming file while keeping the original fully recoverable. The actual deletion is
            // deferred to FinalizeUpgrade, which the caller invokes only after the replacement's DB row
            // has been written. On any failure before that point the originals are restored.
            foreach (var existingFile in existingFiles)
            {
                var file = existingFile.First();
                var episodeFilePath = Path.Combine(localEpisode.Series.Path, file.RelativePath);
                var subfolder = rootFolder.GetRelativePath(_diskProvider.GetParentFolder(episodeFilePath));

                // krzw(atomic-upgrade)
                var pending = new PendingUpgradeFile
                {
                    EpisodeFile = file,
                    OriginalPath = episodeFilePath,
                    Subfolder = subfolder,
                    BackupPath = null
                };

                if (_diskProvider.FileExists(episodeFilePath))
                {
                    // krzw(atomic-upgrade)
                    var backupPath = episodeFilePath + ParkedFileSuffix;

                    // Clear a stale parked file left by a previously interrupted run.
                    if (_diskProvider.FileExists(backupPath))
                    {
                        _diskProvider.DeleteFile(backupPath);
                    }

                    _logger.Debug("Parking existing episode file before upgrade: {0} -> {1}", episodeFilePath, backupPath);
                    _diskProvider.MoveFile(episodeFilePath, backupPath);
                    pending.BackupPath = backupPath;
                }
                else
                {
                    _logger.Warn("Existing episode file missing from disk, nothing to park: {0}", episodeFilePath);  // krzw(atomic-upgrade)
                }

                moveFileResult.PendingUpgrades.Add(pending);  // krzw(atomic-upgrade)
            }

            localEpisode.OldFiles = moveFileResult.OldFiles;

            // krzw(atomic-upgrade): restore parked originals if the transfer fails
            try
            {
                if (copyOnly)
                {
                    moveFileResult.EpisodeFile = _episodeFileMover.CopyEpisodeFile(episodeFile, localEpisode);
                }
                else
                {
                    moveFileResult.EpisodeFile = _episodeFileMover.MoveEpisodeFile(episodeFile, localEpisode);
                }
            }
            catch
            {
                // The replacement transfer failed. Restore every parked original so the slot is never
                // left empty, then rethrow for the caller to record the failed import. No DB rows have
                // been touched yet, so nothing needs to be re-added.
                RestoreParkedFiles(moveFileResult);
                throw;
            }

            // krzw(atomic-upgrade)
            moveFileResult.NewFilePath = Path.Combine(localEpisode.Series.Path, moveFileResult.EpisodeFile.RelativePath);

            return moveFileResult;
        }

        // krzw(atomic-upgrade)
        public void FinalizeUpgrade(EpisodeFileMoveResult moveResult)
        {
            // The replacement is on disk and in the database. Now remove the parked originals: send them
            // to the recycle bin and delete their DB rows (which raises the episodeFileDeleted/Upgrade
            // event). A failure here must never fail the already-committed import.
            foreach (var pending in moveResult.PendingUpgrades)
            {
                string recycleBinPath = null;

                if (pending.BackupPath != null)
                {
                    try
                    {
                        // Recycle under the original filename when the slot is free (the replacement went
                        // to a different path); otherwise recycle the parked file as-is.
                        if (pending.OriginalPath != moveResult.NewFilePath && !_diskProvider.FileExists(pending.OriginalPath))
                        {
                            _diskProvider.MoveFile(pending.BackupPath, pending.OriginalPath);
                            recycleBinPath = _recycleBinProvider.DeleteFile(pending.OriginalPath, pending.Subfolder);
                        }
                        else
                        {
                            recycleBinPath = _recycleBinProvider.DeleteFile(pending.BackupPath, pending.Subfolder);
                        }
                    }
                    catch (Exception e)
                    {
                        _logger.Warn(e, "Upgrade committed but unable to recycle parked original '{0}'; attempting permanent delete", pending.BackupPath);

                        try
                        {
                            // The file is either still parked or was renamed back just before the
                            // recycle failed; clean up whichever location holds it.
                            if (_diskProvider.FileExists(pending.BackupPath))
                            {
                                _diskProvider.DeleteFile(pending.BackupPath);
                            }
                            else if (pending.OriginalPath != moveResult.NewFilePath && _diskProvider.FileExists(pending.OriginalPath))
                            {
                                _diskProvider.DeleteFile(pending.OriginalPath);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Warn(ex, "Unable to delete parked original '{0}'; it may need manual cleanup", pending.BackupPath);
                        }
                    }
                }

                _mediaFileService.Delete(pending.EpisodeFile, DeleteMediaFileReason.Upgrade);
                moveResult.OldFiles.Add(new DeletedEpisodeFile(pending.EpisodeFile, recycleBinPath));
            }
        }

        // krzw(atomic-upgrade)
        public void RollbackUpgrade(EpisodeFileMoveResult moveResult)
        {
            // The import failed after the replacement was placed but before it was committed to the DB.
            // Put the replacement back where it came from (or drop the copy), then restore every parked
            // original.
            if (moveResult.NewFilePath.IsNotNullOrWhiteSpace() && _diskProvider.FileExists(moveResult.NewFilePath))
            {
                try
                {
                    RemoveOrReturnToSource(moveResult.NewFilePath, moveResult.SourcePath);
                }
                catch (Exception e)
                {
                    _logger.Warn(e, "Unable to remove failed-import replacement file '{0}'", moveResult.NewFilePath);
                }
            }

            RestoreParkedFiles(moveResult);
        }

        // krzw(atomic-upgrade)
        private void RestoreParkedFiles(EpisodeFileMoveResult moveResult)
        {
            foreach (var pending in moveResult.PendingUpgrades)
            {
                if (pending.BackupPath == null)
                {
                    continue;
                }

                try
                {
                    if (!_diskProvider.FileExists(pending.BackupPath))
                    {
                        continue;
                    }

                    // The original slot may still hold a stray replacement (same-path upgrade); clear it
                    // so the genuine original can be put back.
                    if (_diskProvider.FileExists(pending.OriginalPath))
                    {
                        RemoveOrReturnToSource(pending.OriginalPath, moveResult.SourcePath);
                    }

                    _logger.Debug("Restoring parked original after failed upgrade: {0} -> {1}", pending.BackupPath, pending.OriginalPath);
                    _diskProvider.MoveFile(pending.BackupPath, pending.OriginalPath);
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Unable to restore parked original '{0}' to '{1}' after a failed upgrade; the file remains parked and may need manual recovery", pending.BackupPath, pending.OriginalPath);
                }
            }
        }

        // krzw(atomic-upgrade)
        private void RemoveOrReturnToSource(string path, string sourcePath)
        {
            // A missing source means the replacement was moved out of the download location; return it
            // so the download client item stays importable on a later attempt. A present source means
            // it was copied or hardlinked, so the stray library copy can simply be removed.
            if (sourcePath.IsNotNullOrWhiteSpace() && !_diskProvider.FileExists(sourcePath))
            {
                _logger.Debug("Returning failed-import replacement to its source: {0} -> {1}", path, sourcePath);
                _diskProvider.MoveFile(path, sourcePath);
            }
            else
            {
                _diskProvider.DeleteFile(path);
            }
        }
    }
}
