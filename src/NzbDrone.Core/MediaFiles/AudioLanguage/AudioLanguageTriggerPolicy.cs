using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Languages;

namespace NzbDrone.Core.MediaFiles.AudioLanguage
{
    public class AudioLanguageTriggerInput
    {
        public bool Enabled { get; set; }
        public string Endpoint { get; set; }

        /// <summary>Union of languages parsed from file name, folder, download client item and the grab history row.</summary>
        public List<Language> ClaimedLanguages { get; set; } = new List<Language>();

        /// <summary>MediaInfo audio tracks (evidence).</summary>
        public List<AudioTrackInfo> Tracks { get; set; } = new List<AudioTrackInfo>();

        /// <summary>The series' original language, used to resolve "Original" in custom format conditions.</summary>
        public Language OriginalLanguage { get; set; } = Language.Unknown;

        /// <summary>True when any custom format used by the profile has a Language condition. Sonarr has no profile Language field, so this is the whole expectation.</summary>
        public bool HasLanguageCustomFormat { get; set; }

        /// <summary>Languages wanted by positively scored, non-negated Language conditions in the profile's formats.</summary>
        public List<Language> CustomFormatLanguages { get; set; } = new List<Language>();

        /// <summary>Caller-evaluated: with the current tags a language CF would drop the file below MinFormatScore.</summary>
        public bool ImpendingRejection { get; set; }

        public string ReleaseGroup { get; set; }
        public AudioLanguageVerifyTaggedMode VerifyTaggedMode { get; set; }
        public string VerifyTaggedReleaseGroups { get; set; }
    }

    public class AudioLanguageTriggerDecision
    {
        public static readonly AudioLanguageTriggerDecision None = new AudioLanguageTriggerDecision(AudioLanguageTrigger.None, new List<int>());

        public AudioLanguageTriggerDecision(AudioLanguageTrigger trigger, List<int> streamIndexes)
        {
            Trigger = trigger;
            StreamIndexes = streamIndexes;
        }

        public AudioLanguageTrigger Trigger { get; }
        public List<int> StreamIndexes { get; }
        public bool ShouldProbe => Trigger != AudioLanguageTrigger.None && StreamIndexes.Count > 0;
    }

    /// <summary>
    /// Pure decision: which audio tracks (if any) to send to the language detector and why.
    /// No I/O, no configuration lookups; everything comes in through the input.
    /// </summary>
    public class AudioLanguageTriggerPolicy
    {
        public AudioLanguageTriggerDecision Evaluate(AudioLanguageTriggerInput input)
        {
            if (!input.Enabled || input.Endpoint.IsNullOrWhiteSpace())
            {
                return AudioLanguageTriggerDecision.None;
            }

            // Fast path: nothing in the profile cares about language (Sonarr profiles have no Language field).
            if (!input.HasLanguageCustomFormat)
            {
                return AudioLanguageTriggerDecision.None;
            }

            if (input.Tracks.Empty())
            {
                return AudioLanguageTriggerDecision.None;
            }

            var expected = ExpectedLanguages(input);

            // a. Contradiction: a claimed language that no track is tagged with.
            var claimed = input.ClaimedLanguages
                .Where(IsConcrete)
                .Distinct()
                .ToList();

            if (claimed.Any(l => input.Tracks.All(t => t.TaggedLanguage != l)))
            {
                // No track is tagged with the missing language, so every track is a candidate.
                return new AudioLanguageTriggerDecision(AudioLanguageTrigger.Contradiction, input.Tracks.Select(t => t.StreamIndex).ToList());
            }

            // b. Unknown: und / untagged tracks.
            var unknown = input.Tracks
                .Where(t => t.TaggedLanguage == null)
                .Select(t => t.StreamIndex)
                .ToList();

            if (unknown.Any())
            {
                return new AudioLanguageTriggerDecision(AudioLanguageTrigger.Unknown, unknown);
            }

            // c. Impending rejection: probe everything not already tagged with an accepted language.
            if (input.ImpendingRejection)
            {
                var notAccepted = input.Tracks
                    .Where(t => !expected.Contains(t.TaggedLanguage))
                    .Select(t => t.StreamIndex)
                    .ToList();

                if (notAccepted.Any())
                {
                    return new AudioLanguageTriggerDecision(AudioLanguageTrigger.ImpendingRejection, notAccepted);
                }
            }

            // d. Positive verification of tracks tagged with an expected language, per setting.
            if (ShouldVerifyTagged(input))
            {
                var tagged = input.Tracks
                    .Where(t => t.TaggedLanguage != null && expected.Contains(t.TaggedLanguage))
                    .Select(t => t.StreamIndex)
                    .ToList();

                if (tagged.Any())
                {
                    return new AudioLanguageTriggerDecision(AudioLanguageTrigger.PositiveVerification, tagged);
                }
            }

            return AudioLanguageTriggerDecision.None;
        }

        /// <summary>The languages positive language CFs ask for, with "Original" resolved through the series.</summary>
        public static List<Language> ExpectedLanguages(AudioLanguageTriggerInput input)
        {
            var expected = new List<Language>();

            foreach (var language in input.CustomFormatLanguages ?? new List<Language>())
            {
                if (language == Language.Original)
                {
                    if (input.OriginalLanguage != null && input.OriginalLanguage != Language.Unknown)
                    {
                        expected.Add(input.OriginalLanguage);
                    }
                }
                else if (IsConcrete(language))
                {
                    expected.Add(language);
                }
            }

            return expected.Distinct().ToList();
        }

        private static bool IsConcrete(Language language)
        {
            return language != null &&
                   language != Language.Unknown &&
                   language != Language.Original;
        }

        private static bool ShouldVerifyTagged(AudioLanguageTriggerInput input)
        {
            switch (input.VerifyTaggedMode)
            {
                case AudioLanguageVerifyTaggedMode.Always:
                    return true;
                case AudioLanguageVerifyTaggedMode.ForReleaseGroups:
                    if (input.ReleaseGroup.IsNullOrWhiteSpace() || input.VerifyTaggedReleaseGroups.IsNullOrWhiteSpace())
                    {
                        return false;
                    }

                    return input.VerifyTaggedReleaseGroups
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(g => g.Trim())
                        .Any(g => g.Equals(input.ReleaseGroup, StringComparison.OrdinalIgnoreCase));
                default:
                    return false;
            }
        }
    }
}
