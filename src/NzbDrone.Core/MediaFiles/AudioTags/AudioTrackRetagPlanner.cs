using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.MediaFiles.AudioTags
{
    /// <summary>
    /// Pure decision logic: given a episode file and the verification threshold, which audio
    /// tracks get a new language tag. Only tracks whose detected language (at or above the
    /// threshold) maps to a different Sonarr language than their tag are edited; tracks below
    /// the threshold, without a detection, or whose detected language has no ISO 639-2 code
    /// are left as they are.
    /// </summary>
    public static class AudioTrackRetagPlanner
    {
        public const string MkvExtension = ".mkv";

        public static AudioTrackRetagPlan Plan(EpisodeFile episodeFile, string path, double confidenceThreshold)
        {
            if (episodeFile.AudioTrackRetag?.IsDone == true)
            {
                return AudioTrackRetagPlan.Skip(AudioTrackRetagSkipReason.AlreadyDone);
            }

            if (!IsMkv(path))
            {
                return AudioTrackRetagPlan.Skip(AudioTrackRetagSkipReason.NotMkv);
            }

            var verification = episodeFile.AudioLanguageVerification;

            if (verification == null || !verification.Any())
            {
                return AudioTrackRetagPlan.Skip(AudioTrackRetagSkipReason.NoVerificationRecord);
            }

            var plan = new AudioTrackRetagPlan();

            foreach (var track in verification.OrderBy(t => t.StreamIndex))
            {
                if (track.DetectedLanguage.IsNullOrWhiteSpace() || track.Confidence < confidenceThreshold)
                {
                    continue;
                }

                var detected = IsoLanguages.Find(track.DetectedLanguage)?.Language;

                if (detected == null || detected == Language.Unknown)
                {
                    plan.SkippedTracks.Add(new AudioTrackRetagSkippedTrack
                    {
                        StreamIndex = track.StreamIndex,
                        Reason = $"detected language '{track.DetectedLanguage}' is not a known language"
                    });
                    continue;
                }

                var tagged = TaggedLanguage(track.TaggedLanguage);

                if (tagged == detected)
                {
                    continue;
                }

                var code = Iso6392Code(detected);

                if (code.IsNullOrWhiteSpace())
                {
                    plan.SkippedTracks.Add(new AudioTrackRetagSkippedTrack
                    {
                        StreamIndex = track.StreamIndex,
                        Reason = $"{detected.Name} has no ISO 639-2 code"
                    });
                    continue;
                }

                plan.Edits.Add(new AudioTrackRetagTrack
                {
                    StreamIndex = track.StreamIndex,
                    From = track.TaggedLanguage,
                    To = code
                });
            }

            if (!plan.Edits.Any() && !plan.SkippedTracks.Any())
            {
                return AudioTrackRetagPlan.Skip(AudioTrackRetagSkipReason.NoMismatch);
            }

            return plan;
        }

        public static bool IsMkv(string path)
        {
            return path.IsNotNullOrWhiteSpace() &&
                   string.Equals(Path.GetExtension(path), MkvExtension, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The ISO 639-2 code Matroska expects (bibliographic form, e.g. "fre" for French),
        /// or null when the language has no ISO entry.
        /// </summary>
        public static string Iso6392Code(Language language)
        {
            var iso = IsoLanguages.Get(language);

            if (iso?.ThreeLetterCode == null)
            {
                return null;
            }

            var bibliographic = FileNameBuilder.Iso639BTMap.FirstOrDefault(kv => kv.Value == iso.ThreeLetterCode).Key;

            return bibliographic ?? iso.ThreeLetterCode;
        }

        private static Language TaggedLanguage(string tag)
        {
            if (tag.IsNullOrWhiteSpace() || tag.Equals("und", StringComparison.OrdinalIgnoreCase))
            {
                return Language.Unknown;
            }

            return IsoLanguages.Find(tag)?.Language ?? Language.Unknown;
        }

        /// <summary>Languages of the file as its (re-probed) tags say, one per distinct audio language, Unknown for unmapped tags.</summary>
        public static List<Language> LanguagesFromTags(IEnumerable<string> audioLanguageTags)
        {
            var languages = new List<Language>();

            foreach (var tag in audioLanguageTags ?? Enumerable.Empty<string>())
            {
                var language = TaggedLanguage(tag);

                if (!languages.Contains(language))
                {
                    languages.Add(language);
                }
            }

            return languages;
        }
    }
}
