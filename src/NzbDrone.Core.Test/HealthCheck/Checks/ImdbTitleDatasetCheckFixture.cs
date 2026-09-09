using System;
using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv.ImdbTitles;

namespace NzbDrone.Core.Test.HealthCheck.Checks
{
    [TestFixture]
    public class ImdbTitleDatasetCheckFixture : CoreTest<ImdbTitleDatasetCheck>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString(It.IsAny<string>()))
                  .Returns("Some Warning Message");

            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
                  .Returns("Some Warning Message");

            Mocker.GetMock<IConfigService>().SetupGet(c => c.ImdbTitleProviderEnabled).Returns(true);
            Mocker.GetMock<IConfigService>().SetupGet(c => c.ImdbTitleProviderRefreshInterval).Returns(7);
        }

        private void GivenIndexBuilt(DateTime builtAt)
        {
            Mocker.GetMock<IImdbAkasDatabase>().SetupGet(d => d.Exists).Returns(true);
            Mocker.GetMock<IImdbAkasDatabase>().Setup(d => d.GetInfo()).Returns(new ImdbAkasDatabaseInfo { BuiltAt = builtAt, RowCount = 1 });
        }

        [Test]
        public void should_return_ok_when_disabled_even_without_index()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.ImdbTitleProviderEnabled).Returns(false);
            Mocker.GetMock<IImdbAkasDatabase>().SetupGet(d => d.Exists).Returns(false);

            Subject.Check().ShouldBeOk();
        }

        [Test]
        public void should_return_warning_when_enabled_and_index_missing()
        {
            Mocker.GetMock<IImdbAkasDatabase>().SetupGet(d => d.Exists).Returns(false);

            Subject.Check().ShouldBeWarning();
        }

        [Test]
        public void should_return_warning_when_index_is_unreadable()
        {
            Mocker.GetMock<IImdbAkasDatabase>().SetupGet(d => d.Exists).Returns(true);
            Mocker.GetMock<IImdbAkasDatabase>().Setup(d => d.GetInfo()).Returns((ImdbAkasDatabaseInfo)null);

            Subject.Check().ShouldBeWarning();
        }

        [Test]
        public void should_return_ok_when_index_is_fresh()
        {
            GivenIndexBuilt(DateTime.UtcNow.AddDays(-10));

            Subject.Check().ShouldBeOk();
        }

        [Test]
        public void should_return_warning_when_index_is_older_than_twice_the_interval()
        {
            GivenIndexBuilt(DateTime.UtcNow.AddDays(-15));

            Subject.Check().ShouldBeWarning();
        }

        [Test]
        public void should_treat_interval_below_one_day_as_one_day()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.ImdbTitleProviderRefreshInterval).Returns(0);
            GivenIndexBuilt(DateTime.UtcNow.AddDays(-3));

            Subject.Check().ShouldBeWarning();
        }
    }
}
