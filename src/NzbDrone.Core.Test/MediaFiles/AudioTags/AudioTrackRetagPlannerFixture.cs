using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.AudioLanguage;
using NzbDrone.Core.MediaFiles.AudioTags;
using NzbDrone.Core.Parser;
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
        [TestCase("cs", "cze")]
        [TestCase("el", "gre")]
        [TestCase("ro", "rum")]
        [TestCase("zh", "chi")]
        public void should_map_detected_language_to_matroska_iso639_2_code(string detected, string expected)
        {
            var plan = AudioTrackRetagPlanner.Plan(File(Track(0, "und", detected)), Mkv, Threshold);

            plan.Edits[0].To.Should().Be(expected);
        }

        [TestCase("bod", "tib")]
        [TestCase("ces", "cze")]
        [TestCase("cym", "wel")]
        [TestCase("deu", "ger")]
        [TestCase("ell", "gre")]
        [TestCase("eus", "baq")]
        [TestCase("fas", "per")]
        [TestCase("fra", "fre")]
        [TestCase("hye", "arm")]
        [TestCase("isl", "ice")]
        [TestCase("kat", "geo")]
        [TestCase("mkd", "mac")]
        [TestCase("mri", "mao")]
        [TestCase("msa", "may")]
        [TestCase("mya", "bur")]
        [TestCase("nld", "dut")]
        [TestCase("ron", "rum")]
        [TestCase("slk", "slo")]
        [TestCase("sqi", "alb")]
        [TestCase("zho", "chi")]
        public void bibliographic_table_should_hold_the_twenty_standard_pairs(string terminology, string bibliographic)
        {
            AudioTrackRetagPlanner.MatroskaBibliographicCodes.Should().HaveCount(20);
            AudioTrackRetagPlanner.MatroskaBibliographicCodes[terminology].Should().Be(bibliographic);
        }

        [Test]
        public void iso6392_code_should_prefer_bibliographic_form()
        {
            AudioTrackRetagPlanner.Iso6392Code(Language.French).Should().Be("fre");
            AudioTrackRetagPlanner.Iso6392Code(Language.German).Should().Be("ger");
            AudioTrackRetagPlanner.Iso6392Code(Language.Dutch).Should().Be("dut");
            AudioTrackRetagPlanner.Iso6392Code(Language.Czech).Should().Be("cze");
            AudioTrackRetagPlanner.Iso6392Code(Language.Greek).Should().Be("gre");
            AudioTrackRetagPlanner.Iso6392Code(Language.Chinese).Should().Be("chi");
        }

        [TestCase("English", "eng")]
        [TestCase("Spanish", "spa")]
        [TestCase("Italian", "ita")]
        [TestCase("Japanese", "jpn")]
        [TestCase("Russian", "rus")]
        [TestCase("Polish", "pol")]
        [TestCase("Swedish", "swe")]
        public void iso6392_code_should_fall_back_to_terminology_code_when_no_bibliographic_form_exists(string name, string expected)
        {
            AudioTrackRetagPlanner.Iso6392Code(IsoLanguages.FindByName(name).Language).Should().Be(expected);
        }

        [Test]
        public void iso6392_code_should_be_null_for_unknown()
        {
            AudioTrackRetagPlanner.Iso6392Code(Language.Unknown).Should().BeNull();
        }

        [Test]
        public void iso6392_code_should_be_deterministic()
        {
            for (var i = 0; i < 50; i++)
            {
                AudioTrackRetagPlanner.Iso6392Code(Language.German).Should().Be("ger");
            }
        }

        // ----- Languages after a retag -----

        private static List<AudioTrackRetagTrack> Rewritten(params (int Index, string From, string To)[] edits)
        {
            return edits.Select(e => new AudioTrackRetagTrack { StreamIndex = e.Index, From = e.From, To = e.To }).ToList();
        }

        [Test]
        public void reconcile_should_keep_the_imported_languages_in_the_common_case()
        {
            var verification = new List<AudioLanguageVerification> { Track(0, "eng", "fr") };
            var stored = new List<Language> { Language.French };

            AudioTrackRetagPlanner.ReconcileLanguages(stored, Rewritten((0, "eng", "fre")), verification, Threshold).Should().Equal(Language.French);
        }

        [Test]
        public void reconcile_should_add_the_detected_language_when_the_import_stored_the_tag()
        {
            var verification = new List<AudioLanguageVerification> { Track(0, "eng", "fr") };
            var stored = new List<Language> { Language.English };

            AudioTrackRetagPlanner.ReconcileLanguages(stored, Rewritten((0, "eng", "fre")), verification, Threshold).Should().Equal(Language.French);
        }

        [Test]
        public void reconcile_should_keep_a_language_another_track_still_carries()
        {
            var verification = new List<AudioLanguageVerification> { Track(0, "eng", "en"), Track(1, "eng", "fr") };
            var stored = new List<Language> { Language.English, Language.French };

            AudioTrackRetagPlanner.ReconcileLanguages(stored, Rewritten((1, "eng", "fre")), verification, Threshold).Should().Equal(Language.English, Language.French);
        }

        [Test]
        public void reconcile_should_never_add_unknown_for_an_und_track()
        {
            var verification = new List<AudioLanguageVerification> { Track(0, "und", null, 0), Track(1, "eng", "fr") };
            var stored = new List<Language> { Language.French };

            var result = AudioTrackRetagPlanner.ReconcileLanguages(stored, Rewritten((1, "eng", "fre")), verification, Threshold);

            result.Should().Equal(Language.French);
            result.Should().NotContain(Language.Unknown);
        }

        [Test]
        public void reconcile_should_retain_the_verified_language_of_a_track_that_could_not_be_written()
        {
            // track 1 verified as a language without an ISO 639-2 code: not rewritten, but the import's decision stands
            var verification = new List<AudioLanguageVerification> { Track(0, "eng", "fr"), Track(1, "eng", "xx") };
            var stored = new List<Language> { Language.French, Language.English };

            AudioTrackRetagPlanner.ReconcileLanguages(stored, Rewritten((0, "eng", "fre")), verification, Threshold).Should().Equal(Language.French, Language.English);
        }

        [Test]
        public void reconcile_should_keep_stored_languages_when_nothing_was_rewritten()
        {
            var stored = new List<Language> { Language.French };

            AudioTrackRetagPlanner.ReconcileLanguages(stored, Rewritten(), null, Threshold).Should().Equal(Language.French);
        }

        [Test]
        public void reconcile_should_handle_null_stored_languages()
        {
            var verification = new List<AudioLanguageVerification> { Track(0, "eng", "fr") };

            AudioTrackRetagPlanner.ReconcileLanguages(null, Rewritten((0, "eng", "fre")), verification, Threshold).Should().Equal(Language.French);
        }
    }
}
