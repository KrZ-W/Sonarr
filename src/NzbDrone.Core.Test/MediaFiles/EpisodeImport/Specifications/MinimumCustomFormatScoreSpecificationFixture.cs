using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.MediaFiles.AudioLanguage;
using NzbDrone.Core.MediaFiles.EpisodeImport.Specifications;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.MediaFiles.EpisodeImport.Specifications
{
    [TestFixture]
    public class MinimumCustomFormatScoreSpecificationFixture : CoreTest<MinimumCustomFormatScoreSpecification>
    {
        private Series _series;
        private LocalEpisode _localEpisode;

        [SetUp]
        public void Setup()
        {
            _series = Builder<Series>.CreateNew()
                .With(s => s.QualityProfile = new QualityProfile { MinFormatScore = 0 })
                .Build();

            _localEpisode = new LocalEpisode
            {
                Path = @"/downloads/episode.mkv",
                Series = _series,
                CustomFormats = new List<CustomFormat>(),
                CustomFormatScore = 0
            };
        }

        [Test]
        public void should_accept_when_score_equals_minimum()
        {
            _localEpisode.CustomFormatScore = 0;
            Subject.IsSatisfiedBy(_localEpisode, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_when_score_above_minimum()
        {
            _localEpisode.CustomFormatScore = 5000;
            Subject.IsSatisfiedBy(_localEpisode, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_when_score_below_minimum()
        {
            _localEpisode.CustomFormatScore = -10000;
            Subject.IsSatisfiedBy(_localEpisode, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_accept_existing_file_regardless_of_score()
        {
            _localEpisode.ExistingFile = true;
            _localEpisode.CustomFormatScore = -10000;
            Subject.IsSatisfiedBy(_localEpisode, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_when_minimum_is_negative_and_score_above_it()
        {
            _series.QualityProfile.Value.MinFormatScore = -5000;
            _localEpisode.CustomFormatScore = -1000;
            Subject.IsSatisfiedBy(_localEpisode, null).Accepted.Should().BeTrue();
        }

        // krzw(audio-language-verification)
        [Test]
        public void should_say_audio_verified_when_a_probe_ran_and_the_score_is_still_too_low()
        {
            _localEpisode.CustomFormatScore = -10000;
            _localEpisode.AudioLanguageTrigger = AudioLanguageTrigger.ImpendingRejection;
            _localEpisode.AudioLanguageVerification = new List<AudioLanguageVerification>
            {
                new AudioLanguageVerification { StreamIndex = 0, TaggedLanguage = "eng", DetectedLanguage = "en", Confidence = 0.97 },
                new AudioLanguageVerification { StreamIndex = 1, TaggedLanguage = "und", DetectedLanguage = "en", Confidence = 0.94 }
            };

            var decision = Subject.IsSatisfiedBy(_localEpisode, null);

            decision.Accepted.Should().BeFalse();
            decision.Message.Should().EndWith(". Audio verified (detected en 0.97, en 0.94)");
        }

        [Test]
        public void should_not_mention_verification_when_no_probe_ran()
        {
            _localEpisode.CustomFormatScore = -10000;

            Subject.IsSatisfiedBy(_localEpisode, null).Message.Should().NotContain("Audio verified");
        }

        [Test]
        public void verification_should_not_change_the_decision()
        {
            _localEpisode.CustomFormatScore = 0;
            _localEpisode.AudioLanguageTrigger = AudioLanguageTrigger.Contradiction;
            _localEpisode.AudioLanguageVerification = new List<AudioLanguageVerification>
            {
                new AudioLanguageVerification { StreamIndex = 0, TaggedLanguage = "eng", DetectedLanguage = "fr", Confidence = 0.97 }
            };

            Subject.IsSatisfiedBy(_localEpisode, null).Accepted.Should().BeTrue();
        }
    }
}
