using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
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
        public void should_accept_when_minimum_is_negative_and_score_above_it()
        {
            _series.QualityProfile.Value.MinFormatScore = -5000;
            _localEpisode.CustomFormatScore = -1000;
            Subject.IsSatisfiedBy(_localEpisode, null).Accepted.Should().BeTrue();
        }
    }
}
