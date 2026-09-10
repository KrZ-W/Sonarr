using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles.AudioLanguage;
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
        /// ISO 639-2 terminology (T) to bibliographic (B) codes: the 20 languages that have two
        /// codes. Matroska's Language element uses the B form ("fre", "ger"). Explicit and
        /// deterministic on purpose: FileNameBuilder.Iso639BTMap maps several B keys to one T
        /// value (ger and gsw both to deu) and a reverse lookup of it is hash-ordered.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> MatroskaBibliographicCodes = new Dictionary<string, string>
        {
            { "bod", "tib" },
            { "ces", "cze" },
            { "cym", "wel" },
            { "deu", "ger" },
            { "ell", "gre" },
            { "eus", "baq" },
            { "fas", "per" },
            { "fra", "fre" },
            { "hye", "arm" },
            { "isl", "ice" },
            { "kat", "geo" },
            { "mkd", "mac" },
            { "mri", "mao" },
            { "msa", "may" },
            { "mya", "bur" },
            { "nld", "dut" },
            { "ron", "rum" },
            { "slk", "slo" },
            { "sqi", "alb" },
            { "zho", "chi" }
        };

        /// <summary>
        /// The ISO 639-2 code Matroska expects (bibliographic form, e.g. "fre" for French, the
        /// terminology code itself when the language has no B form), or null when the language
        /// has no ISO entry.
        /// </summary>
        public static string Iso6392Code(Language language)
        {
            var iso = IsoLanguages.Get(language);

            if (iso?.ThreeLetterCode == null)
            {
                return null;
            }

            return MatroskaBibliographicCodes.TryGetValue(iso.ThreeLetterCode, out var bibliographic) ? bibliographic : iso.ThreeLetterCode;
        }

        /// <summary>Language a track tag means; Unknown for null/empty/"und" or an unmapped tag.</summary>
        public static Language TaggedLanguage(string tag)
        {
            if (tag.IsNullOrWhiteSpace() || tag.Equals("und", StringComparison.OrdinalIgnoreCase))
            {
                return Language.Unknown;
            }

            return IsoLanguages.Find(tag)?.Language ?? Language.Unknown;
        }

        /// <summary>
        /// Languages of the file after a retag: the Languages the import stored, with each rewritten
        /// track now counted as its detected language. A rewritten track's old tag language is dropped
        /// only when no track still carries it; Unknown is never added; a track that could not be
        /// written keeps whatever the import decided (the record says why it was not written).
        /// </summary>
        public static List<Language> ReconcileLanguages(List<Language> stored, IReadOnlyList<AudioTrackRetagTrack> rewritten, IEnumerable<AudioLanguageVerification> verification, double confidenceThreshold, IReadOnlyList<string> streamTags)
        {
            var result = (stored ?? new List<Language>()).Distinct().ToList();
            var rewrittenIndexes = rewritten.Select(e => e.StreamIndex).ToHashSet();

            // what every track means now: detected for rewritten tracks, the import's view for the rest
            var current = new Dictionary<int, Language>();

            foreach (var track in verification ?? Enumerable.Empty<AudioLanguageVerification>())
            {
                var detected = track.DetectedLanguage.IsNotNullOrWhiteSpace() ? IsoLanguages.Find(track.DetectedLanguage)?.Language : null;

                if (rewrittenIndexes.Contains(track.StreamIndex))
                {
                    current[track.StreamIndex] = detected ?? Language.Unknown;
                }
                else if (detected != null && track.Confidence >= confidenceThreshold)
                {
                    current[track.StreamIndex] = detected;
                }
                else
                {
                    current[track.StreamIndex] = TaggedLanguage(track.TaggedLanguage);
                }
            }

            // streams the record does not cover (not probed by the trigger that fired) keep their tag
            for (var i = 0; i < (streamTags?.Count ?? 0); i++)
            {
                if (!current.ContainsKey(i))
                {
                    current[i] = TaggedLanguage(streamTags[i]);
                }
            }

            foreach (var edit in rewritten)
            {
                if (current.TryGetValue(edit.StreamIndex, out var detected) && detected != Language.Unknown && !result.Contains(detected))
                {
                    result.Add(detected);
                }

                var oldTag = TaggedLanguage(edit.From);

                if (oldTag != Language.Unknown && !current.ContainsValue(oldTag))
                {
                    result.Remove(oldTag);
                }
            }

            return result.Any() ? result : (stored ?? new List<Language>());
        }
    }
}
