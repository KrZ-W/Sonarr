using System;
using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Download;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles.AudioLanguage;
using NzbDrone.Core.MediaFiles.EpisodeImport.Aggregation.Aggregators.Augmenters.Language;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.EpisodeImport.Aggregation.Aggregators.Augmenters.Language
{
    [TestFixture]
    public class AugmentLanguageFromAudioProbeFixture : CoreTest<AugmentLanguageFromAudioProbe>
    {
        private Series _series;
        private LocalEpisode _localEpisode;
        private DownloadClientItem _downloadClientItem;
        private List<AudioTrackInfo> _tracks;

        [SetUp]
        public void Setup()
        {
            _series = Builder<Series>.CreateNew()
                .With(s => s.OriginalLanguage = Core.Languages.Language.English)
                .With(s => s.QualityProfile = new QualityProfile
                {
                    MinFormatScore = 10,
                    FormatItems = new List<ProfileFormatItem>
                    {
                        new ProfileFormatItem
                        {
                            Score = 100,
                            Format = new CustomFormat("French", new Core.CustomFormats.LanguageSpecification { Value = Core.Languages.Language.French.Id })
                        }
                    }
                })
                .Build();

            _tracks = new List<AudioTrackInfo>
            {
                new AudioTrackInfo { StreamIndex = 0, Codec = "eac3", Channels = 6, Language = "eng" }
            };

            _localEpisode = new LocalEpisode
            {
                Path = "/downloads/Series.S01E01.FRENCH.1080p-GRP/episode.mkv",
                Series = _series,
                MediaInfo = new MediaInfoModel { RawStreamData = "{}", RunTime = TimeSpan.FromHours(1.5) },
                FileEpisodeInfo = new ParsedEpisodeInfo { Languages = new List<Core.Languages.Language> { Core.Languages.Language.French }, ReleaseGroup = "GRP" }
            };

            _downloadClientItem = new DownloadClientItem { DownloadId = "ABC123" };

            Mocker.GetMock<IConfigService>().SetupGet(c => c.AudioLanguageVerificationEnabled).Returns(true);
            Mocker.GetMock<IConfigService>().SetupGet(c => c.AudioLanguageVerificationEndpoint).Returns("http://whisper:9000");
            Mocker.GetMock<IConfigService>().SetupGet(c => c.AudioLanguageVerificationConfidenceThreshold).Returns(0.85);
            Mocker.GetMock<IConfigService>().SetupGet(c => c.AudioLanguageVerificationVerifyTagged).Returns(AudioLanguageVerifyTaggedMode.Never);
            Mocker.GetMock<IConfigService>().SetupGet(c => c.AudioLanguageVerificationTimeout).Returns(120);
            Mocker.GetMock<IConfigService>().SetupGet(c => c.AudioLanguageVerificationClipOffset).Returns(300);
            Mocker.GetMock<IConfigService>().SetupGet(c => c.AudioLanguageVerificationClipLength).Returns(30);

            Mocker.GetMock<IAudioTrackLayoutReader>().Setup(r => r.Read(It.IsAny<MediaInfoModel>())).Returns(() => _tracks);
            Mocker.GetMock<IHistoryService>().Setup(h => h.FindByDownloadId(It.IsAny<string>())).Returns(new List<EpisodeHistory>());
            Mocker.GetMock<ICustomFormatCalculationService>().Setup(c => c.ParseCustomFormat(It.IsAny<LocalEpisode>())).Returns(new List<CustomFormat>());

            Mocker.SetConstant<IAudioLanguageProbeCache>(new AudioLanguageProbeCache());
        }

        private void GivenProbe(string code, double confidence)
        {
            Mocker.GetMock<IAudioLanguageProbe>()
                  .Setup(p => p.Probe(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan?>()))
                  .Returns(new AudioLanguageProbeResult { LanguageCode = code, Language = code == "fr" ? Core.Languages.Language.French : Core.Languages.Language.English, Confidence = confidence });
        }

        private void VerifyProbeCount(int count)
        {
            Mocker.GetMock<IAudioLanguageProbe>()
                  .Verify(p => p.Probe(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan?>()), Times.Exactly(count));
        }

        [Test]
        public void should_return_detected_language_with_audio_probe_confidence_when_the_claim_contradicts_the_tag()
        {
            GivenProbe("fr", 0.97);

            var result = Subject.AugmentLanguage(_localEpisode, _downloadClientItem);

            result.Should().NotBeNull();
            result.Confidence.Should().Be(Confidence.AudioProbe);
            result.Languages.Should().Equal(new List<Core.Languages.Language> { Core.Languages.Language.French });

            _localEpisode.AudioLanguageTrigger.Should().Be(AudioLanguageTrigger.Contradiction);
            _localEpisode.AudioLanguageVerification.Should().HaveCount(1);
            _localEpisode.AudioLanguageVerification[0].StreamIndex.Should().Be(0);
            _localEpisode.AudioLanguageVerification[0].TaggedLanguage.Should().Be("eng");
            _localEpisode.AudioLanguageVerification[0].DetectedLanguage.Should().Be("fr");
            _localEpisode.AudioLanguageVerification[0].Confidence.Should().Be(0.97);
            _localEpisode.AudioLanguageVerification[0].Source.Should().Be("whisper");
            _localEpisode.AudioLanguageVerification[0].ProbedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        }

        [Test]
        public void audio_probe_confidence_should_outrank_media_info()
        {
            ((int)Confidence.AudioProbe).Should().BeGreaterThan((int)Confidence.MediaInfo);
            Subject.Order.Should().BeGreaterThan(new AugmentLanguageFromMediaInfo().Order);
        }

        [Test]
        public void should_keep_tags_of_tracks_that_were_not_probed()
        {
            _tracks.Add(new AudioTrackInfo { StreamIndex = 1, Codec = "aac", Channels = 2, Language = "und" });
            _localEpisode.FileEpisodeInfo.Languages = new List<Core.Languages.Language>();
            GivenProbe("fr", 0.95);

            var result = Subject.AugmentLanguage(_localEpisode, _downloadClientItem);

            _localEpisode.AudioLanguageTrigger.Should().Be(AudioLanguageTrigger.Unknown);
            VerifyProbeCount(1);
            result.Languages.Should().BeEquivalentTo(new[] { Core.Languages.Language.English, Core.Languages.Language.French });
        }

        [Test]
        public void should_fall_through_below_threshold_but_still_record_the_outcome()
        {
            GivenProbe("fr", 0.5);

            Subject.AugmentLanguage(_localEpisode, _downloadClientItem).Should().BeNull();

            _localEpisode.AudioLanguageVerification.Should().HaveCount(1);
            _localEpisode.AudioLanguageVerification[0].Confidence.Should().Be(0.5);
        }

        [Test]
        public void should_fall_through_when_the_probe_fails()
        {
            Mocker.GetMock<IAudioLanguageProbe>()
                  .Setup(p => p.Probe(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan?>()))
                  .Returns((AudioLanguageProbeResult)null);

            Subject.AugmentLanguage(_localEpisode, _downloadClientItem).Should().BeNull();

            _localEpisode.AudioLanguageTrigger.Should().Be(AudioLanguageTrigger.Contradiction);
            _localEpisode.AudioLanguageVerification.Should().HaveCount(1);
            _localEpisode.AudioLanguageVerification[0].DetectedLanguage.Should().BeNull();
        }

        [Test]
        public void should_not_probe_when_disabled()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.AudioLanguageVerificationEnabled).Returns(false);

            Subject.AugmentLanguage(_localEpisode, _downloadClientItem).Should().BeNull();

            VerifyProbeCount(0);
            _localEpisode.AudioLanguageVerification.Should().BeNull();
        }

        [Test]
        public void should_not_probe_existing_files_on_rescan()
        {
            _localEpisode.ExistingFile = true;

            Subject.AugmentLanguage(_localEpisode, null).Should().BeNull();

            VerifyProbeCount(0);
            _localEpisode.AudioLanguageVerification.Should().BeNull();
        }

        [Test]
        public void should_not_probe_without_media_info()
        {
            _localEpisode.MediaInfo = null;

            Subject.AugmentLanguage(_localEpisode, _downloadClientItem).Should().BeNull();

            VerifyProbeCount(0);
        }

        [Test]
        public void should_never_call_the_detector_when_no_custom_format_has_a_language_condition()
        {
            // Real probe wired in so the assertion is on the HTTP client itself.
            Mocker.SetConstant<IAudioLanguageProbe>(Mocker.Resolve<WhisperAsrAudioLanguageProbe>());
            _series.QualityProfile.Value.FormatItems = new List<ProfileFormatItem>();
            _tracks.Add(new AudioTrackInfo { StreamIndex = 1, Codec = "aac", Channels = 2, Language = "und" });

            Subject.AugmentLanguage(_localEpisode, _downloadClientItem).Should().BeNull();

            Mocker.GetMock<IHttpClient>().Verify(c => c.Post(It.IsAny<HttpRequest>()), Times.Never());
            Mocker.GetMock<IAudioClipExtractor>().Verify(e => e.Extract(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()), Times.Never());
            _localEpisode.AudioLanguageVerification.Should().BeNull();
        }

        [Test]
        public void should_probe_once_per_layout_per_pack_and_reuse_for_other_files()
        {
            GivenProbe("fr", 0.97);

            var first = Subject.AugmentLanguage(_localEpisode, _downloadClientItem);

            var second = new LocalEpisode
            {
                Path = "/downloads/Series.S01E01.FRENCH.1080p-GRP/episode2.mkv",
                Series = _series,
                MediaInfo = _localEpisode.MediaInfo,
                FileEpisodeInfo = _localEpisode.FileEpisodeInfo
            };

            var result = Subject.AugmentLanguage(second, _downloadClientItem);

            VerifyProbeCount(1);
            first.Languages.Should().Equal(result.Languages);
            second.AudioLanguageVerification.Should().HaveCount(1);
            second.AudioLanguageVerification[0].DetectedLanguage.Should().Be("fr");
        }

        [Test]
        public void should_probe_again_for_a_different_layout_in_the_same_pack()
        {
            GivenProbe("fr", 0.97);
            Subject.AugmentLanguage(_localEpisode, _downloadClientItem);

            _tracks = new List<AudioTrackInfo> { new AudioTrackInfo { StreamIndex = 0, Codec = "aac", Channels = 2, Language = "eng" } };
            Subject.AugmentLanguage(_localEpisode, _downloadClientItem);

            VerifyProbeCount(2);
        }

        [Test]
        public void should_key_the_cache_on_the_parent_folder_without_a_download_client_item()
        {
            GivenProbe("fr", 0.97);

            Subject.AugmentLanguage(_localEpisode, null);
            Subject.AugmentLanguage(_localEpisode, null);

            VerifyProbeCount(1);
        }

        [Test]
        public void should_take_the_claim_from_the_grab_history_row()
        {
            _localEpisode.FileEpisodeInfo.Languages = new List<Core.Languages.Language>();
            Mocker.GetMock<IHistoryService>()
                  .Setup(h => h.FindByDownloadId("ABC123"))
                  .Returns(new List<EpisodeHistory>
                  {
                      new EpisodeHistory { EventType = EpisodeHistoryEventType.Grabbed, Date = DateTime.UtcNow, Languages = new List<Core.Languages.Language> { Core.Languages.Language.French } }
                  });
            GivenProbe("fr", 0.97);

            Subject.AugmentLanguage(_localEpisode, _downloadClientItem);

            _localEpisode.AudioLanguageTrigger.Should().Be(AudioLanguageTrigger.Contradiction);
        }

        [Test]
        public void should_probe_on_impending_rejection_without_any_claim()
        {
            // The mocked calculator returns no formats, so the tagged eng track scores 0 < MinFormatScore 10.
            _localEpisode.FileEpisodeInfo.Languages = new List<Core.Languages.Language>();
            GivenProbe("en", 0.97);

            var result = Subject.AugmentLanguage(_localEpisode, _downloadClientItem);

            _localEpisode.AudioLanguageTrigger.Should().Be(AudioLanguageTrigger.ImpendingRejection);
            result.Languages.Should().Equal(new List<Core.Languages.Language> { Core.Languages.Language.English });
        }

        [Test]
        public void should_not_probe_when_the_tagged_languages_already_satisfy_the_formats()
        {
            _localEpisode.FileEpisodeInfo.Languages = new List<Core.Languages.Language>();
            Mocker.GetMock<ICustomFormatCalculationService>()
                  .Setup(c => c.ParseCustomFormat(It.IsAny<LocalEpisode>()))
                  .Returns(new List<CustomFormat> { _series.QualityProfile.Value.FormatItems[0].Format });

            Subject.AugmentLanguage(_localEpisode, _downloadClientItem).Should().BeNull();

            VerifyProbeCount(0);
            _localEpisode.Languages.Should().BeEmpty();
        }

        [Test]
        public void should_restore_languages_after_pre_computing_the_score()
        {
            _localEpisode.FileEpisodeInfo.Languages = new List<Core.Languages.Language>();
            GivenProbe("fr", 0.97);

            Subject.AugmentLanguage(_localEpisode, _downloadClientItem);

            _localEpisode.Languages.Should().BeEmpty();
        }

        [Test]
        public void should_verify_tagged_tracks_for_listed_release_groups()
        {
            _tracks = new List<AudioTrackInfo> { new AudioTrackInfo { StreamIndex = 0, Codec = "aac", Channels = 2, Language = "fra" } };
            Mocker.GetMock<IConfigService>().SetupGet(c => c.AudioLanguageVerificationVerifyTagged).Returns(AudioLanguageVerifyTaggedMode.ForReleaseGroups);
            Mocker.GetMock<IConfigService>().SetupGet(c => c.AudioLanguageVerificationVerifyTaggedGroups).Returns("grp");
            GivenProbe("en", 0.99);

            var result = Subject.AugmentLanguage(_localEpisode, _downloadClientItem);

            _localEpisode.AudioLanguageTrigger.Should().Be(AudioLanguageTrigger.PositiveVerification);
            result.Languages.Should().Equal(new List<Core.Languages.Language> { Core.Languages.Language.English });
        }

        [Test]
        public void should_fall_through_with_one_warning_when_anything_inside_throws()
        {
            Mocker.GetMock<IHistoryService>()
                  .Setup(h => h.FindByDownloadId(It.IsAny<string>()))
                  .Throws(new InvalidOperationException("db gone"));

            AugmentLanguageResult result = null;
            Action act = () => result = Subject.AugmentLanguage(_localEpisode, _downloadClientItem);

            act.Should().NotThrow();
            result.Should().BeNull();
            VerifyProbeCount(0);
            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
