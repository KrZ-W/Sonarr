using System;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
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
    /// rescan / refresh / media-info update.
    /// </summary>
    public class AudioTrackRetagService : IAudioTrackRetagService,
                                          IHandle<EpisodeImportedEvent>,
                                          IExecute<RetagAudioTracksCommand>
    {
        public const string TempSuffix = ".krzw-retag.tmp";
        public static readonly TimeSpan EditTimeout = TimeSpan.FromMinutes(10);

        private readonly IConfigService _configService;
        private readonly IDiskProvider _diskProvider;
        private readonly IAudioTrackTagger _tagger;
        private readonly IVideoFileInfoReader _videoFileInfoReader;
        private readonly IMediaFileService _mediaFileService;
        private readonly ISeriesService _seriesService;
        private readonly Logger _logger;

        public AudioTrackRetagService(IConfigService configService,
                                      IDiskProvider diskProvider,
                                      IAudioTrackTagger tagger,
                                      IVideoFileInfoReader videoFileInfoReader,
                                      IMediaFileService mediaFileService,
                                      ISeriesService seriesService,
                                      Logger logger)
        {
            _configService = configService;
            _diskProvider = diskProvider;
            _tagger = tagger;
            _videoFileInfoReader = videoFileInfoReader;
            _mediaFileService = mediaFileService;
            _seriesService = seriesService;
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
            var plan = AudioTrackRetagPlanner.Plan(episodeFile, path, _configService.AudioLanguageVerificationConfidenceThreshold);
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

            var hardlinked = IsHardlinked(path, hardlinkHint);

            if (hardlinked && mode == AudioTrackRetagHardlinkMode.Skip)
            {
                _logger.Info("'{0}' shares its bytes with another path (hardlink); audio tracks left as they are (hardlinked files: Skip)", path);
                return Record(episodeFile, mode, AudioTrackRetagResult.SkippedHardlinked, plan, null);
            }

            try
            {
                if (hardlinked && mode == AudioTrackRetagHardlinkMode.CopyThenRetag)
                {
                    CopyThenRetag(path, plan);
                }
                else
                {
                    if (hardlinked)
                    {
                        _logger.Info("'{0}' is hardlinked and will be retagged in place: every linked path (a seeding torrent included) changes with it", path);
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
                    _diskProvider.MoveFile(tempPath, path, true);
                }
            }
            catch (Exception ex)
            {
                DeleteTemp(tempPath);
                throw new AudioTrackRetagException($"copy-then-retag failed: {ex.Message}", ex);
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

        private bool IsHardlinked(string path, bool hint)
        {
            try
            {
                var links = _diskProvider.GetHardLinkCount(path);

                if (links > 1)
                {
                    return true;
                }

                if (links == 1)
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Unable to read the link count of '{0}', using the import transfer mode instead", path);
            }

            return hint;
        }

        private AudioTrackRetag VerifyAndRecord(EpisodeFile episodeFile, string path, AudioTrackRetagHardlinkMode mode, AudioTrackRetagPlan plan)
        {
            var mediaInfo = _videoFileInfoReader.GetMediaInfo(path);

            if (mediaInfo == null)
            {
                return Record(episodeFile, mode, AudioTrackRetagResult.Failed, plan, "the file could not be probed after the edit");
            }

            var tags = mediaInfo.AudioLanguages ?? Enumerable.Empty<string>().ToList();
            var wrong = plan.Edits.Where(e => e.StreamIndex >= tags.Count || !SameLanguage(tags[e.StreamIndex], e.To)).ToList();

            episodeFile.MediaInfo = mediaInfo;

            var languages = AudioTrackRetagPlanner.LanguagesFromTags(tags);

            if (languages.Any())
            {
                episodeFile.Languages = languages;
            }

            if (wrong.Any())
            {
                return Record(episodeFile, mode, AudioTrackRetagResult.Failed, plan, "after the edit the file still reports " + string.Join(", ", wrong.Select(e => $"a{e.StreamIndex}={(e.StreamIndex < tags.Count ? tags[e.StreamIndex] : "?")}")));
            }

            return Record(episodeFile, mode, AudioTrackRetagResult.Done, plan, null);
        }

        private static bool SameLanguage(string tag, string expected)
        {
            return string.Equals(tag, expected, StringComparison.OrdinalIgnoreCase) ||
                   (tag.IsNotNullOrWhiteSpace() && IsoLanguages.Find(tag)?.Language is { } a && IsoLanguages.Find(expected)?.Language == a);
        }

        private AudioTrackRetag Record(EpisodeFile episodeFile, AudioTrackRetagHardlinkMode mode, string result, AudioTrackRetagPlan plan, string error)
        {
            var outcome = new AudioTrackRetag
            {
                Mode = mode,
                Result = result,
                Tracks = plan.Edits.ToList(),
                SkippedTracks = plan.SkippedTracks.ToList(),
                At = DateTime.UtcNow,
                Error = error
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
