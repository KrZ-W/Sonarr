using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Download;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles.AudioLanguage;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.EpisodeImport.Aggregation.Aggregators.Augmenters.Language
{
    // krzw(audio-language-verification): listens to the audio when the tags and the claim disagree
    // (or the tags would get the file rejected) and, when the detector is confident, outranks MediaInfo.
    public class AugmentLanguageFromAudioProbe : IAugmentLanguage
    {
        public int Order => 5;
        public string Name => "AudioProbe";

        private readonly IConfigService _configService;
        private readonly IAudioTrackLayoutReader _layoutReader;
        private readonly IAudioLanguageProbe _probe;
        private readonly IAudioLanguageProbeCache _cache;
        private readonly IHistoryService _historyService;
        private readonly ICustomFormatCalculationService _formatCalculator;
        private readonly AudioLanguageTriggerPolicy _policy;
        private readonly Logger _logger;

        public AugmentLanguageFromAudioProbe(IConfigService configService,
                                             IAudioTrackLayoutReader layoutReader,
                                             IAudioLanguageProbe probe,
                                             IAudioLanguageProbeCache cache,
                                             IHistoryService historyService,
                                             ICustomFormatCalculationService formatCalculator,
                                             Logger logger)
        {
            _configService = configService;
            _layoutReader = layoutReader;
            _probe = probe;
            _cache = cache;
            _historyService = historyService;
            _formatCalculator = formatCalculator;
            _policy = new AudioLanguageTriggerPolicy();
            _logger = logger;
        }

        public AugmentLanguageResult AugmentLanguage(LocalEpisode localEpisode, DownloadClientItem downloadClientItem)
        {
            // Nothing in here may ever reach the aggregator: any failure means "no extra evidence".
            try
            {
                return Augment(localEpisode, downloadClientItem);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Audio language verification failed for '{0}', importing on existing evidence", localEpisode.Path);
                return null;
            }
        }

        private AugmentLanguageResult Augment(LocalEpisode localEpisode, DownloadClientItem downloadClientItem)
        {
            if (!_configService.AudioLanguageVerificationEnabled || _configService.AudioLanguageVerificationEndpoint.IsNullOrWhiteSpace())
            {
                return null;
            }

            // Verification is an import-time decision: files already in the library (rescan, DB rebuild)
            // are never probed and never get their stored record rewritten.
            if (localEpisode.ExistingFile || localEpisode.MediaInfo == null || localEpisode.Series?.QualityProfile?.Value == null)
            {
                return null;
            }

            var tracks = _layoutReader.Read(localEpisode.MediaInfo);

            if (tracks.Empty())
            {
                return null;
            }

            var profile = localEpisode.Series.QualityProfile.Value;
            var originalLanguage = localEpisode.Series.OriginalLanguage ?? Languages.Language.Unknown;
            var evidence = tracks.Select(t => t.TaggedLanguage).Where(l => l != null).Distinct().ToList();
            var formatLanguages = GetFormatLanguages(profile, out var hasLanguageFormat);

            var input = new AudioLanguageTriggerInput
            {
                Enabled = true,
                Endpoint = _configService.AudioLanguageVerificationEndpoint,
                ClaimedLanguages = GetClaimedLanguages(localEpisode, downloadClientItem),
                Tracks = tracks,
                OriginalLanguage = originalLanguage,
                HasLanguageCustomFormat = hasLanguageFormat,
                CustomFormatLanguages = formatLanguages,
                ImpendingRejection = hasLanguageFormat && WouldBeRejected(localEpisode, evidence),
                ReleaseGroup = GetReleaseGroup(localEpisode),
                VerifyTaggedMode = _configService.AudioLanguageVerificationVerifyTagged,
                VerifyTaggedReleaseGroups = _configService.AudioLanguageVerificationVerifyTaggedGroups
            };

            var decision = _policy.Evaluate(input);

            if (!decision.ShouldProbe)
            {
                return null;
            }

            // Season packs: every episode of the download shares the pack key, so one probe per layout serves them all.
            var packKey = downloadClientItem?.DownloadId.IsNotNullOrWhiteSpace() == true
                ? downloadClientItem.DownloadId
                : localEpisode.Path.GetParentPath() ?? localEpisode.Path;
            var signature = AudioTrackLayout.Signature(tracks);
            var threshold = _configService.AudioLanguageVerificationConfidenceThreshold;
            var verifications = new List<AudioLanguageVerification>();

            _logger.Debug("Audio language verification triggered ({0}) for '{1}', probing audio stream(s) {2}", decision.Trigger, localEpisode.Path, string.Join(", ", decision.StreamIndexes));

            foreach (var streamIndex in decision.StreamIndexes)
            {
                var track = tracks.First(t => t.StreamIndex == streamIndex);

                if (!_cache.TryGet(packKey, signature, streamIndex, out var result))
                {
                    result = _probe.Probe(localEpisode.Path, streamIndex, localEpisode.MediaInfo.RunTime);
                    _cache.Set(packKey, signature, streamIndex, result);
                }
                else
                {
                    _logger.Debug("Reusing the audio language probe of an earlier file with the same track layout for stream {0}", streamIndex);
                }

                verifications.Add(new AudioLanguageVerification
                {
                    StreamIndex = streamIndex,
                    TaggedLanguage = track.Language,
                    DetectedLanguage = result?.LanguageCode,
                    Confidence = result?.Confidence ?? 0,
                    Source = AudioLanguageVerification.WhisperSource,
                    ProbedAt = DateTime.UtcNow
                });
            }

            localEpisode.AudioLanguageTrigger = decision.Trigger;
            localEpisode.AudioLanguageVerification = verifications;

            var verified = verifications
                .Where(v => v.DetectedLanguage != null && v.Confidence >= threshold)
                .ToDictionary(v => v.StreamIndex, v => IsoLanguages.Find(v.DetectedLanguage)?.Language);

            if (verified.Values.All(l => l == null || l == Languages.Language.Unknown))
            {
                _logger.Debug("No audio language probe of '{0}' reached the confidence threshold {1:0.00}, keeping the tagged languages", localEpisode.Path, threshold);
                return null;
            }

            // Verified tracks take the detected language; the others keep their tag.
            var languages = new List<Languages.Language>();

            foreach (var track in tracks)
            {
                if (verified.TryGetValue(track.StreamIndex, out var detected) && detected != null && detected != Languages.Language.Unknown)
                {
                    languages.AddIfNotNull(detected);
                }
                else
                {
                    languages.AddIfNotNull(track.TaggedLanguage);
                }
            }

            languages = languages.Distinct().ToList();

            if (languages.Empty())
            {
                return null;
            }

            return new AugmentLanguageResult(languages, Confidence.AudioProbe);
        }

        private List<Languages.Language> GetClaimedLanguages(LocalEpisode localEpisode, DownloadClientItem downloadClientItem)
        {
            var claimed = new List<Languages.Language>();

            claimed.AddRange(localEpisode.FileEpisodeInfo?.Languages ?? new List<Languages.Language>());
            claimed.AddRange(localEpisode.FolderEpisodeInfo?.Languages ?? new List<Languages.Language>());
            claimed.AddRange(localEpisode.DownloadClientEpisodeInfo?.Languages ?? new List<Languages.Language>());

            if (downloadClientItem?.DownloadId.IsNotNullOrWhiteSpace() == true)
            {
                var grab = _historyService.FindByDownloadId(downloadClientItem.DownloadId)
                    .Where(h => h.EventType == EpisodeHistoryEventType.Grabbed)
                    .MaxBy(h => h.Date);

                claimed.AddRange(grab?.Languages ?? new List<Languages.Language>());
            }

            return claimed.Distinct().ToList();
        }

        private static List<Languages.Language> GetFormatLanguages(Profiles.Qualities.QualityProfile profile, out bool hasLanguageFormat)
        {
            hasLanguageFormat = false;
            var languages = new List<Languages.Language>();

            foreach (var item in profile.FormatItems ?? new List<Profiles.ProfileFormatItem>())
            {
                var specs = item.Format?.Specifications?.OfType<CustomFormats.LanguageSpecification>().ToList();

                if (specs == null || specs.Empty())
                {
                    continue;
                }

                hasLanguageFormat = true;

                if (item.Score <= 0)
                {
                    continue;
                }

                foreach (var spec in specs.Where(s => !s.Negate && !s.ExceptLanguage))
                {
                    languages.Add((Languages.Language)spec.Value);
                }
            }

            return languages.Distinct().ToList();
        }

        /// <summary>Mirrors the import-time MinimumCustomFormatScoreSpecification on the tagged languages only.</summary>
        private bool WouldBeRejected(LocalEpisode localEpisode, List<Languages.Language> evidence)
        {
            var profile = localEpisode.Series.QualityProfile.Value;
            var previous = localEpisode.Languages;

            try
            {
                localEpisode.Languages = evidence;
                var formats = _formatCalculator.ParseCustomFormat(localEpisode);
                var score = profile.CalculateCustomFormatScore(formats);

                return score < profile.MinFormatScore;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Unable to pre-compute the custom format score for '{0}'", localEpisode.Path);
                return false;
            }
            finally
            {
                localEpisode.Languages = previous;
            }
        }

        private static string GetReleaseGroup(LocalEpisode localEpisode)
        {
            return localEpisode.ReleaseGroup.IsNotNullOrWhiteSpace()
                ? localEpisode.ReleaseGroup
                : localEpisode.FileEpisodeInfo?.ReleaseGroup ?? localEpisode.FolderEpisodeInfo?.ReleaseGroup ?? localEpisode.DownloadClientEpisodeInfo?.ReleaseGroup;
        }
    }
}
