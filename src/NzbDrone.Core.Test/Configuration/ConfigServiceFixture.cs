using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Configuration
{
    [TestFixture]
    public class ConfigServiceFixture : TestBase<ConfigService>
    {
        [SetUp]
        public void SetUp()
        {
        }

        [Test]
        public void Add_new_value_to_database()
        {
            const string key = "RssSyncInterval";
            const int value = 12;

            Subject.RssSyncInterval = value;

            AssertUpsert(key, value);
        }

        [Test]
        public void Get_value_should_return_default_when_no_value()
        {
            Subject.RssSyncInterval.Should().Be(15);
        }

        [Test]
        public void get_value_with_persist_should_store_default_value()
        {
            var salt = Subject.HmacSalt;
            salt.Should().NotBeNullOrWhiteSpace();
            AssertUpsert("HmacSalt", salt);
        }

        [Test]
        public void get_value_with_out_persist_should_not_store_default_value()
        {
            var interval = Subject.RssSyncInterval;
            interval.Should().Be(15);
            Mocker.GetMock<IConfigRepository>().Verify(c => c.Insert(It.IsAny<Config>()), Times.Never());
        }

        private void AssertUpsert(string key, object value)
        {
            Mocker.GetMock<IConfigRepository>().Verify(c => c.Upsert(key.ToLowerInvariant(), value.ToString()));
        }

        [Test]
        [Description("This test will use reflection to ensure each config property read/writes to a unique key")]
        public void config_properties_should_write_and_read_using_same_key()
        {
            var configProvider = Subject;
            var allProperties = typeof(ConfigService).GetProperties().Where(p => p.GetSetMethod() != null).ToList();

            var keys = new List<string>();
            var values = new List<Config>();

            Mocker.GetMock<IConfigRepository>().Setup(c => c.Upsert(It.IsAny<string>(), It.IsAny<string>())).Callback<string, string>((key, value) =>
            {
                keys.Add(key);
                values.Add(new Config { Key = key, Value = value });
            });

            Mocker.GetMock<IConfigRepository>().Setup(c => c.All()).Returns(values);

            foreach (var propertyInfo in allProperties)
            {
                object value = null;

                if (propertyInfo.PropertyType == typeof(string))
                {
                    value = Guid.NewGuid().ToString();
                }
                else if (propertyInfo.PropertyType == typeof(int))
                {
                    value = DateTime.Now.Millisecond;
                }
                else if (propertyInfo.PropertyType == typeof(bool))
                {
                    value = true;
                }
                else if (propertyInfo.PropertyType.BaseType == typeof(Enum))
                {
                    value = 0;
                }
                else if (propertyInfo.PropertyType == typeof(double))
                {
                    // krzw(audio-language-verification): a real decimal, so a culture-dependent round-trip would fail
                    value = 0.75;
                }

                propertyInfo.GetSetMethod().Invoke(configProvider, new[] { value });
                var returnValue = propertyInfo.GetGetMethod().Invoke(configProvider, null);

                if (propertyInfo.PropertyType.BaseType == typeof(Enum))
                {
                    returnValue = (int)returnValue;
                }

                returnValue.Should().Be(value, propertyInfo.Name);
            }

            keys.Should().OnlyHaveUniqueItems();
        }

        [Test]
        public void should_ignore_null_properties()
        {
            Mocker.GetMock<IConfigRepository>()
                  .Setup(v => v.Get("downloadedepisodesfolder"))
                  .Returns(new Config { Id = 1, Key = "DownloadedEpisodesFolder", Value = @"C:\test".AsOsAgnostic() });

            var dict = new Dictionary<string, object>();
            dict.Add("DownloadedEpisodesFolder", null);
            Subject.SaveConfigDictionary(dict);

            Mocker.GetMock<IConfigRepository>().Verify(c => c.Upsert("downloadedepisodesfolder", It.IsAny<string>()), Times.Never());
        }

        // krzw(audio-language-verification): the image runs with a real culture (fr_FR uses a comma decimal);
        // double settings must survive save -> read back through the API dictionary path.
        [Test]
        public void double_settings_should_round_trip_in_a_comma_decimal_culture()
        {
            var previousCulture = CultureInfo.CurrentCulture;
            var previousUiCulture = CultureInfo.CurrentUICulture;

            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
                CultureInfo.CurrentUICulture = new CultureInfo("fr-FR");

                var values = new List<Config>();

                Mocker.GetMock<IConfigRepository>().Setup(c => c.Upsert(It.IsAny<string>(), It.IsAny<string>())).Callback<string, string>((key, value) =>
                {
                    values.RemoveAll(v => v.Key == key);
                    values.Add(new Config { Key = key, Value = value });
                });
                Mocker.GetMock<IConfigRepository>().Setup(c => c.All()).Returns(values);

                var doubleProperties = typeof(ConfigService).GetProperties().Where(p => p.PropertyType == typeof(double) && p.GetSetMethod() != null).ToList();
                doubleProperties.Should().NotBeEmpty();

                foreach (var propertyInfo in doubleProperties)
                {
                    Subject.SaveConfigDictionary(new Dictionary<string, object> { { propertyInfo.Name, 0.75 } });

                    values.Single(v => v.Key == propertyInfo.Name.ToLowerInvariant()).Value.Should().Be("0.75", propertyInfo.Name);
                    propertyInfo.GetGetMethod().Invoke(Subject, null).Should().Be(0.75, propertyInfo.Name);

                    // saving the same value again must be a no-op (the equality check is culture-neutral too)
                    Mocker.GetMock<IConfigRepository>().Invocations.Clear();
                    Subject.SaveConfigDictionary(new Dictionary<string, object> { { propertyInfo.Name, 0.75 } });
                    Mocker.GetMock<IConfigRepository>().Verify(c => c.Upsert(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
                CultureInfo.CurrentUICulture = previousUiCulture;
            }
        }

        [Test]
        public void threshold_written_in_the_current_culture_by_an_older_build_should_still_read_back()
        {
            var previousCulture = CultureInfo.CurrentCulture;

            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("fr-FR");

                Mocker.GetMock<IConfigRepository>().Setup(c => c.All()).Returns(new List<Config>
                {
                    new Config { Key = "audiolanguageverificationconfidencethreshold", Value = "0,6" }
                });

                Subject.AudioLanguageVerificationConfidenceThreshold.Should().Be(0.6);
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
            }
        }
    }
}
