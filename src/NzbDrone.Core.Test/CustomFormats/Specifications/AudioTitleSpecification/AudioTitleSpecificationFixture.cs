using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.CustomFormats.Specifications.AudioTitleSpecification
{
    [TestFixture]
    public class AudioTitleSpecificationFixture : CoreTest<Core.CustomFormats.AudioTitleSpecification>
    {
        private CustomFormatInput _input;

        [SetUp]
        public void Setup()
        {
            _input = new CustomFormatInput
            {
                EpisodeInfo = Builder<ParsedEpisodeInfo>.CreateNew().Build(),
                Series = Builder<Series>.CreateNew().Build(),
                Size = 100.Megabytes(),

                // A release whose title carries NO VFQ token, but whose audio track is labelled VFQ
                AudioTitles = new List<string> { "VFQ", "English (AAC)" },
                Filename = "Series.Title.S01E01.FRENCH.1080p.WEB.x264-NOTAG"
            };
        }

        [Test]
        public void should_match_when_an_audio_track_title_matches_regex()
        {
            Subject.Value = @"\bVFQ\b";
            Subject.Negate = false;

            Subject.IsSatisfiedBy(_input).Should().BeTrue();
        }

        [Test]
        public void should_not_match_when_no_audio_track_title_matches_regex()
        {
            Subject.Value = @"\bVFF\b";
            Subject.Negate = false;

            Subject.IsSatisfiedBy(_input).Should().BeFalse();
        }

        [Test]
        public void should_not_match_when_no_audio_titles_present()
        {
            _input.AudioTitles = new List<string>();
            Subject.Value = @"\bVFQ\b";
            Subject.Negate = false;

            Subject.IsSatisfiedBy(_input).Should().BeFalse();
        }

        [Test]
        public void should_not_match_when_audio_titles_null()
        {
            _input.AudioTitles = null;
            Subject.Value = @"\bVFQ\b";
            Subject.Negate = false;

            Subject.IsSatisfiedBy(_input).Should().BeFalse();
        }

        [Test]
        public void should_match_when_negated_and_no_audio_track_title_matches()
        {
            Subject.Value = @"\bVFF\b";
            Subject.Negate = true;

            Subject.IsSatisfiedBy(_input).Should().BeTrue();
        }
    }
}
