using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles.AudioLanguage;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.AudioLanguage
{
    [TestFixture]
    public class AudioLanguageTriggerPolicyFixture : CoreTest
    {
        private AudioLanguageTriggerPolicy _policy;
        private AudioLanguageTriggerInput _input;

        [SetUp]
        public void Setup()
        {
            _policy = new AudioLanguageTriggerPolicy();
            _input = new AudioLanguageTriggerInput
            {
                Enabled = true,
                Endpoint = "http://whisper:9000",
                OriginalLanguage = Language.English,
                HasLanguageCustomFormat = true,
                CustomFormatLanguages = new List<Language> { Language.French },
                Tracks = new List<AudioTrackInfo>
                {
                    Track(0, "eng"),
                    Track(1, "fra")
                }
            };
        }

        private static AudioTrackInfo Track(int index, string language, string title = null)
        {
            return new AudioTrackInfo { StreamIndex = index, Codec = "aac", Channels = 2, Language = language, Title = title };
        }

        [Test]
        public void should_not_probe_when_disabled()
        {
            _input.Enabled = false;
            _input.ClaimedLanguages = new List<Language> { Language.French };
            _input.Tracks = new List<AudioTrackInfo> { Track(0, "eng") };

            _policy.Evaluate(_input).Trigger.Should().Be(AudioLanguageTrigger.None);
        }

        [Test]
        public void should_not_probe_without_endpoint()
        {
            _input.Endpoint = " ";
            _input.Tracks = new List<AudioTrackInfo> { Track(0, "und") };

            _policy.Evaluate(_input).Trigger.Should().Be(AudioLanguageTrigger.None);
        }

        [Test]
        public void should_take_fast_path_when_no_custom_format_has_a_language_condition()
        {
            _input.HasLanguageCustomFormat = false;
            _input.CustomFormatLanguages = new List<Language>();
            _input.ClaimedLanguages = new List<Language> { Language.French };
            _input.Tracks = new List<AudioTrackInfo> { Track(0, "und"), Track(1, "eng") };
            _input.ImpendingRejection = true;
            _input.VerifyTaggedMode = AudioLanguageVerifyTaggedMode.Always;

            _policy.Evaluate(_input).Trigger.Should().Be(AudioLanguageTrigger.None);
        }

        [Test]
        public void should_evaluate_when_a_language_format_exists_even_without_positive_languages()
        {
            _input.CustomFormatLanguages = new List<Language>();
            _input.Tracks = new List<AudioTrackInfo> { Track(0, "und") };

            _policy.Evaluate(_input).Trigger.Should().Be(AudioLanguageTrigger.Unknown);
        }

        [Test]
        public void should_not_probe_without_tracks()
        {
            _input.Tracks = new List<AudioTrackInfo>();
            _input.ImpendingRejection = true;

            _policy.Evaluate(_input).ShouldProbe.Should().BeFalse();
        }

        [Test]
        public void contradiction_should_probe_every_track_not_tagged_with_the_claimed_language()
        {
            _input.ClaimedLanguages = new List<Language> { Language.French };
            _input.Tracks = new List<AudioTrackInfo> { Track(0, "eng"), Track(1, "eng") };

            var decision = _policy.Evaluate(_input);

            decision.Trigger.Should().Be(AudioLanguageTrigger.Contradiction);
            decision.StreamIndexes.Should().BeEquivalentTo(new[] { 0, 1 });
        }

        [Test]
        public void contradiction_should_not_fire_when_a_track_carries_the_claimed_language()
        {
            _input.ClaimedLanguages = new List<Language> { Language.French, Language.English };

            _policy.Evaluate(_input).Trigger.Should().Be(AudioLanguageTrigger.None);
        }

        [Test]
        public void contradiction_should_ignore_original_and_unknown_claims()
        {
            _input.ClaimedLanguages = new List<Language> { Language.Original, Language.Unknown };

            _policy.Evaluate(_input).Trigger.Should().Be(AudioLanguageTrigger.None);
        }

        [Test]
        public void contradiction_should_win_over_unknown()
        {
            _input.ClaimedLanguages = new List<Language> { Language.French };
            _input.Tracks = new List<AudioTrackInfo> { Track(0, "eng"), Track(1, "und") };

            var decision = _policy.Evaluate(_input);

            decision.Trigger.Should().Be(AudioLanguageTrigger.Contradiction);
            decision.StreamIndexes.Should().BeEquivalentTo(new[] { 0, 1 });
        }

        [Test]
        public void unknown_should_probe_only_und_and_untagged_tracks()
        {
            _input.Tracks = new List<AudioTrackInfo> { Track(0, "fra"), Track(1, "und"), Track(2, null), Track(3, "") };

            var decision = _policy.Evaluate(_input);

            decision.Trigger.Should().Be(AudioLanguageTrigger.Unknown);
            decision.StreamIndexes.Should().BeEquivalentTo(new[] { 1, 2, 3 });
        }

        [Test]
        public void impending_rejection_should_probe_tracks_not_tagged_with_an_expected_language()
        {
            _input.Tracks = new List<AudioTrackInfo> { Track(0, "eng"), Track(1, "ger") };
            _input.ImpendingRejection = true;

            var decision = _policy.Evaluate(_input);

            decision.Trigger.Should().Be(AudioLanguageTrigger.ImpendingRejection);
            decision.StreamIndexes.Should().BeEquivalentTo(new[] { 0, 1 });
        }

        [Test]
        public void impending_rejection_should_skip_tracks_tagged_with_a_custom_format_language()
        {
            _input.Tracks = new List<AudioTrackInfo> { Track(0, "eng"), Track(1, "fra") };
            _input.ImpendingRejection = true;

            var decision = _policy.Evaluate(_input);

            decision.Trigger.Should().Be(AudioLanguageTrigger.ImpendingRejection);
            decision.StreamIndexes.Should().BeEquivalentTo(new[] { 0 });
        }

        [Test]
        public void impending_rejection_should_resolve_original_language_through_the_series()
        {
            _input.CustomFormatLanguages = new List<Language> { Language.Original };
            _input.OriginalLanguage = Language.German;
            _input.Tracks = new List<AudioTrackInfo> { Track(0, "eng"), Track(1, "ger") };
            _input.ImpendingRejection = true;

            var decision = _policy.Evaluate(_input);

            decision.Trigger.Should().Be(AudioLanguageTrigger.ImpendingRejection);
            decision.StreamIndexes.Should().BeEquivalentTo(new[] { 0 });
        }

        [Test]
        public void should_not_probe_when_nothing_is_wrong_and_verify_tagged_is_never()
        {
            _input.VerifyTaggedMode = AudioLanguageVerifyTaggedMode.Never;

            _policy.Evaluate(_input).Trigger.Should().Be(AudioLanguageTrigger.None);
        }

        [Test]
        public void positive_verification_always_should_probe_only_expected_tagged_tracks()
        {
            _input.VerifyTaggedMode = AudioLanguageVerifyTaggedMode.Always;

            var decision = _policy.Evaluate(_input);

            decision.Trigger.Should().Be(AudioLanguageTrigger.PositiveVerification);
            decision.StreamIndexes.Should().BeEquivalentTo(new[] { 1 });
        }

        [TestCase("QxR", "qxr,FGT", true)]
        [TestCase("FGT", " qxr , fgt ", true)]
        [TestCase("SPARKS", "qxr,FGT", false)]
        [TestCase(null, "qxr", false)]
        [TestCase("QxR", "", false)]
        public void positive_verification_for_release_groups_should_match_csv_case_insensitively(string group, string csv, bool expected)
        {
            _input.VerifyTaggedMode = AudioLanguageVerifyTaggedMode.ForReleaseGroups;
            _input.ReleaseGroup = group;
            _input.VerifyTaggedReleaseGroups = csv;

            _policy.Evaluate(_input).ShouldProbe.Should().Be(expected);
        }

        [Test]
        public void expected_languages_should_resolve_original_and_drop_unknown()
        {
            _input.OriginalLanguage = Language.English;
            _input.CustomFormatLanguages = new List<Language> { Language.French, Language.Original, Language.Unknown };

            AudioLanguageTriggerPolicy.ExpectedLanguages(_input).Should().BeEquivalentTo(new[] { Language.English, Language.French });
        }
    }
}
