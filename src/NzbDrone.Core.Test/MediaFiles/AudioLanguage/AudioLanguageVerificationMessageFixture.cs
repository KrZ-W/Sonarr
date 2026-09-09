using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles.AudioLanguage;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.AudioLanguage
{
    [TestFixture]
    public class AudioLanguageVerificationMessageFixture : CoreTest
    {
        private LocalEpisode _localEpisode;

        [SetUp]
        public void Setup()
        {
            _localEpisode = new LocalEpisode
            {
                AudioLanguageTrigger = AudioLanguageTrigger.Contradiction,
                AudioLanguageVerification = new List<AudioLanguageVerification>
                {
                    new AudioLanguageVerification { StreamIndex = 0, TaggedLanguage = "eng", DetectedLanguage = "en", Confidence = 0.97 },
                    new AudioLanguageVerification { StreamIndex = 1, TaggedLanguage = "und", DetectedLanguage = "en", Confidence = 0.94 }
                }
            };
        }

        [Test]
        public void should_describe_verified_absence_of_the_wanted_language()
        {
            AudioLanguageVerificationMessage.Describe(_localEpisode, Language.French)
                .Should().Be("Audio verified: no French track (detected en 0.97, en 0.94)");
        }

        [Test]
        public void should_describe_without_wanted_language()
        {
            AudioLanguageVerificationMessage.Describe(_localEpisode, null).Should().Be("Audio verified (detected en 0.97, en 0.94)");
        }

        [Test]
        public void should_mark_failed_probes()
        {
            _localEpisode.AudioLanguageVerification[1].DetectedLanguage = null;

            AudioLanguageVerificationMessage.Describe(_localEpisode, Language.French).Should().Contain("detected en 0.97, ?");
        }

        [TestCase(AudioLanguageTrigger.None)]
        [TestCase(AudioLanguageTrigger.PositiveVerification)]
        public void should_return_null_unless_a_rejection_trigger_fired(AudioLanguageTrigger trigger)
        {
            _localEpisode.AudioLanguageTrigger = trigger;

            AudioLanguageVerificationMessage.Describe(_localEpisode, Language.French).Should().BeNull();
        }

        [Test]
        public void should_return_null_without_results()
        {
            _localEpisode.AudioLanguageVerification = new List<AudioLanguageVerification>();
            AudioLanguageVerificationMessage.Describe(_localEpisode, Language.French).Should().BeNull();

            _localEpisode.AudioLanguageVerification = null;
            AudioLanguageVerificationMessage.Describe(_localEpisode, Language.French).Should().BeNull();
        }
    }
}
