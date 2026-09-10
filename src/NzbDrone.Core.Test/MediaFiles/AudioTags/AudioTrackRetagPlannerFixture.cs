using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.AudioLanguage;
using NzbDrone.Core.MediaFiles.AudioTags;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.AudioTags
{
    [TestFixture]
    public class AudioTrackRetagPlannerFixture : CoreTest
    {
        private const string Mkv = "/tv/Series/Season 01/Series - S01E01.mkv";
        private const double Threshold = 0.85;

        private static AudioLanguageVerification Track(int index, string tagged, string detected, double confidence = 0.99)
        {
            return new AudioLanguageVerification
            {
                StreamIndex = index,
                TaggedLanguage = tagged,
                DetectedLanguage = detected,
                Confidence = confidence,
                ProbedAt = DateTime.UtcNow
            };
        }

        private static EpisodeFile File(params AudioLanguageVerification[] tracks)
        {
            return new EpisodeFile { AudioLanguageVerification = tracks.Length == 0 ? null : new List<AudioLanguageVerification>(tracks) };
        }

        [Test]
        public void should_edit_mismatched_track_above_threshold()
        {
            var plan = AudioTrackRetagPlanner.Plan(File(Track(0, "eng", "fr", 0.99)), Mkv, Threshold);

            plan.ShouldRetag.Should().BeTrue();
            plan.Edits.Should().HaveCount(1);
            plan.Edits[0].StreamIndex.Should().Be(0);
            plan.Edits[0].From.Should().Be("eng");
            plan.Edits[0].To.Should().Be("fre");
        }

        [Test]
        public void should_skip_track_below_threshold()
        {
            var plan = AudioTrackRetagPlanner.Plan(File(Track(0, "eng", "fr", 0.5)), Mkv, Threshold);

            plan.SkipReason.Should().Be(AudioTrackRetagSkipReason.NoMismatch);
            plan.ShouldRetag.Should().BeFalse();
        }

        [Test]
        public void should_accept_confidence_equal_to_threshold()
        {
            AudioTrackRetagPlanner.Plan(File(Track(0, "eng", "fr", Threshold)), Mkv, Threshold).ShouldRetag.Should().BeTrue();
        }

        [Test]
        public void should_skip_track_without_detection()
        {
            var plan = AudioTrackRetagPlanner.Plan(File(Track(0, "eng", null, 0)), Mkv, Threshold);

            plan.SkipReason.Should().Be(AudioTrackRetagSkipReason.NoMismatch);
        }

        [Test]
        public void should_not_edit_track_whose_tag_already_matches()
        {
            AudioTrackRetagPlanner.Plan(File(Track(0, "fra", "fr")), Mkv, Threshold).SkipReason.Should().Be(AudioTrackRetagSkipReason.NoMismatch);
            AudioTrackRetagPlanner.Plan(File(Track(0, "fre", "fr")), Mkv, Threshold).SkipReason.Should().Be(AudioTrackRetagSkipReason.NoMismatch);
            AudioTrackRetagPlanner.Plan(File(Track(0, "fr", "fr")), Mkv, Threshold).SkipReason.Should().Be(AudioTrackRetagSkipReason.NoMismatch);
        }

        [TestCase("und")]
        [TestCase(null)]
        [TestCase("")]
        public void should_edit_unknown_tag(string tagged)
        {
            var plan = AudioTrackRetagPlanner.Plan(File(Track(0, tagged, "fr")), Mkv, Threshold);

            plan.ShouldRetag.Should().BeTrue();
            plan.Edits[0].To.Should().Be("fre");
        }

        [Test]
        public void should_only_edit_the_mismatched_tracks_of_a_multi_track_file()
        {
            var plan = AudioTrackRetagPlanner.Plan(File(Track(0, "eng", "en"), Track(1, "eng", "fr"), Track(2, "eng", "fr", 0.3)), Mkv, Threshold);

            plan.Edits.Should().HaveCount(1);
            plan.Edits[0].StreamIndex.Should().Be(1);
        }

        [Test]
        public void should_skip_non_mkv()
        {
            AudioTrackRetagPlanner.Plan(File(Track(0, "eng", "fr")), "/tv/Series/Season 01/Series - S01E01.mp4", Threshold).SkipReason.Should().Be(AudioTrackRetagSkipReason.NotMkv);
            AudioTrackRetagPlanner.Plan(File(Track(0, "eng", "fr")), null, Threshold).SkipReason.Should().Be(AudioTrackRetagSkipReason.NotMkv);
        }

        [Test]
        public void should_accept_upper_case_extension()
        {
            AudioTrackRetagPlanner.Plan(File(Track(0, "eng", "fr")), "/tv/Series/Season 01/Series - S01E01.MKV", Threshold).ShouldRetag.Should().BeTrue();
        }

        [Test]
        public void should_skip_without_verification_record()
        {
            AudioTrackRetagPlanner.Plan(File(), Mkv, Threshold).SkipReason.Should().Be(AudioTrackRetagSkipReason.NoVerificationRecord);
            AudioTrackRetagPlanner.Plan(new EpisodeFile { AudioLanguageVerification = new List<AudioLanguageVerification>() }, Mkv, Threshold).SkipReason.Should().Be(AudioTrackRetagSkipReason.NoVerificationRecord);
        }

        [Test]
        public void should_skip_file_already_done()
        {
            var file = File(Track(0, "eng", "fr"));
            file.AudioTrackRetag = new AudioTrackRetag { Result = AudioTrackRetagResult.Done };

            AudioTrackRetagPlanner.Plan(file, Mkv, Threshold).SkipReason.Should().Be(AudioTrackRetagSkipReason.AlreadyDone);
        }

        [TestCase(AudioTrackRetagResult.Failed)]
        [TestCase(AudioTrackRetagResult.SkippedHardlinked)]
        [TestCase(AudioTrackRetagResult.SkippedContainer)]
        public void should_retry_file_with_non_final_outcome(string result)
        {
            var file = File(Track(0, "eng", "fr"));
            file.AudioTrackRetag = new AudioTrackRetag { Result = result };

            AudioTrackRetagPlanner.Plan(file, Mkv, Threshold).ShouldRetag.Should().BeTrue();
        }

        [Test]
        public void should_record_track_whose_detected_language_is_unknown()
        {
            var plan = AudioTrackRetagPlanner.Plan(File(Track(0, "eng", "xx")), Mkv, Threshold);

            plan.SkipReason.Should().Be(AudioTrackRetagSkipReason.None);
            plan.ShouldRetag.Should().BeFalse();
            plan.Edits.Should().BeEmpty();
            plan.SkippedTracks.Should().HaveCount(1);
            plan.SkippedTracks[0].StreamIndex.Should().Be(0);
            plan.SkippedTracks[0].Reason.Should().Contain("xx");
        }

        [TestCase("fr", "fre")]
        [TestCase("de", "ger")]
        [TestCase("en", "eng")]
        [TestCase("es", "spa")]
        [TestCase("nl", "dut")]
        [TestCase("ja", "jpn")]
        public void should_map_detected_language_to_matroska_iso639_2_code(string detected, string expected)
        {
            var plan = AudioTrackRetagPlanner.Plan(File(Track(0, "und", detected)), Mkv, Threshold);

            plan.Edits[0].To.Should().Be(expected);
        }

        [Test]
        public void iso6392_code_should_prefer_bibliographic_form()
        {
            AudioTrackRetagPlanner.Iso6392Code(Language.French).Should().Be("fre");
            AudioTrackRetagPlanner.Iso6392Code(Language.German).Should().Be("ger");
            AudioTrackRetagPlanner.Iso6392Code(Language.English).Should().Be("eng");
            AudioTrackRetagPlanner.Iso6392Code(Language.Unknown).Should().BeNull();
        }

        [Test]
        public void languages_from_tags_should_map_distinct_known_tags()
        {
            var languages = AudioTrackRetagPlanner.LanguagesFromTags(new[] { "eng", "fre", "fra", "und", "zzz" });

            languages.Should().Equal(Language.English, Language.French, Language.Unknown);
        }

        [Test]
        public void languages_from_tags_should_handle_null()
        {
            AudioTrackRetagPlanner.LanguagesFromTags(null).Should().BeEmpty();
        }
    }
}
