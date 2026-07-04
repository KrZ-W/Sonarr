using System;
using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]
    public class PriorityCustomFormatUpgradeFixture : CoreTest<UpgradeDiskSpecification>
    {
        private UpgradeDiskSpecification _upgradeDisk;
        private RemoteEpisode _parseResult;
        private EpisodeFile _existingFile;
        private CustomFormat _priorityFormat;
        private CustomFormat _regularFormat;

        [SetUp]
        public void Setup()
        {
            Mocker.Resolve<UpgradableSpecification>();
            _upgradeDisk = Mocker.Resolve<UpgradeDiskSpecification>();

            // Create a "priority" custom format and a "regular" one
            _priorityFormat = new CustomFormat("Priority Format", new ResolutionSpecification { Value = (int)Resolution.R1080p }) { Id = 1 };
            _regularFormat = new CustomFormat("Regular Format", new ResolutionSpecification { Value = (int)Resolution.R720p }) { Id = 2 };

            // Use SDTV which is BELOW the Bluray1080p cutoff, so cutoff is not met
            _existingFile = new EpisodeFile { Quality = new QualityModel(Quality.SDTV, new Revision(version: 1)), DateAdded = DateTime.Now, Languages = new List<Language> { Language.English } };

            var singleEpisodeList = new List<Episode>
            {
                new Episode { EpisodeFile = _existingFile, EpisodeFileId = 1 }
            };

            var fakeSeries = Builder<Series>.CreateNew()
                .With(c => c.QualityProfile = new QualityProfile
                {
                    UpgradeAllowed = true,
                    Cutoff = Quality.Bluray1080p.Id,
                    Items = Qualities.QualityFixture.GetDefaultQualities(),
                    MinFormatScore = 0,
                    CutoffFormatScore = 10000, // High cutoff so CF cutoff isn't met
                    MinUpgradeFormatScore = 0, // No minimum upgrade increment required
                    FormatItems = new List<ProfileFormatItem>
                    {
                        new ProfileFormatItem { Format = _priorityFormat, Score = 100, Priority = true },
                        new ProfileFormatItem { Format = _regularFormat, Score = 100, Priority = false }
                    }
                })
                .Build();

            _parseResult = new RemoteEpisode
            {
                Series = fakeSeries,

                // New release also below cutoff but same quality level
                ParsedEpisodeInfo = new ParsedEpisodeInfo { Quality = new QualityModel(Quality.SDTV, new Revision(version: 1)), Languages = new List<Language> { Language.English } },
                Episodes = singleEpisodeList,
                CustomFormats = new List<CustomFormat>()
            };

            Mocker.GetMock<ICustomFormatCalculationService>()
                .Setup(x => x.ParseCustomFormat(It.IsAny<EpisodeFile>()))
                .Returns(new List<CustomFormat>());
        }

        [Test]
        public void should_upgrade_when_new_release_has_priority_format_and_existing_does_not()
        {
            Mocker.GetMock<ICustomFormatCalculationService>()
                .Setup(x => x.ParseCustomFormat(It.IsAny<EpisodeFile>()))
                .Returns(new List<CustomFormat>());

            _parseResult.CustomFormats = new List<CustomFormat> { _priorityFormat };

            _upgradeDisk.IsSatisfiedBy(_parseResult, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_not_upgrade_on_priority_format_when_profile_does_not_allow_upgrades()
        {
            _parseResult.Series.QualityProfile.Value.UpgradeAllowed = false;

            Mocker.GetMock<ICustomFormatCalculationService>()
                .Setup(x => x.ParseCustomFormat(It.IsAny<EpisodeFile>()))
                .Returns(new List<CustomFormat>());

            _parseResult.CustomFormats = new List<CustomFormat> { _priorityFormat };

            _upgradeDisk.IsSatisfiedBy(_parseResult, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_not_upgrade_when_priority_format_score_is_equal()
        {
            Mocker.GetMock<ICustomFormatCalculationService>()
                .Setup(x => x.ParseCustomFormat(It.IsAny<EpisodeFile>()))
                .Returns(new List<CustomFormat> { _priorityFormat });

            _parseResult.CustomFormats = new List<CustomFormat> { _priorityFormat };

            _upgradeDisk.IsSatisfiedBy(_parseResult, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_not_upgrade_from_priority_to_regular_format_even_if_regular_has_higher_score()
        {
            _parseResult.Series.QualityProfile.Value.FormatItems = new List<ProfileFormatItem>
            {
                new ProfileFormatItem { Format = _priorityFormat, Score = 50, Priority = true },
                new ProfileFormatItem { Format = _regularFormat, Score = 200, Priority = false }
            };

            Mocker.GetMock<ICustomFormatCalculationService>()
                .Setup(x => x.ParseCustomFormat(It.IsAny<EpisodeFile>()))
                .Returns(new List<CustomFormat> { _priorityFormat });

            _parseResult.CustomFormats = new List<CustomFormat> { _regularFormat };

            _upgradeDisk.IsSatisfiedBy(_parseResult, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_upgrade_to_higher_priority_score()
        {
            var higherPriorityFormat = new CustomFormat("Higher Priority", new ResolutionSpecification { Value = (int)Resolution.R2160p }) { Id = 3 };

            _parseResult.Series.QualityProfile.Value.FormatItems = new List<ProfileFormatItem>
            {
                new ProfileFormatItem { Format = _priorityFormat, Score = 50, Priority = true },
                new ProfileFormatItem { Format = higherPriorityFormat, Score = 100, Priority = true }
            };

            Mocker.GetMock<ICustomFormatCalculationService>()
                .Setup(x => x.ParseCustomFormat(It.IsAny<EpisodeFile>()))
                .Returns(new List<CustomFormat> { _priorityFormat });

            _parseResult.CustomFormats = new List<CustomFormat> { higherPriorityFormat };

            _upgradeDisk.IsSatisfiedBy(_parseResult, null).Accepted.Should().BeTrue();
        }
    }
}
