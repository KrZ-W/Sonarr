using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles.AudioLanguage;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MediaFiles.AudioTags
{
    public interface IAudioTrackRetagService
    {
        /// <summary>Applies the retag rules to one library file and persists the outcome. Returns the recorded outcome, or null when nothing applied.</summary>
        AudioTrackRetag Retag(EpisodeFile episodeFile, Series series, bool hardlinkHint, bool manual);
    }

    /// <summary>
    /// Post-import "Audio Track Retag": rewrites the language tag of the audio tracks the
    /// Audio Language Verification record says are mistagged, header-only, on the library
    /// file. Runs once per file (result "done" is final), only for new downloads, never on
    /// rescan / refresh / media-info update. A non-Matroska file is skipped, or, with
    /// "Non-MKV files" = Remux to MKV, stream-copied by ffmpeg into a new .mkv that replaces
    /// it (the file record, path and extension change; the rename events are raised).
    /// </summary>
    public class AudioTrackRetagService : IAudioTrackRetagService,
                                          IHandle<EpisodeImportedEvent>,
                                          IExecute<RetagAudioTracksCommand>
    {
        public const string TempSuffix = ".krzw-retag.tmp";
        public const string BackupSuffix = ".krzw-retag.bak";
        public const string RemuxTempSuffix = ".krzw-remux.tmp.mkv";
        public const string RemuxBackupSuffix = ".krzw-remux.bak";
        public const int RemuxTimeoutFactor = 10;
        public const string LinkCountUnavailable = "link count unavailable";
        public static readonly TimeSpan EditTimeout = TimeSpan.FromMinutes(10);

        /// <summary>A remux may not run longer than <see cref="RemuxTimeoutFactor"/> x the verification timeout (default 120 s, so 20 min), never less than this floor.</summary>
        public static readonly TimeSpan RemuxTimeoutFloor = TimeSpan.FromMinutes(10);

        /// <summary>Tolerance between source and remuxed duration as reported by ffprobe.</summary>
        public static readonly TimeSpan RemuxDurationTolerance = TimeSpan.FromSeconds(1);

        private readonly IConfigService _configService;
        private readonly IDiskProvider _diskProvider;
        private readonly IAudioTrackTagger _tagger;
        private readonly IAudioTrackRemuxer _remuxer;
        private readonly IVideoFileInfoReader _videoFileInfoReader;
        private readonly IAudioTrackLayoutReader _layoutReader;
        private readonly IMediaFileService _mediaFileService;
        private readonly ISeriesService _seriesService;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public AudioTrackRetagService(IConfigService configService,
                                      IDiskProvider diskProvider,
                                      IAudioTrackTagger tagger,
                                      IAudioTrackRemuxer remuxer,
                                      IVideoFileInfoReader videoFileInfoReader,
                                      IAudioTrackLayoutReader layoutReader,
                                      IMediaFileService mediaFileService,
                                      ISeriesService seriesService,
                                      IEventAggregator eventAggregator,
                                      Logger logger)
        {
            _configService = configService;
            _diskProvider = diskProvider;
            _tagger = tagger;
            _remuxer = remuxer;
            _videoFileInfoReader = videoFileInfoReader;
            _layoutReader = layoutReader;
            _mediaFileService = mediaFileService;
            _seriesService = seriesService;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public void Handle(EpisodeImportedEvent message)
        {
            if (!_configService.AudioTrackRetagEnabled)
            {
                return;
            }

            // Only the import of a download qualifies; files discovered by a disk scan (rescan,
            // refresh) also raise this event with NewDownload = false and are never touched.
            if (!message.NewDownload)
            {
                return;
            }

            var episodeFile = message.ImportedEpisode;
            var series = message.EpisodeInfo?.Series ?? episodeFile.Series?.Value;
            var hardlinkHint = message.EpisodeInfo?.TransferMode == TransferMode.HardLink;

            try
            {
                Retag(episodeFile, series, hardlinkHint, false);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Audio track retag failed for '{0}'", episodeFile);
            }
        }

        public void Execute(RetagAudioTracksCommand message)
        {
            var episodeFile = _mediaFileService.Get(message.EpisodeFileId);

            if (episodeFile == null)
            {
                throw new ArgumentException($"Episode file {message.EpisodeFileId} does not exist");
            }

            var series = episodeFile.Series?.Value ?? _seriesService.GetSeries(episodeFile.SeriesId);

            var outcome = Retag(episodeFile, series, false, true);

            _logger.Info("Audio track retag of '{0}': {1}", episodeFile, outcome?.Result ?? "nothing to do");
        }

        public AudioTrackRetag Retag(EpisodeFile episodeFile, Series series, bool hardlinkHint, bool manual)
        {
            var path = LibraryPath(episodeFile, series);
            var remuxNonMkv = _configService.AudioTrackRetagNonMkvMode == AudioTrackRetagNonMkvMode.RemuxToMkv;
            var plan = AudioTrackRetagPlanner.Plan(episodeFile, path, _configService.AudioLanguageVerificationConfidenceThreshold, remuxNonMkv);
            var mode = _configService.AudioTrackRetagHardlinkMode;

            switch (plan.SkipReason)
            {
                case AudioTrackRetagSkipReason.AlreadyDone:
                    _logger.Debug("Audio tracks of '{0}' were already retagged on {1}, skipping", path, episodeFile.AudioTrackRetag.At);
                    return null;
                case AudioTrackRetagSkipReason.NotMkv:
                    // Only recorded on a manual request: a non-MKV import is the normal case and
                    // the column should not fill up with "skipped-container" for every MP4.
                    if (manual || episodeFile.AudioLanguageVerification?.Any() == true)
                    {
                        _logger.Info("Audio tracks of '{0}' cannot be retagged: only Matroska files are supported", path);
                        return Record(episodeFile, mode, AudioTrackRetagResult.SkippedContainer, plan, "only Matroska (.mkv) files can be retagged");
                    }

                    return null;
                case AudioTrackRetagSkipReason.NoVerificationRecord:
                    _logger.Debug("No audio language verification record for '{0}', nothing to retag", path);
                    return null;
                case AudioTrackRetagSkipReason.NoMismatch:
                    _logger.Debug("Audio language tags of '{0}' match the verified languages, nothing to retag", path);
                    return null;
            }

            if (!plan.Edits.Any())
            {
                // Every mismatched track was unmappable; remember why, but this is not "done".
                _logger.Info("No audio track of '{0}' can be retagged: {1}", path, string.Join("; ", plan.SkippedTracks.Select(t => $"a{t.StreamIndex}: {t.Reason}")));
                return Record(episodeFile, mode, AudioTrackRetagResult.Failed, plan, "no mismatched track has a language mkvpropedit can write");
            }

            if (!_diskProvider.FileExists(path))
            {
                _logger.Warn("Cannot retag audio tracks: '{0}' does not exist", path);
                return Record(episodeFile, mode, AudioTrackRetagResult.Failed, plan, "file not found");
            }

            if (plan.Remux)
            {
                // A remux writes a brand-new file (link count 1 by construction) and only ever deletes the
                // library's own directory entry, so the hardlink mode does not apply: a seeding hardlink of
                // the source keeps its bytes under the client's path whatever the mode says.
                return RemuxToMkv(episodeFile, series, path, mode, plan);
            }

            var hardlink = GetHardlinkState(path, hardlinkHint);

            if (mode == AudioTrackRetagHardlinkMode.Skip)
            {
                if (hardlink == HardlinkState.Linked)
                {
                    _logger.Info("'{0}' shares its bytes with another path (hardlink); audio tracks left as they are (hardlinked files: Skip)", path);
                    return Record(episodeFile, mode, AudioTrackRetagResult.SkippedHardlinked, plan, null);
                }

                if (hardlink == HardlinkState.Unknown)
                {
                    // Fail safe: without a link count we cannot promise the seed stays intact.
                    _logger.Info("'{0}' may be hardlinked (link count unavailable on this platform); audio tracks left as they are (hardlinked files: Skip)", path);
                    return Record(episodeFile, mode, AudioTrackRetagResult.SkippedHardlinked, plan, LinkCountUnavailable);
                }
            }

            try
            {
                if (hardlink != HardlinkState.NotLinked && mode == AudioTrackRetagHardlinkMode.CopyThenRetag)
                {
                    CopyThenRetag(path, plan);
                }
                else
                {
                    if (hardlink != HardlinkState.NotLinked)
                    {
                        _logger.Info("'{0}' is (or may be) hardlinked and will be retagged in place: every linked path (a seeding torrent included) changes with it", path);
                    }

                    _tagger.SetLanguages(path, plan.Edits, EditTimeout);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Audio track retag of '{0}' failed", path);
                return Record(episodeFile, mode, AudioTrackRetagResult.Failed, plan, ex.Message);
            }

            _logger.Info("Retagged audio tracks of '{0}': {1}", path, string.Join(", ", plan.Edits.Select(e => $"a{e.StreamIndex} {e.From ?? "und"} -> {e.To}")));

            return VerifyAndRecord(episodeFile, path, mode, plan);
        }

        /// <summary>Upper bound for one ffmpeg remux: 10 x the verification timeout, at least <see cref="RemuxTimeoutFloor"/>.</summary>
        public static TimeSpan RemuxTimeout(int verificationTimeoutSeconds)
        {
            var scaled = TimeSpan.FromSeconds(Math.Max(0, verificationTimeoutSeconds) * (long)RemuxTimeoutFactor);

            return scaled > RemuxTimeoutFloor ? scaled : RemuxTimeoutFloor;
        }

        /// <summary>Path the remuxed file takes: same directory and stem, .mkv extension.</summary>
        public static string RemuxTargetPath(string path)
        {
            return Path.ChangeExtension(path, AudioTrackRetagPlanner.MkvExtension);
        }

        /// <summary>Path of the temporary output written next to the source while ffmpeg runs.</summary>
        public static string RemuxTempPath(string path)
        {
            return Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, Path.GetFileNameWithoutExtension(path) + RemuxTempSuffix);
        }

        /// <summary>
        /// "Non-MKV files: Remux to MKV". ffmpeg stream-copies the source into a temporary .mkv in the same
        /// directory with the corrected audio languages, the result is checked with ffprobe (stream count,
        /// duration, tags), then the source is parked as .krzw-remux.bak, the temp moved to the .mkv path,
        /// the backup deleted, and the file record (path, size, media info, languages) updated. Any failure
        /// deletes the temp, restores the original from the backup when one was made, and records "failed".
        /// </summary>
        private AudioTrackRetag RemuxToMkv(EpisodeFile episodeFile, Series series, string path, AudioTrackRetagHardlinkMode mode, AudioTrackRetagPlan plan)
        {
            var originalContainer = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();

            if (series?.Path.IsNullOrWhiteSpace() != false)
            {
                return Record(episodeFile, mode, AudioTrackRetagResult.Failed, plan, "remux needs the series folder to compute the new relative path");
            }

            var targetPath = RemuxTargetPath(path);
            var tempPath = RemuxTempPath(path);

            if (_diskProvider.FileExists(targetPath))
            {
                _logger.Warn("Cannot remux '{0}': '{1}' already exists", path, targetPath);
                return Record(episodeFile, mode, AudioTrackRetagResult.Failed, plan, $"'{Path.GetFileName(targetPath)}' already exists next to the file");
            }

            var size = _diskProvider.GetFileSize(path);
            var folder = _diskProvider.GetParentFolder(path);
            var free = _diskProvider.GetAvailableSpace(folder);

            if (free.HasValue && free.Value < size)
            {
                _logger.Warn("Cannot remux '{0}': {1} free in '{2}', {3} needed", path, free.Value.SizeSuffix(), folder, size.SizeSuffix());
                return Record(episodeFile, mode, AudioTrackRetagResult.Failed, plan, $"not enough free space in '{folder}' to remux the file ({free.Value.SizeSuffix()} free, {size.SizeSuffix()} needed)");
            }

            var sourceInfo = episodeFile.MediaInfo?.RawStreamData.IsNotNullOrWhiteSpace() == true ? episodeFile.MediaInfo : _videoFileInfoReader.GetMediaInfo(path);
            var timeout = RemuxTimeout(_configService.AudioLanguageVerificationTimeout);
            var backupPath = path + RemuxBackupSuffix;
            var parked = false;
            MediaInfoModel newInfo;
            List<AudioTrackInfo> tracks;

            try
            {
                _logger.Info("Remuxing '{0}' ({1}) into '{2}' to write the audio language tags: {3}", path, originalContainer, Path.GetFileName(targetPath), string.Join(", ", plan.Edits.Select(e => $"a{e.StreamIndex} {e.From ?? "und"} -> {e.To}")));

                var map = _remuxer.Remux(path, tempPath, sourceInfo?.RawStreamData, plan.Edits, timeout);

                if (!_diskProvider.FileExists(tempPath))
                {
                    throw new AudioTrackRetagException("ffmpeg produced no output file");
                }

                newInfo = _videoFileInfoReader.GetMediaInfo(tempPath);

                if (newInfo == null)
                {
                    throw new AudioTrackRetagException("the remuxed file could not be probed");
                }

                var newCount = FfmpegMatroskaRemuxer.CountStreams(newInfo.RawStreamData);

                if (!map.MapAll && newCount >= 0 && newCount != map.Kept.Count)
                {
                    throw new AudioTrackRetagException($"the remuxed file has {newCount} stream(s), {map.Kept.Count} expected");
                }

                if (sourceInfo?.RunTime > TimeSpan.Zero && newInfo.RunTime > TimeSpan.Zero && (newInfo.RunTime - sourceInfo.RunTime).Duration() > RemuxDurationTolerance)
                {
                    throw new AudioTrackRetagException($"the remuxed file lasts {newInfo.RunTime} but the source {sourceInfo.RunTime}");
                }

                tracks = _layoutReader.Read(newInfo);
                var wrong = plan.Edits.Where(e => e.StreamIndex >= tracks.Count || !SameLanguage(tracks[e.StreamIndex].Language, e.To)).ToList();

                if (wrong.Any())
                {
                    throw new AudioTrackRetagException("after the remux the file still reports " + string.Join(", ", wrong.Select(e => $"a{e.StreamIndex}={(e.StreamIndex < tracks.Count ? tracks[e.StreamIndex].Language ?? "(none)" : "?")}")));
                }

                try
                {
                    _diskProvider.CopyPermissions(path, tempPath);
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Unable to copy permissions to '{0}'", tempPath);
                }

                _diskProvider.MoveFile(path, backupPath);
                parked = true;
                _diskProvider.MoveFile(tempPath, targetPath);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Remux of '{0}' failed; the original is left untouched", path);
                DeleteTemp(tempPath);

                if (parked)
                {
                    RestoreParked(path, backupPath);
                }

                return Record(episodeFile, mode, AudioTrackRetagResult.Failed, plan, $"remux failed: {ex.Message}");
            }

            try
            {
                _diskProvider.DeleteFile(backupPath);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Remux succeeded but the parked original '{0}' could not be deleted", backupPath);
            }

            var previousPath = path;
            var previousRelativePath = episodeFile.RelativePath;

            episodeFile.RelativePath = series.Path.GetRelativePath(targetPath);
            episodeFile.Path = targetPath;
            episodeFile.Size = _diskProvider.GetFileSize(targetPath);
            episodeFile.MediaInfo = newInfo;
            episodeFile.Languages = AudioTrackRetagPlanner.ReconcileLanguages(episodeFile.Languages, plan.Edits, episodeFile.AudioLanguageVerification, _configService.AudioLanguageVerificationConfidenceThreshold, tracks.Select(t => t.Language).ToList());

            _logger.Info("Remuxed '{0}' into '{1}' and retagged audio tracks: {2}", previousPath, targetPath, string.Join(", ", plan.Edits.Select(e => $"a{e.StreamIndex} {e.From ?? "und"} -> {e.To}")));

            var outcome = Record(episodeFile, mode, AudioTrackRetagResult.Done, plan, null, originalContainer);

            // the same events the renamer raises, so notifications, webhooks and media servers see the new path
            _eventAggregator.PublishEvent(new EpisodeFileRenamedEvent(series, episodeFile, previousPath));
            _eventAggregator.PublishEvent(new SeriesRenamedEvent(series, new List<RenamedEpisodeFile>
            {
                new RenamedEpisodeFile { EpisodeFile = episodeFile, PreviousPath = previousPath, PreviousRelativePath = previousRelativePath }
            }));

            return outcome;
        }

        private void RestoreParked(string path, string backupPath)
        {
            try
            {
                _diskProvider.MoveFile(backupPath, path);
            }
            catch (Exception restoreEx)
            {
                _logger.Error(restoreEx, "Unable to restore '{0}' from '{1}'; the original is still there under that name", path, backupPath);
            }
        }

        private void CopyThenRetag(string path, AudioTrackRetagPlan plan)
        {
            var size = _diskProvider.GetFileSize(path);
            var folder = _diskProvider.GetParentFolder(path);
            var free = _diskProvider.GetAvailableSpace(folder);

            if (free.HasValue && free.Value < size)
            {
                throw new AudioTrackRetagException($"not enough free space in '{folder}' to copy the file before retagging ({free.Value.SizeSuffix()} free, {size.SizeSuffix()} needed)");
            }

            var tempPath = path + TempSuffix;

            try
            {
                _logger.Debug("Copying hardlinked '{0}' to '{1}' before retagging so the seed keeps its bytes", path, tempPath);
                _diskProvider.CopyFile(path, tempPath, true);
                _tagger.SetLanguages(tempPath, plan.Edits, EditTimeout);

                try
                {
                    _diskProvider.CopyPermissions(path, tempPath);
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Unable to copy permissions to '{0}'", tempPath);
                }

                if (!_diskProvider.TryRenameFile(tempPath, path))
                {
                    SwapInto(path, tempPath);
                }
            }
            catch (Exception ex)
            {
                DeleteTemp(tempPath);
                throw new AudioTrackRetagException($"copy-then-retag failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Non-atomic fallback when rename(2) is unavailable: park the library file as .bak, move the
        /// retagged copy in, drop the .bak. A failure after the first move restores the original.
        /// </summary>
        private void SwapInto(string path, string tempPath)
        {
            var backupPath = path + BackupSuffix;

            _diskProvider.MoveFile(path, backupPath);

            try
            {
                _diskProvider.MoveFile(tempPath, path);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to move the retagged copy over '{0}', restoring the original", path);

                try
                {
                    _diskProvider.MoveFile(backupPath, path);
                }
                catch (Exception restoreEx)
                {
                    _logger.Error(restoreEx, "Unable to restore '{0}' from '{1}'; the original is still there under that name", path, backupPath);
                }

                throw;
            }

            try
            {
                _diskProvider.DeleteFile(backupPath);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Retag succeeded but the parked original '{0}' could not be deleted", backupPath);
            }
        }

        private void DeleteTemp(string tempPath)
        {
            try
            {
                if (_diskProvider.FileExists(tempPath))
                {
                    _diskProvider.DeleteFile(tempPath);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to delete temporary file '{0}'", tempPath);
            }
        }

        private enum HardlinkState
        {
            NotLinked,
            Linked,
            Unknown
        }

        private HardlinkState GetHardlinkState(string path, bool hint)
        {
            try
            {
                var links = _diskProvider.GetHardLinkCount(path);

                if (links > 1)
                {
                    return HardlinkState.Linked;
                }

                if (links == 1)
                {
                    return HardlinkState.NotLinked;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Unable to read the link count of '{0}', using the import transfer mode instead", path);
            }

            // 0 = the platform cannot tell; the import's transfer mode is the only remaining evidence
            return hint ? HardlinkState.Linked : HardlinkState.Unknown;
        }

        private AudioTrackRetag VerifyAndRecord(EpisodeFile episodeFile, string path, AudioTrackRetagHardlinkMode mode, AudioTrackRetagPlan plan)
        {
            var mediaInfo = _videoFileInfoReader.GetMediaInfo(path);

            if (mediaInfo == null)
            {
                return Record(episodeFile, mode, AudioTrackRetagResult.Failed, plan, "the file could not be probed after the edit");
            }

            // One entry per audio stream, untagged streams included, so the index matches the record's
            // audio-relative StreamIndex (MediaInfo.AudioLanguages drops empty tags and would shift it).
            var tracks = _layoutReader.Read(mediaInfo);
            var wrong = plan.Edits.Where(e => e.StreamIndex >= tracks.Count || !SameLanguage(tracks[e.StreamIndex].Language, e.To)).ToList();

            episodeFile.MediaInfo = mediaInfo;

            if (wrong.Any())
            {
                return Record(episodeFile, mode, AudioTrackRetagResult.Failed, plan, "after the edit the file still reports " + string.Join(", ", wrong.Select(e => $"a{e.StreamIndex}={(e.StreamIndex < tracks.Count ? tracks[e.StreamIndex].Language ?? "(none)" : "?")}")));
            }

            episodeFile.Languages = AudioTrackRetagPlanner.ReconcileLanguages(episodeFile.Languages, plan.Edits, episodeFile.AudioLanguageVerification, _configService.AudioLanguageVerificationConfidenceThreshold, tracks.Select(t => t.Language).ToList());

            return Record(episodeFile, mode, AudioTrackRetagResult.Done, plan, null);
        }

        private static bool SameLanguage(string tag, string expected)
        {
            return string.Equals(tag, expected, StringComparison.OrdinalIgnoreCase) ||
                   (tag.IsNotNullOrWhiteSpace() && IsoLanguages.Find(tag)?.Language is { } a && IsoLanguages.Find(expected)?.Language == a);
        }

        private AudioTrackRetag Record(EpisodeFile episodeFile, AudioTrackRetagHardlinkMode mode, string result, AudioTrackRetagPlan plan, string error, string remuxedFrom = null)
        {
            var outcome = new AudioTrackRetag
            {
                Mode = mode,
                Result = result,
                Tracks = plan.Edits.ToList(),
                SkippedTracks = plan.SkippedTracks.ToList(),
                At = DateTime.UtcNow,
                Error = error,
                Remuxed = remuxedFrom != null,
                OriginalContainer = remuxedFrom
            };

            episodeFile.AudioTrackRetag = outcome;
            _mediaFileService.Update(episodeFile);

            return outcome;
        }

        private static string LibraryPath(EpisodeFile episodeFile, Series series)
        {
            if (series?.Path.IsNotNullOrWhiteSpace() == true && episodeFile.RelativePath.IsNotNullOrWhiteSpace())
            {
                return Path.Combine(series.Path, episodeFile.RelativePath);
            }

            return episodeFile.Path;
        }
    }
}
